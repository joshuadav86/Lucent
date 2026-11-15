using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Lucent.Settings;
using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>How volume leveling evens out the music (Options > Sound).</summary>
public enum LevelingMode
{
    /// <summary>Every song plays as it was mastered.</summary>
    Off,

    /// <summary>Every song is turned up or down to about the same loudness.</summary>
    Track,

    /// <summary>Each album is turned up or down as a whole, so its quiet songs stay quieter than its loud ones.</summary>
    Album,
}

/// <summary>
/// Volume leveling: how far to turn each song up or down so they all play at about the same loudness, as ReplayGain
/// players do. The amount comes from the file's ReplayGain tags when it has them; otherwise Lucent measures the song
/// itself (EBU R 128 integrated loudness, with <see cref="LoudnessMeter"/>) in the background the first time it's
/// lined up or played, and remembers it. It's only ever a change of volume: nothing is compressed, and files aren't
/// changed.
/// </summary>
/// <remarks>
/// Songs are brought to −18 LUFS (ReplayGain 2's level). A song is never turned up past the point its loudest
/// sample would clip. In Album mode an album is the songs in one folder with the same album tag; its loudness is the
/// average power of all of them, until all are measured each plays at its own song's level. Measuring decodes a song
/// at many times real time on one low-priority thread; it's only done for files Lucent decodes itself (not videos or
/// what only libVLC plays), and remembered in Loudness.json beside the settings, by path, size and date.
/// </remarks>
public sealed class VolumeLeveling
{
    /// <summary>The loudness every song is brought to, in LUFS: ReplayGain 2's reference level.</summary>
    public const double TargetLufs = -18;

    // How far a song is ever turned up or down, in dB.
    private const double MostBoost = 12;
    private const double MostCut = 24;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ConcurrentDictionary<string, TagGains> _tags = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Measurement> _measured;
    private readonly ConcurrentDictionary<string, byte> _unmeasurable = new(StringComparer.Ordinal);
    private readonly BlockingCollection<string> _toMeasure = new();
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private readonly string _cachePath;
    private readonly object _saveGate = new();
    private Thread? _worker;
    private volatile bool _dirty;

    public VolumeLeveling()
        : this(Path.Combine(AppDataFolder.Path, "Loudness.json"))
    {
    }

    internal VolumeLeveling(string cachePath)
    {
        _cachePath = cachePath;
        _measured = new ConcurrentDictionary<string, Measurement>(Load(cachePath), StringComparer.Ordinal);
    }

    public LevelingMode Mode { get; set; }

    /// <summary>
    /// Raised on a background thread when a song has been measured, with its path: a song playing or lined up with
    /// gain 1 because it wasn't known yet can now be given its gain (see <see cref="GainFor"/>).
    /// </summary>
    public event Action<string>? Measured;

    /// <summary>
    /// How much to turn <paramref name="path"/> up or down, as a factor (1 leaves it as it is). When the song isn't
    /// known yet it's 1, and the song is measured in the background: <see cref="Measured"/> says when to ask again.
    /// </summary>
    public float GainFor(string path)
    {
        if (Mode == LevelingMode.Off || path.Contains("://", StringComparison.Ordinal))
        {
            return 1f;
        }

        var tags = TagsOf(path);
        double? gain;
        double peak;
        if (Mode == LevelingMode.Album && !double.IsNaN(tags.AlbumGain))
        {
            gain = tags.AlbumGain;
            peak = tags.AlbumPeak;
        }
        else if (Mode == LevelingMode.Album && AlbumLevel(path, tags) is { } album)
        {
            gain = album.Gain;
            peak = album.Peak;
        }
        else if (!double.IsNaN(tags.TrackGain))
        {
            gain = tags.TrackGain;
            peak = tags.TrackPeak;
        }
        else if (MeasurementOf(path) is { } measured)
        {
            gain = TargetLufs - measured.Lufs;
            peak = measured.Peak;
        }
        else
        {
            Measure(path);
            return 1f;
        }

        return ToFactor(gain.Value, peak);
    }

    /// <summary>The factor for a gain in dB, kept within reason and below the point the song's peak would clip.</summary>
    internal static float ToFactor(double gainDb, double peak)
    {
        if (!double.IsFinite(gainDb))
        {
            return 1f;
        }

        var factor = Math.Pow(10, Math.Clamp(gainDb, -MostCut, MostBoost) / 20);
        if (factor > 1 && double.IsFinite(peak) && peak > 0)
        {
            // Turned up only as far as its loudest sample allows (and never turned down for being loud already).
            factor = Math.Max(1, Math.Min(factor, 1 / peak));
        }

        return (float)factor;
    }

    /// <summary>Starts measuring <paramref name="path"/> in the background, unless it's known or can't be.</summary>
    public void Measure(string path)
    {
        if (Mode == LevelingMode.Off || _unmeasurable.ContainsKey(path) || MeasurementOf(path) is not null
            || !double.IsNaN(TagsOf(path).TrackGain) || !_queued.TryAdd(path, 0))
        {
            return;
        }

        lock (_saveGate)
        {
            if (_worker is null)
            {
                _worker = new Thread(Work) { IsBackground = true, Name = "Volume leveling", Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        _toMeasure.Add(path);
    }

    // ---- Tags ----

    private readonly record struct TagGains(double TrackGain, double TrackPeak, double AlbumGain, double AlbumPeak, string Album);

    private TagGains TagsOf(string path) => _tags.GetOrAdd(path, ReadTags);

    private static TagGains ReadTags(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path, Library.TagWriter.TagLibMimeType(path), TagLib.ReadStyle.None);
            var tag = file.Tag;
            return new TagGains(tag.ReplayGainTrackGain, tag.ReplayGainTrackPeak, tag.ReplayGainAlbumGain,
                tag.ReplayGainAlbumPeak, tag.Album ?? string.Empty);
        }
        catch (Exception)
        {
            return new TagGains(double.NaN, double.NaN, double.NaN, double.NaN, string.Empty);
        }
    }

    // ---- Albums ----

    // The album's gain and peak from every song in it, once all are measured or tagged; null until then (and the rest
    // are put in to be measured).
    private (double Gain, double Peak)? AlbumLevel(string path, TagGains tags)
    {
        if (tags.Album.Length == 0 || Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch (Exception)
        {
            return null;
        }

        double power = 0, seconds = 0, peak = 0;
        var complete = true;
        foreach (var file in files)
        {
            if (!AudioReaderFactory.IsSupported(file) || Video.VideoFiles.IsVideo(file))
            {
                continue;
            }

            var other = TagsOf(file);
            if (!string.Equals(other.Album, tags.Album, StringComparison.Ordinal))
            {
                continue;
            }

            double lufs, length, top;
            if (MeasurementOf(file) is { } measured)
            {
                (lufs, length, top) = (measured.Lufs, measured.Seconds, measured.Peak);
            }
            else if (!double.IsNaN(other.TrackGain))
            {
                // A tagged song's loudness from its gain; its length doesn't matter much, so it counts as a usual song's.
                (lufs, length, top) = (TargetLufs - other.TrackGain, 240, double.IsNaN(other.TrackPeak) ? 0 : other.TrackPeak);
            }
            else
            {
                if (!_unmeasurable.ContainsKey(file))
                {
                    complete = false;
                    Measure(file);
                }

                continue;
            }

            power += Math.Pow(10, lufs / 10) * length;
            seconds += length;
            peak = Math.Max(peak, top);
        }

        if (!complete || seconds <= 0 || power <= 0)
        {
            return null;
        }

        return (TargetLufs - 10 * Math.Log10(power / seconds), peak);
    }

    // ---- Measuring ----

    /// <summary>What a song measured as: its integrated loudness, its loudest sample, and how long it is, with the file's size and date.</summary>
    internal sealed record Measurement(double Lufs, double Peak, double Seconds, long Size, long Modified);

    private Measurement? MeasurementOf(string path)
    {
        if (!_measured.TryGetValue(path, out var measured))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length == measured.Size && info.LastWriteTimeUtc.Ticks == measured.Modified)
            {
                return measured;
            }
        }
        catch (Exception)
        {
        }

        // The file has changed since (or gone): measured again when it's next asked for.
        _measured.TryRemove(path, out _);
        return null;
    }

    private void Work()
    {
        foreach (var path in _toMeasure.GetConsumingEnumerable())
        {
            try
            {
                if (Mode != LevelingMode.Off && MeasurementOf(path) is null)
                {
                    if (MeasureFile(path) is { } measured)
                    {
                        _measured[path] = measured;
                        _dirty = true;
                        SaveSoon();
                        Measured?.Invoke(path);
                    }
                    else
                    {
                        _unmeasurable[path] = 0;
                    }
                }
            }
            catch (Exception)
            {
                _unmeasurable[path] = 0;
            }
            finally
            {
                _queued.TryRemove(path, out _);
            }
        }
    }

    /// <summary>Measures a whole file, or returns null for one Lucent doesn't decode itself or that's silent.</summary>
    internal static Measurement? MeasureFile(string path)
    {
        if (Video.VideoFiles.IsVideo(path) || AudioReaderFactory.LibVlcOnlyExtensions.Contains(Path.GetExtension(path)))
        {
            return null;
        }

        var info = new FileInfo(path);
        using var reader = AudioReaderFactory.OpenQuickly(path);
        return Measure(reader.ToSampleProvider(), info.Length, info.LastWriteTimeUtc.Ticks);
    }

    internal static Measurement? Measure(ISampleProvider samples, long size, long modified)
    {
        var channels = Math.Max(1, samples.WaveFormat.Channels);
        var meter = new LoudnessMeter(samples.WaveFormat.SampleRate);
        var buffer = new float[4096 * channels];
        var left = new float[4096];
        var right = new float[4096];
        long frames = 0;
        var peak = 0f;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            var count = read / channels;
            for (var i = 0; i < count; i++)
            {
                // Mono plays the same on both sides; more channels than two are measured by their front pair.
                var l = buffer[i * channels];
                var r = buffer[i * channels + Math.Min(1, channels - 1)];
                left[i] = l;
                right[i] = r;
                for (var c = 0; c < channels; c++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[i * channels + c]));
                }
            }

            meter.Write(left.AsSpan(0, count), right.AsSpan(0, count));
            frames += count;
        }

        var lufs = meter.Read().Integrated;
        return double.IsFinite(lufs)
            ? new Measurement(lufs, peak, (double)frames / samples.WaveFormat.SampleRate, size, modified)
            : null;
    }

    // ---- Remembering ----

    private static Dictionary<string, Measurement> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Measurement>>(File.ReadAllText(path), JsonOptions) ?? new();
            }
        }
        catch (Exception)
        {
            // Measured again as they're played.
        }

        return new();
    }

    private int _saveScheduled;

    // Saves a few seconds after the last song measured, so measuring an album writes the file once or twice, not every song.
    private void SaveSoon()
    {
        if (Interlocked.Exchange(ref _saveScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _saveScheduled, 0);
            Save();
        }, TaskScheduler.Default);
    }

    /// <summary>Writes what's been measured, if anything's new.</summary>
    public void Save()
    {
        if (!_dirty)
        {
            return;
        }

        lock (_saveGate)
        {
            try
            {
                _dirty = false;
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var temp = _cachePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, Measurement>(_measured), JsonOptions));
                File.Move(temp, _cachePath, overwrite: true);
            }
            catch (Exception)
            {
                _dirty = true;
            }
        }
    }
}
