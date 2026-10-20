using System.IO;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>
/// One AVS preset as a visual, the part both apps share: the running preset, how big its picture is
/// drawn, and its picture made ready for the graphics card. Each app keeps the texture it goes into and
/// draws it with Shaders/AvsDisplay.hlsl, which brings it into Lucent's picture.
/// </summary>
public sealed class AvsVisual
{
    // Shared by every AVS preset, as reg00-reg99 and gmegabuf were in AVS.
    private static readonly EelGlobals Globals = new();

    private int _audioFrame = -1;
    private uint[] _upload = Array.Empty<uint>();
    private bool _modern;

    private AvsVisual(AvsPresetRunner runner)
    {
        Runner = runner;
    }

    /// <summary>
    /// Whether AVS presets are shown modernised (the Options switch; on unless it's turned off) or as Winamp
    /// drew them. It's the app's setting rather than a visualizer's, so recordings match the screen.
    /// </summary>
    public static bool ModernLook { get; set; } = true;

    /// <summary>
    /// Whether <paramref name="source"/> is shown without Lucent's glow and tone-mapping: MilkDrop presets as MilkDrop
    /// showed them, and AVS presets as Winamp drew them (see <see cref="MilkDropLook"/> and <see cref="ModernLook"/>).
    /// </summary>
    public static bool ShownPlain(PresetSource source) =>
        (source.IsMilkDrop && !MilkDropLook.Modern) || (source.IsAvs && !ModernLook);

    public AvsPresetRunner Runner { get; }

    /// <summary>Effects that were left out of it, for the error log.</summary>
    public IReadOnlyList<string> Notes => Runner.Warnings;

    /// <summary>Whether <see cref="Notes"/> have gone in the error log yet (once a session, when it's first shown).</summary>
    public bool NotesLogged { get; set; }

    /// <summary>Whether it's on screen (so it starts from the top when it next comes on).</summary>
    public bool Showing { get; private set; }

    /// <summary>
    /// Reads a preset (on a compile thread). Throws <see cref="FormatException"/> if the file isn't an AVS preset;
    /// effects Lucent doesn't have are left out and listed in <see cref="Notes"/>.
    /// </summary>
    public static AvsVisual Load(PresetSource source)
    {
        var preset = AvsPreset.Parse(source.Code, source.Name);
        return new AvsVisual(new AvsPresetRunner(preset, Globals, Path.GetDirectoryName(source.FilePath)));
    }

    /// <summary>
    /// How big the preset's picture is drawn for a <paramref name="width"/> x <paramref name="height"/> view.
    /// As Winamp drew it, it's about the size of AVS's window in its day, its pixels stretched to fill the screen.
    /// Modernised, it's drawn sharper (up to the view's own size, more on higher detail settings) with lines and
    /// dots thickened to match, then smoothed and lit by Lucent.
    /// </summary>
    public static (int Width, int Height, float DotScale) PictureSize(int width, int height, float quality, bool modern)
    {
        var lines = (modern, quality) switch
        {
            (false, < 0.5f) => 360,
            (false, < 1.5f) => 480,
            (false, < 2.5f) => 600,
            (false, _) => 720,
            (true, < 0.5f) => 540,
            (true, < 1.5f) => 720,
            (true, < 2.5f) => 900,
            (true, _) => 1080,
        };
        var pictureHeight = Math.Clamp(Math.Min(lines, height), 16, 4096);
        var pictureWidth = Math.Clamp((int)Math.Round((double)pictureHeight * width / Math.Max(height, 1)), 16, 8192);

        // AVS's presets were made in windows about 480 lines high; thicken lines and dots to look the same.
        var dotScale = modern ? Math.Max(1f, pictureHeight / 480f) : 1f;
        return (pictureWidth, pictureHeight, dotScale);
    }

    /// <summary>
    /// Moves the preset on for this frame (<paramref name="frame"/> counts the visualizer's frames, so the sound
    /// is taken once a frame however many times it's drawn), and returns the picture to upload: <see cref="Runner"/>'s
    /// size, 32-bit BGRA. Its alpha says how to show it: 255 modernised, 0 as Winamp drew it.
    /// </summary>
    public uint[] Draw(AudioAnalyzer analyzer, int frame, int viewWidth, int viewHeight, double time, float quality, bool modern)
    {
        if (!Showing)
        {
            // Coming on screen: from the top, as AVS started a preset when it was loaded.
            Runner.Restart();
            Showing = true;
        }

        if (_audioFrame != frame)
        {
            _audioFrame = frame;
            Runner.Audio.Update(analyzer);
        }

        var (width, height, dotScale) = PictureSize(viewWidth, viewHeight, quality, modern);
        var changed = Runner.Render(width, height, time, dotScale);
        var picture = Runner.Picture;
        if (changed || modern != _modern || _upload.Length != picture.Length)
        {
            _modern = modern;
            if (_upload.Length != picture.Length)
            {
                _upload = new uint[picture.Length];
            }

            var alpha = modern ? 0xff000000u : 0u;
            var upload = _upload;
            Parallel.For(0, 8, part =>
            {
                var start = picture.Length * part / 8;
                var end = picture.Length * (part + 1) / 8;
                for (var i = start; i < end; i++)
                {
                    upload[i] = ((uint)picture[i] & 0xffffffu) | alpha;
                }
            });
        }

        return _upload;
    }

    /// <summary>Off screen: it starts over next time it's shown.</summary>
    public void Hide() => Showing = false;
}
