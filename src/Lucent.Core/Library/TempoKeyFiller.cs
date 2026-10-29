using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Lucent.Audio;
using Lucent.Editor;
using Lucent.Editor.Dsp;
using Lucent.Settings;
using NAudio.Wave;

namespace Lucent.Library;

/// <summary>
/// Fills in the BPM and key of songs whose files don't have them, by listening to them the way Detect in Edit info
/// does (Options › Library, off until switched on). Each song that plays is listened to, and if asked the whole
/// library too. Songs are listened to one at a time, on one core at the lowest priority, and the library is gone
/// through slowly, resting between songs, so it never takes much of the computer. Only blank fields are written: a
/// BPM or key a file already has is never changed. A song is written once it's no longer playing, because saving
/// the file that's playing makes the music stop for a moment.
/// </summary>
public sealed class TempoKeyFiller : IDisposable
{
    /// <summary>Longer files are DJ mixes and live sets, which have no one tempo or key, and would take a lot of memory.</summary>
    public static readonly TimeSpan LongestSong = TimeSpan.FromMinutes(20);

    // After each song in the library it rests this many times as long as the song took: a quarter of one core at most.
    private const double RestFactor = 3;
    private static readonly TimeSpan ShortestRest = TimeSpan.FromSeconds(1);

    // A song whose file is in use (by another app, or Lucent's own crossfade) is tried again this often, this many times.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(20);
    private const int MostTries = 6;

    // Files being written, so the library's folder watcher doesn't rescan everything for each one.
    private static readonly ConcurrentDictionary<string, DateTime> Writing = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, bool> _isBusy;
    private readonly Func<string, Func<Task>, Task> _runReleased;
    private readonly Action<Action> _post;
    private readonly string _checkedPath;
    private readonly object _gate = new();

    // Guarded by _gate.
    private readonly HashSet<string> _checked;
    private readonly List<string> _played = new();
    private List<string> _library = new();
    private int _libraryNext;
    private int _checkedSinceSave;
    private DateTime _restUntil;
    private bool _onPlay;
    private bool _scanLibrary;
    private CancellationTokenSource? _listening;
    private bool _listeningToLibrary;

    // UI thread only.
    private readonly List<Found> _waiting = new();
    private int _filled;

    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    /// <param name="isBusy">Whether the player has the file open: playing it, or lined up to play next.</param>
    /// <param name="runReleased">The player's RunWithFileReleasedAsync, which the quick swap at the end of a save runs in.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public TempoKeyFiller(Func<string, bool> isBusy, Func<string, Func<Task>, Task> runReleased, Action<Action> post)
    {
        _isBusy = isBusy;
        _runReleased = runReleased;
        _post = post;
        _checkedPath = Path.Combine(AppDataFolder.Path, "tempo-key-checked.txt");
        _checked = LoadChecked(_checkedPath);
        _thread = new Thread(Run) { IsBackground = true, Name = "Tempo and key" };
        try
        {
            _thread.Priority = ThreadPriority.Lowest;
        }
        catch (Exception)
        {
            // Not every system lets a thread's priority be set; one core and the rests still keep it light.
        }

        _thread.Start();
    }

    /// <summary>Raised on the UI thread after songs' BPM or key were written, so the lists can show them.</summary>
    public event EventHandler<TagsSavedEventArgs>? Filled;

    /// <summary>Raised on the UI thread when <see cref="Status"/> may have changed.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Whether songs are listened to as they play.</summary>
    public bool OnPlay
    {
        get
        {
            lock (_gate)
            {
                return _onPlay;
            }
        }
        set => Switch(value, ScanLibrary);
    }

    /// <summary>Whether the library is gone through too, slowly (only while <see cref="OnPlay"/> is on).</summary>
    public bool ScanLibrary
    {
        get
        {
            lock (_gate)
            {
                return _scanLibrary;
            }
        }
        set => Switch(OnPlay, value);
    }

    /// <summary>"Filled in 12 songs. 3,214 left to check in the library." or empty, for under the switches.</summary>
    public string Status
    {
        get
        {
            int left;
            bool scanning;
            lock (_gate)
            {
                left = _library.Count - _libraryNext;
                scanning = _onPlay && _scanLibrary;
            }

            var parts = new List<string>();
            if (_filled > 0)
            {
                parts.Add(_filled == 1 ? "Filled in 1 song." : $"Filled in {_filled:N0} songs.");
            }

            if (scanning)
            {
                parts.Add(left switch
                {
                    0 => "Every song in the library has been checked.",
                    1 => "1 song left to check in the library.",
                    _ => $"{left:N0} songs left to check in the library.",
                });
            }

            return string.Join(" ", parts);
        }
    }

    /// <summary>True while Lucent is writing this file's BPM or key (or a copy of it next to it), for the folder watcher.</summary>
    public static bool IsWriting(string path)
    {
        if (Writing.IsEmpty)
        {
            return false;
        }

        foreach (var suffix in new[] { TagWriter.TempSuffix, TagWriter.BackupSuffix })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
            }
        }

        return Writing.TryGetValue(path, out var until) && DateTime.UtcNow < until;
    }

    /// <summary>A song has started playing (call on the UI thread). It's listened to first, ahead of the library.</summary>
    public void Played(string path)
    {
        WriteWaiting();
        if (!IsCandidate(path))
        {
            return;
        }

        lock (_gate)
        {
            if (!_onPlay || _checked.Contains(path))
            {
                return;
            }

            _played.Remove(path);
            _played.Insert(0, path);
        }

        _wake.Set();
    }

    /// <summary>The library's songs, after their tags are read. Those missing a BPM or key are gone through in this order.</summary>
    public void SetLibrary(IEnumerable<LibraryItem> items)
    {
        var missing = items
            .Where(item => item.Track is { } track && (track.Tags.Bpm == 0 || track.Tags.Key.Length == 0)
                && track.Duration <= LongestSong && IsCandidate(item.Path))
            .Select(item => item.Path)
            .ToList();
        lock (_gate)
        {
            _library = missing.Where(path => !_checked.Contains(path)).ToList();
            _libraryNext = 0;
        }

        _wake.Set();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate)
        {
            _listening?.Cancel();
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        SaveChecked();
    }

    /// <summary>
    /// What to write into a file: its BPM and key where it has none, found by listening to it. Null when there's
    /// nothing to fill in, or nothing could be heard. Takes a few seconds; <paramref name="cores"/> limits how many it uses.
    /// </summary>
    public static TagChanges? Listen(string path, CancellationToken cancel, int cores = 1)
    {
        bool needsBpm, needsKey;
        using (var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), TagLib.ReadStyle.Average))
        {
            if (file.Properties is { } properties && (properties.Duration > LongestSong
                || properties.MediaTypes.HasFlag(TagLib.MediaTypes.Video) && properties.VideoWidth > 0))
            {
                return null;
            }

            needsBpm = file.Tag.BeatsPerMinute == 0;
            needsKey = string.IsNullOrWhiteSpace(file.Tag.InitialKey);
        }

        if (!needsBpm && !needsKey)
        {
            return null;
        }

        var music = MusicAnalysis.Analyze(LoadMono(path, cancel), cancel, cores);
        var changes = new TagChanges();
        if (needsBpm && music.Grid is { } grid)
        {
            changes.Fields[TagField.Bpm] = TagFields.NumberText((uint)Math.Round(grid.Tempo));
        }

        // A guess isn't written: an unsure key is often the relative major or minor, and a wrong key is worse than none.
        if (needsKey && music.Key is { IsSure: true } key)
        {
            changes.Fields[TagField.Key] = key.TagText;
        }

        return changes.IsEmpty ? null : changes;
    }

    // Local music files Lucent can write tags into. (A video in an .mp4 is left out once it's looked inside.)
    private static bool IsCandidate(string path) =>
        !path.Contains("://") && TagWriter.CanWrite(path) && AudioReaderFactory.IsSupported(path);

    private void Switch(bool onPlay, bool scanLibrary)
    {
        lock (_gate)
        {
            if (_onPlay == onPlay && _scanLibrary == scanLibrary)
            {
                return;
            }

            _onPlay = onPlay;
            _scanLibrary = scanLibrary;
            if (!onPlay)
            {
                // Switched off: stop listening now, and forget the songs played so far.
                _played.Clear();
                _listening?.Cancel();
            }
            else if (!scanLibrary && _listeningToLibrary)
            {
                _listening?.Cancel();
            }
        }

        _wake.Set();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Run()
    {
        var waits = new[] { _wake, _stop.Token.WaitHandle };
        while (!_stop.IsCancellationRequested)
        {
            var next = Next(out var fromLibrary, out var wait);
            if (next is null)
            {
                WaitHandle.WaitAny(waits, wait);
                continue;
            }

            var listening = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            lock (_gate)
            {
                _listening = listening;
                _listeningToLibrary = fromLibrary;
            }

            var took = Stopwatch.StartNew();
            var done = true;
            TagChanges? changes = null;
            try
            {
                changes = Listen(next, listening.Token);
            }
            catch (OperationCanceledException)
            {
                // Switched off or closing: it's listened to again another time.
                done = false;
            }
            catch (Exception)
            {
                // Damaged, missing, or in a format that can't be read: there's nothing to fill in.
            }
            finally
            {
                lock (_gate)
                {
                    _listening = null;
                }

                listening.Dispose();
            }

            if (fromLibrary)
            {
                lock (_gate)
                {
                    _restUntil = DateTime.UtcNow + TimeSpan.FromTicks(Math.Max(ShortestRest.Ticks, (long)(took.Elapsed.Ticks * RestFactor)));
                    if (!done && _libraryNext > 0 && _library[_libraryNext - 1] == next)
                    {
                        _libraryNext--;
                    }
                }
            }

            if (!done)
            {
                continue;
            }

            if (changes is null)
            {
                MarkChecked(next);
            }
            else
            {
                _post(() =>
                {
                    _waiting.RemoveAll(found => found.Path == next && !found.Saving);
                    _waiting.Add(new Found(next, changes));
                    WriteWaiting();
                });
            }

            _post(() => StatusChanged?.Invoke(this, EventArgs.Empty));
        }

        SaveChecked();
    }

    // The next song to listen to: the latest one played, else the library's next once the rest is over.
    private string? Next(out bool fromLibrary, out TimeSpan wait)
    {
        lock (_gate)
        {
            fromLibrary = false;
            wait = Timeout.InfiniteTimeSpan;
            if (!_onPlay)
            {
                return null;
            }

            while (_played.Count > 0)
            {
                var played = _played[0];
                _played.RemoveAt(0);
                if (!_checked.Contains(played))
                {
                    return played;
                }
            }

            if (!_scanLibrary)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (now < _restUntil)
            {
                wait = _restUntil - now;
                return null;
            }

            while (_libraryNext < _library.Count)
            {
                var song = _library[_libraryNext++];
                if (!_checked.Contains(song))
                {
                    fromLibrary = true;
                    return song;
                }
            }

            return null;
        }
    }

    // Writes what was found into the songs that aren't playing. UI thread.
    private async void WriteWaiting()
    {
        foreach (var found in _waiting.Where(found => !found.Saving && !_isBusy(found.Path)).ToList())
        {
            found.Saving = true;
            var path = found.Path;
            Writing[path] = DateTime.MaxValue;
            try
            {
                var prepared = await Task.Run(() =>
                {
                    // Only what's still blank: the info may have been edited since the song was listened to.
                    using (var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), TagLib.ReadStyle.None))
                    {
                        if (file.Tag.BeatsPerMinute != 0)
                        {
                            found.Changes.Fields.Remove(TagField.Bpm);
                        }

                        if (!string.IsNullOrWhiteSpace(file.Tag.InitialKey))
                        {
                            found.Changes.Fields.Remove(TagField.Key);
                        }
                    }

                    return found.Changes.IsEmpty ? null : TagWriter.Prepare(path, found.Changes);
                });

                if (prepared is not null)
                {
                    try
                    {
                        await _runReleased(path, () => Task.Run(() => TagWriter.Commit(prepared)));
                    }
                    catch
                    {
                        TagWriter.Discard(prepared);
                        throw;
                    }

                    _filled++;
                    Filled?.Invoke(this, new TagsSavedEventArgs(new[] { path }));
                }

                _waiting.Remove(found);
                MarkChecked(path);
            }
            catch (IOException ex) when (TagWriter.IsInUse(ex) && ++found.Tries < MostTries)
            {
                // Open in another app, or still fading out: try again in a little while.
                found.Saving = false;
                _ = Task.Delay(RetryAfter).ContinueWith(_ => _post(WriteWaiting), TaskScheduler.Default);
            }
            catch (Exception)
            {
                // Read-only, on a drive that's gone, or a kind of file that can't hold them: leave it as it is.
                _waiting.Remove(found);
                MarkChecked(path);
            }
            finally
            {
                // The folder watcher hears about the swap a moment after it's done.
                Writing[path] = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MarkChecked(string path)
    {
        bool save;
        lock (_gate)
        {
            save = _checked.Add(path) && ++_checkedSinceSave >= 20;
        }

        if (save)
        {
            SaveChecked();
        }
    }

    // The songs already listened to, one path a line, so the library isn't gone through again each time Lucent starts.
    private static HashSet<string> LoadChecked(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new HashSet<string>(File.ReadLines(path).Where(line => line.Length > 0), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable: the songs are listened to again, and nothing already in them is changed.
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveChecked()
    {
        string[] lines;
        lock (_gate)
        {
            if (_checkedSinceSave == 0)
            {
                return;
            }

            lines = _checked.ToArray();
            _checkedSinceSave = 0;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_checkedPath)!);
            var temp = _checkedPath + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, _checkedPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saved next time.
        }
    }

    /// <summary>The whole file mixed to one channel, which is all the tempo and key need and half the memory of stereo.</summary>
    private static AudioBuffer LoadMono(string path, CancellationToken cancel)
    {
        using var reader = AudioReaderFactory.Open(path);
        var samples = reader.ToSampleProvider();
        var channels = samples.WaveFormat.Channels;
        var expected = reader.WaveFormat.BlockAlign > 0 ? reader.Length / reader.WaveFormat.BlockAlign : 0;
        var rate = samples.WaveFormat.SampleRate;
        var most = (long)(LongestSong.TotalSeconds + 10) * rate;
        var mono = new float[(int)Math.Clamp(expected + rate, rate, most)];
        var chunk = new float[16384 * channels];
        var frames = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var read = samples.Read(chunk, 0, chunk.Length) / channels;
            if (read == 0)
            {
                break;
            }

            if (frames + read > mono.Length)
            {
                if (mono.Length >= most)
                {
                    break;
                }

                Array.Resize(ref mono, (int)Math.Min(most, Math.Max(frames + read, mono.Length * 3L / 2)));
                read = Math.Min(read, mono.Length - frames);
            }

            for (var i = 0; i < read; i++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    sum += chunk[i * channels + c];
                }

                mono[frames + i] = sum / channels;
            }

            frames += read;
        }

        Array.Resize(ref mono, frames);
        return new AudioBuffer(new[] { mono }, rate);
    }

    private sealed class Found
    {
        public Found(string path, TagChanges changes)
        {
            Path = path;
            Changes = changes;
        }

        public string Path { get; }

        public TagChanges Changes { get; }

        public bool Saving { get; set; }

        public int Tries { get; set; }
    }
}
