using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Lucent.Visualizer;

/// <summary>
/// The title and artist of what's playing, for visuals that show them. The player sets them (<see cref="Set"/>) on the
/// UI thread whenever the song changes; the visuals read them from their own thread (<see cref="Take"/>) and hand every
/// visual the letters themselves, not a picture of them, so each visual draws the names in its own lettering
/// (captionLetter in Common.hlsli).
/// <para>
/// Accents are taken off and curly quotes and long dashes made plain, so a lettering that has only the basic Latin
/// letters can still draw nearly every name. Letters with no plain form (Japanese, say) are passed on as they are.
/// </para>
/// </summary>
public sealed class SongCaption
{
    /// <summary>The most letters a line holds; longer names are cut short.</summary>
    public const int MaxLetters = 128;

    /// <summary>The lines: the title, then the artist.</summary>
    public const int Lines = 2;

    private readonly object _gate = new();
    private int[] _title = [];
    private int[] _artist = [];
    private int _version;
    private long _changedAt = Stopwatch.GetTimestamp();

    /// <summary>Records what's playing; null or empty for a line that has nothing to show.</summary>
    public void Set(string? title, string? artist)
    {
        var titleLetters = Letters(title);
        var artistLetters = Letters(artist);
        lock (_gate)
        {
            if (titleLetters.AsSpan().SequenceEqual(_title) && artistLetters.AsSpan().SequenceEqual(_artist))
            {
                return;
            }

            _title = titleLetters;
            _artist = artistLetters;
            _version++;
            _changedAt = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Says how many letters each line has and how many seconds ago the names last changed (so a visual can bring new
    /// ones in). When they've changed since <paramref name="version"/>, also writes their letters into
    /// <paramref name="letters"/> (<see cref="MaxLetters"/> by <see cref="Lines"/>, the title's row first, 0 past each
    /// line's end), moves <paramref name="version"/> on and returns true, for the renderer to send them to the GPU.
    /// </summary>
    public bool Take(ref int version, float[] letters, out int titleLength, out int artistLength, out float age)
    {
        lock (_gate)
        {
            titleLength = _title.Length;
            artistLength = _artist.Length;
            age = (float)Stopwatch.GetElapsedTime(_changedAt).TotalSeconds;
            if (version == _version)
            {
                return false;
            }

            Array.Clear(letters);
            for (var i = 0; i < _title.Length; i++)
            {
                letters[i] = _title[i];
            }

            for (var i = 0; i < _artist.Length; i++)
            {
                letters[MaxLetters + i] = _artist[i];
            }

            version = _version;
            return true;
        }
    }

    /// <summary>A line's letters as Unicode numbers: made plain, runs of spaces closed up, and cut at <see cref="MaxLetters"/>.</summary>
    public static int[] Letters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // Split each accented letter into the letter and its accents, drop the accents (only the ones Latin, Greek and
        // Cyrillic letters take, so a Japanese name keeps its marks), and put what's left back together.
        var plain = new StringBuilder(text.Length);
        foreach (var c in Normalized(text, NormalizationForm.FormD))
        {
            if (c is >= '\u0300' and <= '\u036F' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            plain.Append(Plain(c));
        }

        var letters = new List<int>(Math.Min(plain.Length, MaxLetters));
        var space = false;
        foreach (var rune in Normalized(plain.ToString(), NormalizationForm.FormC).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                space = letters.Count > 0;
                continue;
            }

            if (space)
            {
                if (letters.Count >= MaxLetters - 1)
                {
                    break;
                }

                letters.Add(' ');
                space = false;
            }

            if (letters.Count >= MaxLetters)
            {
                break;
            }

            letters.Add(rune.Value);
        }

        return [.. letters];
    }

    /// <summary>The text in the given normal form, or as it is when it can't be put in one (a broken surrogate pair, say).</summary>
    private static string Normalized(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    /// <summary>The plain form of a letter or mark that has one; the letter itself otherwise.</summary>
    private static string Plain(char c) => c switch
    {
        '‘' or '’' or '‚' or '‛' or '′' or '´' or '`' => "'",
        '“' or '”' or '„' or '‟' or '″' or '«' or '»' => "\"",
        '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => "-",
        '…' => "...",
        '×' => "x",
        'ß' => "ss",
        'Æ' => "AE",
        'æ' => "ae",
        'Œ' => "OE",
        'œ' => "oe",
        'Ø' => "O",
        'ø' => "o",
        'Ł' => "L",
        'ł' => "l",
        'Đ' => "D",
        'đ' => "d",
        'Ð' => "D",
        'ð' => "d",
        'Þ' => "TH",
        'þ' => "th",
        'ı' => "i",
        ' ' or ' ' or ' ' => " ",
        _ => c.ToString(),
    };
}
