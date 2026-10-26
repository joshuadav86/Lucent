using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Lucent.Mac.Editor;
using Lucent.Editor;
using Lucent.Editor.Dsp;

namespace Lucent.Mac;

/// <summary>
/// Markers (named spots in the song, shown as flags over the waveform) and the analysis card, which measures
/// the levels, loudness and spectrum of the selection and can point out every spot where it clips.
/// </summary>
public partial class EditorWindow
{
    /// <summary>The markers menu lists up to this many to jump to.</summary>
    private const int ListedMarkers = 30;

    /// <summary>What the analysis card measured, for "Show clipping".</summary>
    private FrameRange _analysisRange;

    // ---- Markers ----

    private void AddMarker(int frame)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        var name = NextMarkerName(session);
        var index = session.AddMarker(SnapFrame(frame), name);
        ShowMarkers();
        ShowStatus($"Added \"{session.Markers[index].Name}\" at {Format(session.Markers[index].Frame)}. " +
                   "Drag its flag to move it, or right-click it to rename it. [ and ] go from marker to marker.");
    }

    private static string NextMarkerName(EditSession session)
    {
        var number = session.Markers.Count + 1;
        while (session.Markers.Any(m => m.Name == $"Marker {number}"))
        {
            number++;
        }

        return $"Marker {number}";
    }

    private void ShowMarkers()
    {
        if (_session is { } session)
        {
            Waveform.Markers = session.Markers;
            UpdateHistory();
        }
    }

    /// <summary>Clicking a marker's flag puts the cursor there (and plays on from it, if playing).</summary>
    private void GoToMarker(int index)
    {
        if (_session is not { } session || index < 0 || index >= session.Markers.Count)
        {
            return;
        }

        var marker = session.Markers[index];
        Waveform.ClearSelection();
        Waveform.CursorFrame = marker.Frame;
        Waveform.KeepInView(marker.Frame);
        if (_player.IsPlaying)
        {
            PlayFrom(marker.Frame, session.Audio.Frames, _previewThrough);
        }

        ShowStatus($"{marker.Name}  ·  {Format(marker.Frame)}");
    }

    private void MoveMarker(int index, int frame)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        session.MoveMarker(index, frame);
        ShowMarkers();
    }

    /// <summary>Goes to the next marker after the cursor (or the playhead), or the one before it.</summary>
    private void GoToNextMarker(bool forward)
    {
        if (_session is not { } session || session.Markers.Count == 0)
        {
            ShowStatus("There are no markers yet. M adds one at the cursor.");
            return;
        }

        var from = _player.IsPlaying ? _player.PositionFrame : Waveform.CursorFrame;
        // A little slack, so pressing [ while playing just past a marker goes to the one before it.
        var slack = _player.IsPlaying ? session.Audio.SampleRate / 2 : 0;
        var markers = session.Markers.Select((m, i) => (Marker: m, Index: i)).ToList();
        var next = forward
            ? markers.FirstOrDefault(m => m.Marker.Frame > from)
            : markers.LastOrDefault(m => m.Marker.Frame < from - slack);
        if (next.Marker is null)
        {
            ShowStatus(forward ? "There are no more markers after this." : "There are no markers before this.");
            return;
        }

        GoToMarker(next.Index);
    }

    /// <summary>The menu for a marker's flag, when it's right-clicked.</summary>
    private void ShowMarkerMenu(int index)
    {
        if (_session is not { } session || index < 0 || index >= session.Markers.Count)
        {
            return;
        }

        var marker = session.Markers[index];
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        void Add(string header, Action action, bool enabled = true)
        {
            var item = MenuEntry(header, enabled: enabled);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        menu.Items.Add(MenuEntry($"{marker.Name}  ·  {Format(marker.Frame)}", enabled: false));
        Add("Go here", () => GoToMarker(index));
        Add("Play from here", () => Play(marker.Frame, session.Audio.Frames));
        var end = index + 1 < session.Markers.Count ? session.Markers[index + 1].Frame : session.Audio.Frames;
        Add(index + 1 < session.Markers.Count ? "Select to the next marker" : "Select to the end", () =>
            Waveform.Selection = new FrameRange(marker.Frame, end - marker.Frame), end > marker.Frame);
        menu.Items.Add(new Separator());
        Add("Rename…", async () => await RenameMarkerAsync(index));
        Add("Delete marker", () =>
        {
            session.RemoveMarker(index);
            ShowMarkers();
            ShowStatus($"Deleted \"{marker.Name}\". ⌘Z brings it back.");
        });
        menu.Open(Waveform);
    }

    private async Task RenameMarkerAsync(int index)
    {
        if (_session is not { } session || index < 0 || index >= session.Markers.Count)
        {
            return;
        }

        var name = await PromptWindow.AskAsync(this, "Rename marker", "A name for this spot, such as Chorus or Guitar solo.", "Rename",
            session.Markers[index].Name, answer => answer.Length == 0 ? "Type a name." : null);
        if (name is not null)
        {
            session.RenameMarker(index, name);
            ShowMarkers();
        }
    }

    private void OnMarkers(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var menu = MenuAbove();
        MenuItem Add(ItemsControl parent, string header, Action action, string? gesture = null, bool enabled = true, string? tip = null)
        {
            var item = MenuEntry(header, gesture, enabled, tip);
            item.Click += (_, _) => action();
            parent.Items.Add(item);
            return item;
        }

        var markers = session.Markers;
        var any = markers.Count > 0;
        Add(menu, "Add a marker at the cursor", () => AddMarker(_player.IsPlaying ? _player.PositionFrame
            : Selection.IsEmpty ? Waveform.CursorFrame : Selection.Start), "M", tip: "Press M while playing to mark spots in time with the music");
        Add(menu, "Previous marker", () => GoToNextMarker(forward: false), "[", any);
        Add(menu, "Next marker", () => GoToNextMarker(forward: true), "]", any);
        if (any)
        {
            menu.Items.Add(new Separator());
            foreach (var (marker, index) in markers.Select((m, i) => (m, i)).Take(ListedMarkers))
            {
                Add(menu, $"{marker.Name}  ·  {Format(marker.Frame)}", () => GoToMarker(index));
            }

            if (markers.Count > ListedMarkers)
            {
                menu.Items.Add(MenuEntry($"…and {markers.Count - ListedMarkers} more", enabled: false));
            }

            menu.Items.Add(new Separator());
            Add(menu, "Split into clips at every marker", () => Split(markers.Select(m => m.Frame).ToArray()),
                tip: "Then each part can be moved, or deleted, on its own");
            Add(menu, "Delete all markers", () =>
            {
                var count = session.RemoveMarkers(_ => true, "Delete all markers");
                ShowMarkers();
                ShowStatus($"Deleted {count} marker{(count == 1 ? string.Empty : "s")}. ⌘Z brings them back.");
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Markers aren't saved into the file", enabled: false));
        menu.Open(MarkersButton);
    }

    // ---- Analysis ----

    private async void OnAnalyze(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var audio = session.Audio;
        var range = Selection.IsEmpty ? new FrameRange(0, audio.Frames) : Selection;
        AudioStats? stats = null;
        double[]? spectrum = null;
        var done = await RunBusyAsync("Measuring…", async (progress, token) =>
        {
            (stats, spectrum) = await Task.Run(() =>
            {
                var measured = AudioAnalysis.Measure(audio, range, value => progress.Report(value * 0.9), token);
                token.ThrowIfCancellationRequested();
                return (measured, AudioAnalysis.AverageSpectrum(audio, range));
            }, token);
        });

        if (!done || stats is null)
        {
            return;
        }

        _analysisRange = range;
        var whole = range.Start == 0 && range.Length == audio.Frames;
        AnalysisHeading.Text = whole
            ? $"The whole sound  ·  {Format(audio.Frames)}"
            : $"The selection  ·  {Format(range.Start)} – {Format(range.End)}";
        AnalysisText.Text = Describe(stats, audio.ChannelCount);
        AnalysisSpectrum.SampleRate = audio.SampleRate;
        AnalysisSpectrum.Spectrum = spectrum;
        ShowClippingButton.IsEnabled = stats.ClippedSamples > 0;
        ShowStatus("Measured. The figures are for the original audio, before the listening volume.");
        AnalysisPopup.IsOpen = true;
    }

    /// <summary>The analysis as a little table, one column per channel.</summary>
    private static string Describe(AudioStats stats, int channels)
    {
        var culture = CultureInfo.CurrentCulture;
        string Db(double value, string unit = "dB") =>
            double.IsNegativeInfinity(value) ? "silent" : $"{value.ToString("+0.0;−0.0;0.0", culture)} {unit}";
        string Lufs(double? value) => value is { } v && !double.IsInfinity(v) ? $"{v.ToString("0.0", culture)} LUFS" : "too quiet or short";

        var text = new StringBuilder();
        void Row(string label, Func<int, string> value)
        {
            text.Append(label.PadRight(20));
            for (var c = 0; c < channels; c++)
            {
                text.Append(value(c).PadRight(16));
            }

            text.AppendLine();
        }

        if (channels > 1)
        {
            Row(string.Empty, c => c == 0 ? "Left" : c == 1 ? "Right" : $"Channel {c + 1}");
        }

        Row("Peak", c => Db(stats.PeakDb[c]));
        Row("True peak", c => Db(stats.TruePeakDb[c], "dBTP"));
        Row("RMS level", c => Db(stats.RmsDb[c]));
        Row("DC offset", c => $"{(stats.DcOffset[c] * 100).ToString("+0.000;−0.000;0.000", culture)}%");
        text.AppendLine();
        text.AppendLine($"{"Loudness".PadRight(20)}{Lufs(stats.IntegratedLufs)}");
        text.AppendLine($"{"Loudness range".PadRight(20)}{(stats.LoudnessRange is { } lra ? $"{lra.ToString("0.0", culture)} LU" : "–")}");
        text.AppendLine($"{"Loudest moment".PadRight(20)}{Lufs(stats.MaxMomentaryLufs)} (0.4 s), {Lufs(stats.MaxShortTermLufs)} (3 s)");
        if (stats.Correlation is { } correlation)
        {
            var meaning = correlation > 0.95 ? "almost mono" : correlation < 0 ? "out of phase: it may vanish in mono" : "healthy stereo";
            text.AppendLine($"{"Stereo correlation".PadRight(20)}{correlation.ToString("+0.00;−0.00;0.00", culture)} ({meaning})");
        }

        text.AppendLine();
        var truePeak = AudioStats.Highest(stats.TruePeakDb);
        text.Append(stats.ClippedSamples > 0
            ? $"Clipping: {stats.ClippedSamples:N0} clipped sample{(stats.ClippedSamples == 1 ? string.Empty : "s")}. Show clipping marks where."
            : truePeak > 0
                ? "No clipped samples, but the true peak is over 0 dB, so it may distort once converted. A limiter at −1 dB fixes that."
                : "No clipping.");
        return text.ToString();
    }

    private void OnShowClipping(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var spots = AudioAnalysis.FindClipping(session.Audio, _analysisRange);
        AnalysisPopup.IsOpen = false;
        Waveform.Highlights = spots;
        if (spots.Count == 0)
        {
            ShowStatus("No clipping was found.");
            return;
        }

        Waveform.ClearSelection();
        Waveform.CursorFrame = _analysisRange.Start;
        GoToNextHighlight(forward: true);
        ShowStatus($"{spots.Count} clipped spot{(spots.Count == 1 ? " is" : "s are")} marked in red. F3 goes to the next, ⇧F3 to the one before. " +
                   "A limiter, or turning the volume down, keeps it from getting worse; clipping that's already there can't be fully undone.");
    }

    /// <summary>Selects the next spot marked in red (clipping) after the cursor, or the one before it.</summary>
    private void GoToNextHighlight(bool forward)
    {
        var spots = Waveform.Highlights;
        if (spots.Count == 0)
        {
            ShowStatus("Nothing is marked. Analyze, then Show clipping, marks where the audio clips.");
            return;
        }

        var from = Selection.IsEmpty ? Waveform.CursorFrame : forward ? Selection.Start + 1 : Selection.Start;
        var next = forward
            ? spots.Cast<FrameRange?>().FirstOrDefault(s => s!.Value.Start >= from)
            : spots.Cast<FrameRange?>().LastOrDefault(s => s!.Value.Start < from);
        if (next is not { } spot)
        {
            ShowStatus(forward ? "That was the last marked spot." : "That was the first marked spot.");
            return;
        }

        // Zoom in far enough to see the spot, with a little either side.
        var context = Math.Max(spot.Length * 20, (_session?.Audio.SampleRate ?? 44100) / 20);
        if (Waveform.ViewLength > context * 40)
        {
            Waveform.Zoom(context * 4 / Waveform.ViewLength);
        }

        Waveform.Selection = spot;
        Waveform.ScrollTo(spot.Start);
        var index = spots.ToList().IndexOf(spot) + 1;
        ShowStatus($"Marked spot {index} of {spots.Count}, at {Format(spot.Start)}.");
    }

    private void OnCloseAnalysis(object? sender, RoutedEventArgs e) => AnalysisPopup.IsOpen = false;
}
