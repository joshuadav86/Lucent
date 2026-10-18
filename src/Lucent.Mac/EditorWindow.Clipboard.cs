using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Editor;
using NAudio.Utils;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// Cut, copy and paste. What's copied is shared by every editor window (so part of one song can go into
/// another) and also put on the Mac's clipboard as a WAV, for other apps; audio copied in another
/// app (WAV or AIFF) can be pasted here too. Every join is crossfaded, by the length chosen in the Edit menu.
/// </summary>
public partial class EditorWindow
{
    /// <summary>Longer copies stay inside Lucent: a WAV of more than this on the clipboard gets unwieldy.</summary>
    private static readonly TimeSpan SystemClipboardLimit = TimeSpan.FromMinutes(10);

    private static ClipAudio? s_clip;

    /// <summary>The clipboard's change count just after Lucent put its copy there, to tell whether something has replaced it since.</summary>
    private static long? s_clipChangeCount;

    /// <summary>True when there is audio to paste, from Lucent or from another app.</summary>
    private static bool HasClip => s_clip is not null || AudioClipboard.ContainsAudio();

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
        s_clipChangeCount = null;
        var shared = clip.Audio.Duration <= SystemClipboardLimit && TryPutOnSystemClipboard(clip.Audio);
        ShowStatus($"Copied {clip.Audio.Duration.TotalSeconds:0.0#} seconds. ⌘V pastes it at the cursor, or over a selection" +
                   (shared ? ", here or in another app." : "."));
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
            await ApplyAsync(new WinampInspiredVisualizer.Editor.Effects.SilenceEffect());
            ShowStatus("Copied. Only one channel is selected, so that part of it was silenced (both channels must stay the same length).");
            return;
        }

        // The cut part becomes silence, so what's after it stays put; dragging the next clip's tab left closes the gap.
        await EditAsync("Cut", session => session.SilenceRange(selection, "Cut"), () => new FrameRange(selection.Start, 0));
    }

    private async Task PasteAsync()
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        var clip = TakeClip();
        if (clip is null)
        {
            ShowStatus("There's nothing to paste. Copy part of a sound first (⌘C).");
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
                       ". ⌘Z undoes it.");
        }
    }

    /// <summary>The audio to paste: Lucent's own copy, unless another app has put audio on the clipboard since.</summary>
    private static ClipAudio? TakeClip()
    {
        try
        {
            var count = AudioClipboard.ChangeCount();
            var ours = s_clipChangeCount is not null && count == s_clipChangeCount;
            if (!ours && AudioClipboard.GetAudio() is { } copied)
            {
                using var stream = new MemoryStream(copied.Bytes, writable: false);
                if (ReadAudio(stream, copied.Type == AudioClipboard.AiffType) is { } audio)
                {
                    return ClipAudio.Bare(audio);
                }
            }
        }
        catch (Exception)
        {
            // The clipboard holds something unreadable: fall back to Lucent's own copy.
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

            s_clipChangeCount = AudioClipboard.SetWave(stream.ToArray());
            return s_clipChangeCount is not null;
        }
        catch (Exception)
        {
            // The copy still works inside Lucent.
            return false;
        }
    }

    /// <summary>Reads a WAV or AIFF from the clipboard into memory, or null if it isn't one NAudio can read.</summary>
    private static AudioBuffer? ReadAudio(Stream stream, bool aiff)
    {
        using WaveStream reader = aiff ? new AiffFileReader(stream) : new WaveFileReader(stream);
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

    private void OnEditMenu(object? sender, RoutedEventArgs e)
    {
        var menu = MenuAbove();
        var hasSelection = !Selection.IsEmpty;
        MenuItem Add(ItemsControl parent, string header, Func<Task> action, string? gesture = null, bool enabled = true)
        {
            var item = MenuEntry(header, gesture, enabled);
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
            return item;
        }

        Add(menu, "Cut", CutAsync, "⌘X", hasSelection);
        Add(menu, "Copy", () =>
        {
            Copy();
            return Task.CompletedTask;
        }, "⌘C", hasSelection);
        Add(menu, hasSelection ? "Paste over selection" : "Paste at the cursor", PasteAsync, "⌘V", HasClip);
        menu.Items.Add(new Separator());

        var crossfade = MenuEntry("Crossfade joins",
            tip: "When cutting, pasting or moving clips, each side of a join fades into the other over this long, so there's no click");
        foreach (var ms in EditorSettings.CrossfadeChoices)
        {
            var item = new MenuItem
            {
                Header = ms == 0 ? "Off (a hard cut)" : $"{ms} ms{(ms == 10 ? "  (recommended)" : string.Empty)}",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _settings.CrossfadeMs == ms,
            };
            item.Click += (_, _) => SetCrossfade(ms);
            crossfade.Items.Add(item);
        }

        menu.Items.Add(crossfade);
        menu.Open(EditMenuButton);
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
