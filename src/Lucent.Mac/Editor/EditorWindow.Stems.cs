using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Lucent.Editor;
using Lucent.Editor.Stems;
using Lucent.Export;

namespace Lucent.Mac;

/// <summary>
/// The stem splitter: separates the music into vocals, drums, bass and the other instruments with an AI model
/// (Demucs), to keep only some of them in the editor or save each as a file.
/// </summary>
public partial class EditorWindow
{
    private StemsWindow? _stemsWindow;

    private void OnStems(object? sender, RoutedEventArgs e)
    {
        if (StemChoices.Children.Count == 0)
        {
            foreach (var source in StemNames.All)
            {
                var choice = new ToggleButton
                {
                    Theme = FindTheme("ToolToggle"),
                    Content = StemNames.Title(source),
                    Tag = source,
                    Height = 30,
                };
                choice.Click += (_, _) => StemChoicesChanged();
                StemChoices.Children.Add(choice);
            }
        }

        ShowStemChoices();
        StemsPopup.IsOpen = true;
    }

    private IReadOnlyList<string> StemsKept => StemChoices.Children.OfType<ToggleButton>()
        .Where(choice => choice.IsChecked == true).Select(choice => (string)choice.Tag!).ToList();

    private void ShowStemChoices()
    {
        foreach (var choice in StemChoices.Children.OfType<ToggleButton>())
        {
            choice.IsChecked = _settings.StemsKept.Contains((string)choice.Tag!);
        }

        StemChoicesChanged();
    }

    private void StemChoicesChanged()
    {
        var kept = StemsKept;
        _settings.StemsKept = kept.ToList();
        var some = kept.Count > 0 && kept.Count < StemNames.All.Length;
        StemsApplyButton.IsEnabled = some;
        StemsApplyButton.Content = some ? new StemsEffect(kept).Description : "Apply";
        var where = Selection.IsEmpty ? "the whole sound" : "the selection";
        var what = kept.Count == 0
            ? "Choose the parts to keep."
            : kept.Count == StemNames.All.Length
                ? "That's everything; choose fewer to take some out."
                : $"Keeps the {StemNames.List(kept)} in {where}, and takes out the rest.";
        var how = StemModel.IsDownloaded
            ? "Splitting a song takes a few seconds on a graphics card, or a minute or two without one."
            : $"It uses Demucs, an AI model that runs on your graphics card. The first time, Lucent downloads it (about {StemModel.DownloadMegabytes} MB).";
        StemsHint.Text = $"{what} {how}";
    }

    private void OnStemsKaraoke(object? sender, RoutedEventArgs e)
    {
        _settings.StemsKept = StemNames.All.Where(source => source != "vocals").ToList();
        ShowStemChoices();
    }

    private void OnStemsVocals(object? sender, RoutedEventArgs e)
    {
        _settings.StemsKept = new List<string> { "vocals" };
        ShowStemChoices();
    }

    private async void OnApplyStems(object? sender, RoutedEventArgs e)
    {
        StemsPopup.IsOpen = false;
        _settings.Save();
        var kept = StemsKept;
        if (kept.Count == 0 || kept.Count == StemNames.All.Length || !await EnsureStemModelAsync())
        {
            return;
        }

        // Splitting needs both channels, even when the selection is in one.
        await ApplyAsync(new StemsEffect(kept), bothChannels: true);
    }

    private async void OnSaveStems(object? sender, RoutedEventArgs e)
    {
        StemsPopup.IsOpen = false;
        _settings.Save();
        await SaveStemsAsync();
    }

    /// <summary>Splits the selection (or everything) and opens its stems in their own window to listen to.</summary>
    private async void OnListenToStems(object? sender, RoutedEventArgs e)
    {
        StemsPopup.IsOpen = false;
        _settings.Save();
        if (_session is not { } session || !await EnsureStemModelAsync())
        {
            return;
        }

        var audio = StemsAudio(session);
        StemSet? stems = null;
        var ok = await RunBusyAsync("Splitting into stems…", async (progress, token) =>
        {
            await Task.Run(() => stems = StemCache.Split(audio, progress.Report, token), token);
        });
        if (!ok || stems is null)
        {
            return;
        }

        ShowStemsWindow(stems);
    }

    /// <summary>Opens the stems in their own window, replacing any stems window already open.</summary>
    private void ShowStemsWindow(StemSet stems)
    {
        _stemsWindow?.Close();
        var song = Path.GetFileNameWithoutExtension(_path) + (Selection.IsEmpty ? string.Empty : " (selection)");
        var window = new StemsWindow(stems, song, StemModel.Splitter.DeviceNote, KeepStemsAsync, SaveStemsAsync);
        _stemsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_stemsWindow, window))
            {
                _stemsWindow = null;
            }
        };
        window.Show(this);
        ShowStatus($"{StemModel.Splitter.DeviceNote} Mute or solo the stems to hear each one.");
    }

    /// <summary>From the stems window: keeps the stems being heard, as Apply in the popup does.</summary>
    private async Task KeepStemsAsync(IReadOnlyList<string> kept)
    {
        _stemsWindow?.Close();
        _settings.StemsKept = kept.ToList();
        _settings.Save();
        Activate();
        // Splitting needs both channels, even when the selection is in one.
        await ApplyAsync(new StemsEffect(kept), bothChannels: true);
    }

    /// <summary>What the stems are split from: the selection, or everything if nothing is selected.</summary>
    private AudioBuffer StemsAudio(EditSession session)
    {
        var range = Selection.IsEmpty ? new FrameRange(0, session.Audio.Frames) : Selection;
        return session.Audio.Slice(range.Start, range.Length);
    }

    private async Task SaveStemsAsync()
    {
        if (_session is not { } session || _info is not { } info || !await EnsureStemModelAsync())
        {
            return;
        }

        var audio = StemsAudio(session);
        var options = OptionsLike(info, ExportOptions.WritableFormat(_path) ?? ExportFormat.Flac);
        var folder = StemFiles.NewFolderFor(_path);
        var source = _path;
        IReadOnlyList<string> files = Array.Empty<string>();
        StemSet? stems = null;
        var ok = await RunBusyAsync("Splitting into stems…", async (progress, token) =>
        {
            await Task.Run(() =>
            {
                stems = StemCache.Split(audio, fraction => progress.Report(0.85 * fraction), token);
                files = StemFiles.Save(stems, source, folder, options, fraction => progress.Report(0.85 + 0.15 * fraction), token);
            }, token);
        });

        if (ok && files.Count > 0)
        {
            _lastSavedPath = files[0];
            ShowFileButton.IsVisible = true;
            var part = Selection.IsEmpty ? string.Empty : "the selection's ";
            // Saved from the popup: open the stems to listen to as well (from the stems window, it's already open).
            if (_stemsWindow is null && stems is not null)
            {
                ShowStemsWindow(stems);
            }

            _stemsWindow?.ShowSaved(files[0]);
            ShowStatus($"Saved {part}vocals, drums, bass and other instruments as {options.Summary} in \"{Path.GetFileName(folder)}\". " +
                       StemModel.Splitter.DeviceNote);
        }
    }

    /// <summary>Downloads the stem splitter's model the first time, after asking. False if it isn't there to use.</summary>
    private async Task<bool> EnsureStemModelAsync()
    {
        if (StemModel.IsDownloaded)
        {
            return true;
        }

        var answer = await MessageWindow.ShowAsync(this,
            $"Splitting into stems uses Demucs, an AI model made by Meta and shared for anyone to use. Lucent needs to download it " +
            $"once (about {StemModel.DownloadMegabytes} MB) and keeps it for next time.\n\nDownload it now?",
            "Stems", MessageButtons.YesNo, MessageResult.Yes);
        if (answer != MessageResult.Yes)
        {
            return false;
        }

        var ok = await RunBusyAsync("Downloading the stem splitter…",
            (progress, token) => StemModel.DownloadAsync(progress.Report, token));
        if (ok)
        {
            ShowStatus("Downloaded the stem splitter.");
        }

        return ok && StemModel.IsDownloaded;
    }
}
