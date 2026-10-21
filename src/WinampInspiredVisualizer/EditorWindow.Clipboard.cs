using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NAudio.Utils;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Cut, copy and paste. What's copied is shared by every editor window (so part of one song can go into
/// another) and also put on the Windows clipboard as a WAV, for other programs; audio copied in another
/// program can be pasted here too. Every join is crossfaded, by the length chosen in the Edit menu.
/// </summary>
public partial class EditorWindow
{
    /// <summary>Longer copies stay inside Lucent: a WAV of more than this on the Windows clipboard gets unwieldy.</summary>
    private static readonly TimeSpan SystemClipboardLimit = TimeSpan.FromMinutes(10);

    private static ClipAudio? s_clip;

    /// <summary>What Lucent last put on the Windows clipboard, to tell whether something else has replaced it since.</summary>
    private static IDataObject? s_clipData;

    /// <summary>True when there is audio to paste, from Lucent or from another program.</summary>
    private static bool HasClip
    {
        get
        {
            if (s_clip is not null)
            {
                return true;
            }

            try
            {
                return Clipboard.ContainsAudio();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>How many frames each side of a join plays on under the other, at a sample rate.</summary>
    private int JoinFrames(int sampleRate) => (int)((long)_settings.CrossfadeMs * sampleRate / 1000);

    private void Copy()
    {
        if (_session is not { } session || Selection.IsEmpty)
        {
            ShowStatus("Select something to copy first.");
            return;
        }

        var clip = session.Copy(Selection);
        s_clip = clip;
        s_clipData = null;
        var shared = clip.Audio.Duration <= SystemClipboardLimit && TryPutOnSystemClipboard(clip.Audio);
        ShowStatus($"Copied {clip.Audio.Duration.TotalSeconds:0.0#} seconds. Ctrl+V pastes it at the cursor, or over a selection" +
                   (shared ? ", here or in another program." : "."));
    }

    private async Task CutAsync()
    {
        var selection = Selection;
        if (_session is null || selection.IsEmpty)
        {
            ShowStatus("Select something to cut first.");
            return;
        }

        Copy();
        if (Waveform.SelectedChannel is not null)
        {
            // One channel can't get shorter than the other, so the cut part of it is silenced.
            await ApplyAsync(new Editor.Effects.SilenceEffect());
            ShowStatus("Copied. Only one channel is selected, so that part of it was silenced (both channels must stay the same length).");
            return;
        }

        // The cut part becomes silence, so what's after it stays put; dragging the next clip's tab left closes the gap.
        await EditAsync("Cut", session => session.SilenceRange(selection, "Cut"), () => new FrameRange(selection.Start, 0));
    }

    private async Task PasteAsync()
    {
        if (_session is not { } session || IsBusy || VideoRefuses("Pasting"))
        {
            return;
        }

        var clip = TakeClip();
        if (clip is null)
        {
            ShowStatus("There's nothing to paste. Copy part of a sound first (Ctrl+C).");
            return;
        }

        // A paste always goes into both channels: they must stay the same length.
        var target = Selection.IsEmpty ? new FrameRange(Waveform.CursorFrame, 0) : Selection;
        var audio = session.Audio;
        var pasted = default(FrameRange);
        var converted = clip.Audio.ChannelCount != audio.ChannelCount || clip.Audio.SampleRate != audio.SampleRate;
        var done = await RunBusyAsync("Pasting…", async (_, token) =>
        {
            pasted = await Task.Run(() => session.Paste(target, clip.ConvertTo(audio.ChannelCount, audio.SampleRate, token)), token);
        });

        if (done)
        {
            Waveform.SelectedChannel = null;
            ShowAudio();
            Waveform.Selection = pasted;
            ShowStatus((target.IsEmpty ? "Pasted" : "Pasted over the selection") +
                       (converted ? $", converted to {(audio.ChannelCount == 1 ? "mono" : "stereo")} at {audio.SampleRate / 1000.0:0.#} kHz" : string.Empty) +
                       ". Ctrl+Z undoes it.");
        }
    }

    /// <summary>The audio to paste: Lucent's own copy, unless another program has put audio on the clipboard since.</summary>
    private static ClipAudio? TakeClip()
    {
        try
        {
            var ours = s_clipData is not null && Clipboard.IsCurrent(s_clipData);
            if (!ours && Clipboard.ContainsAudio() && Clipboard.GetAudioStream() is { } stream)
            {
                using (stream)
                {
                    if (ReadWave(stream) is { } audio)
                    {
                        return ClipAudio.Bare(audio);
                    }
                }
            }
        }
        catch (Exception)
        {
            // The clipboard is busy or holds something unreadable: fall back to Lucent's own copy.
        }

        return s_clip;
    }

    private static bool TryPutOnSystemClipboard(AudioBuffer audio)
    {
        try
        {
            var stream = new MemoryStream();
            var format = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.ChannelCount);
            using (var writer = new WaveFileWriter(new IgnoreDisposeStream(stream), format))
            {
                var block = new float[4096 * audio.ChannelCount];
                for (var start = 0; start < audio.Frames; start += 4096)
                {
                    var frames = Math.Min(4096, audio.Frames - start);
                    for (var i = 0; i < frames; i++)
                    {
                        for (var c = 0; c < audio.ChannelCount; c++)
                        {
                            block[i * audio.ChannelCount + c] = audio.Channels[c][start + i];
                        }
                    }

                    writer.WriteSamples(block, 0, frames * audio.ChannelCount);
                }
            }

            stream.Position = 0;
            var data = new DataObject();
            data.SetAudio(stream);
            Clipboard.SetDataObject(data, copy: true);
            s_clipData = data;
            return true;
        }
        catch (Exception)
        {
            // Another program has the clipboard open; the copy still works inside Lucent.
            return false;
        }
    }

    /// <summary>Reads a WAV from the clipboard into memory, or null if it isn't one NAudio can read.</summary>
    private static AudioBuffer? ReadWave(Stream stream)
    {
        using var reader = new WaveFileReader(stream);
        var samples = reader.ToSampleProvider();
        var channels = Math.Min(2, samples.WaveFormat.Channels);
        var source = samples.WaveFormat.Channels;
        var output = Enumerable.Range(0, channels).Select(_ => new List<float>()).ToArray();
        var chunk = new float[8192 * source];
        int read;
        while ((read = samples.Read(chunk, 0, chunk.Length)) > 0)
        {
            for (var i = 0; i < read / source; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    output[c].Add(chunk[i * source + c]);
                }
            }
        }

        return output[0].Count == 0 ? null : new AudioBuffer(output.Select(list => list.ToArray()).ToArray(), samples.WaveFormat.SampleRate);
    }

    // ---- The Edit menu ----

    private void OnEditMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = EditMenuButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        var hasSelection = !Selection.IsEmpty;
        MenuItem Add(ItemsControl parent, string header, Func<Task> action, string? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
            return item;
        }

        Add(menu, "Cut", CutAsync, "Ctrl+X", hasSelection);
        Add(menu, "Copy", () =>
        {
            Copy();
            return Task.CompletedTask;
        }, "Ctrl+C", hasSelection);
        Add(menu, hasSelection ? "Paste over selection" : "Paste at the cursor", PasteAsync, "Ctrl+V", HasClip);
        menu.Items.Add(new Separator());

        var crossfade = new MenuItem
        {
            Header = "Crossfade joins",
            ToolTip = "When cutting, pasting, or moving clips, each side of a join fades into the other over this long, so there's no click",
        };
        foreach (var ms in EditorSettings.CrossfadeChoices)
        {
            var item = new MenuItem
            {
                Header = ms == 0 ? "Off (a hard cut)" : $"{ms} ms{(ms == 10 ? "  (recommended)" : string.Empty)}",
                IsCheckable = true,
                IsChecked = _settings.CrossfadeMs == ms,
            };
            item.Click += (_, _) => SetCrossfade(ms);
            crossfade.Items.Add(item);
        }

        menu.Items.Add(crossfade);
        menu.IsOpen = true;
    }

    private void SetCrossfade(int ms)
    {
        _settings.CrossfadeMs = ms;
        _settings.Save();
        if (_session is { } session)
        {
            session.JoinFrames = JoinFrames(session.Audio.SampleRate);
        }

        ShowStatus(ms == 0 ? "Joins are now hard cuts, with no crossfade." : $"Joins now crossfade over {ms} ms.");
    }
}
