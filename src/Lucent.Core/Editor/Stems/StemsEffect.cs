using WinampInspiredVisualizer.Editor.Effects;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>The four stems, with the names people know them by.</summary>
public static class StemNames
{
    /// <summary>The model's names for the stems, in the order the editor shows them.</summary>
    public static readonly string[] All = { "vocals", "drums", "bass", "other" };

    public static string Title(string source) => source switch
    {
        "vocals" => "Vocals",
        "drums" => "Drums",
        "bass" => "Bass",
        "other" => "Instruments",
        _ => char.ToUpperInvariant(source[0]) + source[1..],
    };

    /// <summary>"vocals", "drums and bass", "vocals, drums and bass".</summary>
    public static string List(IReadOnlyList<string> sources)
    {
        var names = sources.Select(source => source == "other" ? "the other instruments" : source).ToList();
        return names.Count switch
        {
            0 => "nothing",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }
}

/// <summary>
/// Keeps some stems of the music and takes out the rest: "Remove vocals" for a karaoke version, "Keep vocals"
/// for an a cappella, or any mix of the four.
/// </summary>
/// <remarks>
/// Splitting is slow, so the last split is remembered: choosing different stems for the same audio, or
/// saving the stems after keeping some, doesn't split it again.
/// </remarks>
public sealed class StemsEffect : IAudioEffect
{
    public StemsEffect(IEnumerable<string> keep) => Keep = StemNames.All.Where(keep.Contains).ToArray();

    /// <summary>The stems to keep, in the editor's order.</summary>
    public IReadOnlyList<string> Keep { get; }

    public string Description
    {
        get
        {
            var removed = StemNames.All.Where(source => !Keep.Contains(source)).ToList();
            return removed.Count == 1 ? $"Remove {StemNames.List(removed)}" : $"Keep {StemNames.List(Keep)}";
        }
    }

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var stems = StemCache.Split(input, context.Report, context.Cancellation);
        var splitter = StemModel.Splitter;
        context.Note = $"{Description}: done, split on the {splitter.Device}. {(OperatingSystem.IsMacOS() ? "⌘Z" : "Ctrl+Z")} undoes it.";
        return stems.Mix(Keep);
    }
}

/// <summary>Remembers the last audio split into stems, so it isn't split twice.</summary>
public static class StemCache
{
    private static readonly object Gate = new();
    private static (ulong Fingerprint, int Frames, int Rate, int Channels, StemSet Stems)? _last;

    /// <summary>Splits the audio, or gives back the stems from last time if it's the same audio.</summary>
    public static StemSet Split(AudioBuffer audio, Action<double>? progress, CancellationToken cancellation)
    {
        var fingerprint = Fingerprint(audio);
        lock (Gate)
        {
            if (_last is { } last && last.Fingerprint == fingerprint && last.Frames == audio.Frames &&
                last.Rate == audio.SampleRate && last.Channels == audio.ChannelCount)
            {
                progress?.Invoke(1);
                return last.Stems;
            }
        }

        var stems = StemModel.Splitter.Split(audio, progress, cancellation);
        lock (Gate)
        {
            _last = (fingerprint, audio.Frames, audio.SampleRate, audio.ChannelCount, stems);
        }

        return stems;
    }

    /// <summary>Lets go of the remembered stems (they can take hundreds of megabytes).</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _last = null;
        }
    }

    /// <summary>A 64-bit FNV-1a hash of every sample.</summary>
    private static ulong Fingerprint(AudioBuffer audio)
    {
        var hash = 14695981039346656037UL;
        foreach (var channel in audio.Channels)
        {
            foreach (var value in System.Runtime.InteropServices.MemoryMarshal.Cast<float, uint>(channel.AsSpan()))
            {
                hash = (hash ^ value) * 1099511628211UL;
            }
        }

        return hash;
    }
}
