using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Lucent.Mac.Audio;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Settings;
using PictureType = TagLib.PictureType;

namespace Lucent.Mac;

/// <summary>
/// Shows and edits a track's info (title, artist, album and so on), its artwork and its file details.
/// With several tracks chosen it edits them together: fields that differ say "(several values)" and are
/// only changed if something is typed into them.
/// </summary>
public partial class TrackInfoWindow : Window
{
    private const string SeveralValues = "(several values)";
    private const int PreviewPixels = 640;
    private const int ThumbnailPixels = 112;

    private static readonly string[] CommonGenres =
    {
        "Alternative", "Ambient", "Blues", "Classical", "Country", "Dance", "Disco", "Drum & Bass", "Dubstep",
        "Electronic", "Folk", "Funk", "Hip-Hop", "House", "Indie", "Jazz", "Latin", "Lo-Fi", "Metal", "Pop",
        "Punk", "R&B", "Reggae", "Rock", "Singer-Songwriter", "Soul", "Soundtrack", "Synthwave", "Techno", "Trance",
        "World",
    };

    // Browsers on the Mac put a PNG copy of a picture on the clipboard, which keeps the colours exactly.
    private static readonly DataFormat<byte[]> PngClipboardFormat = DataFormat.CreateBytesPlatformFormat("public.png");

    private static TrackInfoWindow? _open;

    private readonly AudioPlayer _player;
    private readonly AppSettings _settings;
    private readonly Dictionary<TagField, TextBox> _boxes;
    private readonly Dictionary<TagField, string> _initial = new();
    private readonly ObservableCollection<PictureItem> _pictures = new();
    private IReadOnlyList<TrackTags> _tracks = Array.Empty<TrackTags>();
    private bool _picturesDiffer;
    private bool _picturesChanged;
    private bool _isLoading;
    private bool _isSaving;
    private bool _closeConfirmed;
    private bool _askingToClose;
    private int _loadVersion;
    private DateTime _genrePopupClosedAt;

    /// <summary>Only for Avalonia's XAML loader and previewer; the app opens it with Open.</summary>
    public TrackInfoWindow()
        : this(new AudioPlayer(), new AppSettings())
    {
    }

    private TrackInfoWindow(AudioPlayer player, AppSettings settings)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _player = player;
        _settings = settings;
        Resources["WarningBrush"] = CreateWarningBrush();

        _boxes = new Dictionary<TagField, TextBox>
        {
            [TagField.Title] = TitleBox,
            [TagField.Artist] = ArtistBox,
            [TagField.Album] = AlbumBox,
            [TagField.AlbumArtist] = AlbumArtistBox,
            [TagField.Year] = YearBox,
            [TagField.Track] = TrackBox,
            [TagField.TrackCount] = TrackCountBox,
            [TagField.Disc] = DiscBox,
            [TagField.DiscCount] = DiscCountBox,
            [TagField.Genre] = GenreBox,
            [TagField.Composer] = ComposerBox,
            [TagField.Bpm] = BpmBox,
            [TagField.Key] = KeyBox,
            [TagField.Comment] = CommentBox,
            [TagField.Lyrics] = LyricsBox,
        };

        PictureList.ItemsSource = _pictures;
        KeepDatesSwitch.IsChecked = settings.KeepFileDatesWhenEditing;
        KeepDatesSwitch.IsCheckedChanged += (_, _) => UpdateKeepDatesHint();
        UpdateKeepDatesHint();
        BuildGenreChips();

        ArtworkFrame.AddHandler(DragDrop.DragEnterEvent, OnArtworkDragOver);
        ArtworkFrame.AddHandler(DragDrop.DragOverEvent, OnArtworkDragOver);
        ArtworkFrame.AddHandler(DragDrop.DragLeaveEvent, OnArtworkDragLeave);
        ArtworkFrame.AddHandler(DragDrop.DropEvent, OnArtworkDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Raised on the UI thread after files' info has been saved, with the files that changed. Lists and
    /// the now playing display listen to this to show the new info.
    /// </summary>
    public static event EventHandler<TagsSavedEventArgs>? TagsSaved;

    /// <summary>
    /// Opens the info window for one or more music files. If it's already open it comes to the front,
    /// switching to these files unless it has unsaved changes.
    /// </summary>
    public static void Open(Window owner, AudioPlayer player, AppSettings settings, IEnumerable<string> paths)
    {
        var list = paths.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            return;
        }

        if (_open is { } existing)
        {
            if (!existing._isSaving && !existing.HasChanges)
            {
                _ = existing.LoadAsync(list);
            }

            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = new TrackInfoWindow(player, settings);
        _open = window;
        window.Show(owner);
        _ = window.LoadAsync(list);
    }

    /// <summary>
    /// For the screenshots: a window with <paramref name="paths"/> already read in (on this thread, so
    /// it's ready to draw at once), showing the given tab ("Song", "Lyrics" or "File").
    /// </summary>
    internal static TrackInfoWindow CreatePreview(AudioPlayer player, AppSettings settings, IReadOnlyList<string> paths, string tab = "Song")
    {
        var window = new TrackInfoWindow(player, settings);
        window.BeginLoad(paths);
        window.FinishLoad(ReadAll(paths), paths);
        var button = tab switch
        {
            "Lyrics" => window.LyricsTabButton,
            "File" => window.FileTabButton,
            _ => window.SongTabButton,
        };
        button.IsChecked = true;
        window.ShowTab(tab);
        return window;
    }

    private bool HasChanges => _tracks.Count > 0 && (_picturesChanged || _boxes.Keys.Any(IsChanged));

    private static string TextOf(TextBox box) => box.Text ?? string.Empty;

    // ---- Reading ----

    private async Task LoadAsync(IReadOnlyList<string> paths)
    {
        var version = BeginLoad(paths);
        var result = await Task.Run(() => ReadAll(paths));
        if (version == _loadVersion)
        {
            FinishLoad(result, paths);
        }
    }

    private int BeginLoad(IReadOnlyList<string> paths)
    {
        var version = ++_loadVersion;
        _isLoading = true;
        _tracks = Array.Empty<TrackTags>();
        ContentGrid.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SubtitleText.Text = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} tracks";
        HideProblems();
        ShowStatus("Reading…");
        return version;
    }

    private void FinishLoad(LoadResult result, IReadOnlyList<string> paths)
    {
        _isLoading = false;
        _tracks = result.Tracks;
        if (result.Failed.Count > 0)
        {
            ShowProblems(
                result.Tracks.Count == 0
                    ? paths.Count == 1 ? "Lucent can't edit this file:" : "Lucent can't edit these files:"
                    : result.Failed.Count == 1
                        ? "Lucent can't edit one of these files, so it's been left out:"
                        : $"Lucent can't edit {result.Failed.Count} of these files, so they've been left out:",
                result.Failed);
        }

        if (_tracks.Count == 0)
        {
            // Nothing can be edited: don't leave the previous files' info on show.
            foreach (var box in _boxes.Values)
            {
                box.Text = string.Empty;
                box.Watermark = null;
            }

            FillPictures(Array.Empty<PictureItem>(), differ: false);
            DetailsGrid.Children.Clear();
            DetailsGrid.RowDefinitions.Clear();
            ShowStatus(string.Empty);
            return;
        }

        SubtitleText.Text = _tracks.Count == 1 ? Path.GetFileName(_tracks[0].Path) : $"{_tracks.Count} tracks";
        Title = _tracks.Count == 1 ? $"{Path.GetFileNameWithoutExtension(_tracks[0].Path)} - Edit info - Lucent" : "Edit info - Lucent";
        FillFields();
        FillPictures(result.Pictures, result.PicturesDiffer);
        FillDetails();
        ContentGrid.IsEnabled = true;
        SaveButton.IsEnabled = true;
        DetectButton.IsEnabled = _tracks.Count == 1;
        ShowStatus(_tracks.Count > 1 ? "Anything you type here is saved into every one of these tracks." : string.Empty);
        (_tracks.Count == 1 ? TitleBox : ArtistBox).Focus();
    }

    private static LoadResult ReadAll(IReadOnlyList<string> paths)
    {
        var tracks = new List<TrackTags>();
        var failed = new List<(string Path, string Reason)>();
        foreach (var path in paths)
        {
            if (!TagWriter.CanWrite(path))
            {
                failed.Add((path, "Lucent can't write info into this kind of file."));
                continue;
            }

            try
            {
                tracks.Add(TrackTags.Read(path));
            }
            catch (Exception ex)
            {
                failed.Add((path, TagWriter.Explain(ex)));
            }
        }

        var differ = tracks.Count > 1 && tracks.Skip(1).Any(track => !TagPicture.SameList(track.Pictures, tracks[0].Pictures));
        var pictures = tracks.Count == 0 || differ
            ? new List<PictureItem>()
            : tracks[0].Pictures.Select(PictureItem.Create).ToList();
        return new LoadResult(tracks, failed, pictures, differ);
    }

    private void FillFields()
    {
        _initial.Clear();
        foreach (var (field, box) in _boxes)
        {
            var values = _tracks.Select(track => track.Values[field]).Distinct(StringComparer.Ordinal).ToList();
            var mixed = values.Count > 1;
            box.Text = mixed ? string.Empty : values[0];
            box.Watermark = mixed ? SeveralValues : null;
            box.IsEnabled = true;
            box.Classes.Remove("warning");
            _initial[field] = TextOf(box);
        }

        // Every song has its own title, so it isn't offered when editing several at once.
        if (_tracks.Count > 1)
        {
            TitleBox.Text = string.Empty;
            TitleBox.Watermark = "Each track keeps its own title";
            TitleBox.IsEnabled = false;
            _initial[TagField.Title] = string.Empty;
        }
    }

    private bool IsChanged(TagField field)
    {
        var box = _boxes[field];
        return box.IsEnabled && TagFields.Normalize(field, TextOf(box)) != TagFields.Normalize(field, _initial.GetValueOrDefault(field, string.Empty));
    }

    private void FillDetails()
    {
        DetailsGrid.Children.Clear();
        DetailsGrid.RowDefinitions.Clear();
        var details = _tracks.Select(track => track.Details).ToList();
        var single = details.Count == 1;

        string Same(Func<FileDetails, string> describe)
        {
            var values = details.Select(describe).Distinct().ToList();
            return values.Count == 1 ? values[0] : SeveralValues;
        }

        AddDetail("Format", Same(d => d.Format));
        AddDetail("Codec", Same(d => d.Codec.Length == 0 ? "—" : d.Codec));
        AddDetail("Tags", Same(d => d.TagFormat));
        AddDetail("Bit rate", Same(BitrateText));
        AddDetail("Sample rate", Same(d => d.SampleRate > 0 ? $"{d.SampleRate / 1000.0:0.###} kHz" : "—"));
        AddDetail("Bit depth", Same(d => d.BitsPerSample > 0 ? $"{d.BitsPerSample}-bit" : "—"));
        AddDetail("Channels", Same(d => d.Channels switch
        {
            0 => "—",
            1 => "Mono",
            2 => "Stereo",
            var count => $"{count} channels",
        }));

        var length = TimeSpan.FromTicks(details.Sum(d => d.Duration.Ticks));
        AddDetail("Length", single ? TimeFormat.Format(length) : $"{TimeFormat.Format(length)} in total");
        var size = details.Sum(d => d.SizeBytes);
        AddDetail("File size", single ? SizeText(size) : $"{SizeText(size)} in total");
        AddDetail("Modified", Same(d => d.Modified.ToString("g", CultureInfo.CurrentCulture)));

        var folders = _tracks.Select(track => Path.GetDirectoryName(track.Path) ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AddDetail(single ? "Location" : "Folder", single ? _tracks[0].Path : folders.Count == 1 ? folders[0] : "(several folders)");
    }

    private void AddDetail(string label, string value)
    {
        var row = DetailsGrid.RowDefinitions.Count;
        DetailsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var name = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 12, 10) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetRow(name, row);

        // Selectable text, so the details (like the path) can be copied.
        var text = new SelectableTextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        text.SetResourceReference(SelectableTextBlock.ForegroundProperty, "TextBrush");
        text.SetResourceReference(SelectableTextBlock.SelectionBrushProperty, "AccentBrush");
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);

        DetailsGrid.Children.Add(name);
        DetailsGrid.Children.Add(text);
    }

    private static string BitrateText(FileDetails details)
    {
        if (details.BitrateKbps > 0)
        {
            return $"{details.BitrateKbps:N0} kbps";
        }

        // Some formats don't record one; work it out from the size and length instead.
        return details.Duration.TotalSeconds > 1
            ? $"About {details.SizeBytes * 8 / details.Duration.TotalSeconds / 1000:N0} kbps"
            : "—";
    }

    private static string SizeText(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };

    // ---- Artwork ----

    private void FillPictures(IReadOnlyList<PictureItem> pictures, bool differ)
    {
        _picturesDiffer = differ;
        _picturesChanged = false;
        _pictures.Clear();
        foreach (var picture in pictures)
        {
            _pictures.Add(picture);
        }

        ArtworkNote.Text = string.Empty;
        PictureList.SelectedItem = _pictures.FirstOrDefault(item => item.Picture.Type == PictureType.FrontCover) ?? _pictures.FirstOrDefault();
        ShowSelectedPicture();
    }

    private void OnPictureSelected(object? sender, SelectionChangedEventArgs e) => ShowSelectedPicture();

    private void ShowSelectedPicture()
    {
        var item = PictureList.SelectedItem as PictureItem ?? _pictures.FirstOrDefault();
        ArtworkImage.Source = item?.Preview;
        ArtworkEmpty.IsVisible = item?.Preview is null;
        ArtworkInfo.Text = item?.InfoText ?? string.Empty;
        PictureList.IsVisible = _pictures.Count > 1;

        if (item is not null)
        {
            ArtworkEmptyTitle.Text = "Can't show this picture";
            ArtworkEmptyText.Text = "It's kept in the file as it is.";
        }
        else if (_picturesDiffer && _picturesChanged)
        {
            ArtworkEmptyTitle.Text = "Artwork will be removed";
            ArtworkEmptyText.Text = $"Saving takes the pictures out of all {_tracks.Count} tracks.";
        }
        else if (_picturesDiffer)
        {
            ArtworkEmptyTitle.Text = "Different artwork";
            ArtworkEmptyText.Text = "These tracks have different pictures. A picture added here becomes the front cover of all of them.";
        }
        else
        {
            ArtworkEmptyTitle.Text = _picturesChanged ? "Artwork will be removed" : "No artwork";
            ArtworkEmptyText.Text = "Drop a picture here, or copy one and press ⌘V.";
        }

        var removeAll = _picturesDiffer && item is null;
        RemoveImageButton.Content = removeAll ? "Remove all" : "Remove";
        ToolTip.SetTip(RemoveImageButton, removeAll ? $"Take the pictures out of all {_tracks.Count} tracks" : "Take this picture out of the file");
        RemoveImageButton.IsEnabled = item is not null || (removeAll && !_picturesChanged);
        SaveImageButton.IsEnabled = item is not null;
    }

    private async void OnChooseImage(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a cover picture",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Pictures")
                    {
                        Patterns = CoverArt.ImageExtensions.Select(extension => "*" + extension).ToList(),
                        AppleUniformTypeIdentifiers = new[] { "public.image" },
                        MimeTypes = new[] { "image/*" },
                    },
                    FilePickerFileTypes.All,
                },
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't open the file picker ({ex.Message}).", warning: true);
            return;
        }

        if (files.FirstOrDefault() is not { } file)
        {
            return;
        }

        if (file.TryGetLocalPath() is { } path)
        {
            await UseImageAsync(() => File.ReadAllBytes(path));
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            var bytes = copy.ToArray();
            await UseImageAsync(() => bytes);
        }
        catch (Exception)
        {
            ShowStatus("That picture couldn't be used. Try a JPEG or PNG file.", warning: true);
        }
    }

    private async void OnPasteImage(object? sender, RoutedEventArgs e)
    {
        if (!await TryPasteImageAsync())
        {
            ShowStatus("There's no picture on the clipboard. Copy one first, for example by right-clicking a picture in your browser and choosing Copy Image.", warning: true);
        }
    }

    /// <summary>Uses a picture from the clipboard as the cover, if there is one.</summary>
    private async Task<bool> TryPasteImageAsync()
    {
        if (!CanEditArtwork() || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        try
        {
            // A picture file copied in Finder.
            if (await clipboard.TryGetFilesAsync() is { } files
                && files.Select(item => item.TryGetLocalPath()).OfType<string>().FirstOrDefault(CoverArt.IsImageFile) is { } file)
            {
                _ = UseImageAsync(() => File.ReadAllBytes(file));
                return true;
            }

            if (await clipboard.TryGetValueAsync(PngClipboardFormat) is { Length: > 0 } png)
            {
                _ = UseImageAsync(() => png);
                return true;
            }

            if (await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                var bytes = CoverArt.EncodeJpeg(bitmap, 92);
                _ = UseImageAsync(() => bytes);
                return true;
            }
        }
        catch (Exception)
        {
            // Treat a clipboard that can't be read as empty.
        }

        return false;
    }

    private void OnArtworkDragOver(object? sender, DragEventArgs e)
    {
        var usable = CanEditArtwork() && DroppedImage(e) is not null;
        e.DragEffects = usable ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.IsVisible = usable;
        e.Handled = true;
    }

    private void OnArtworkDragLeave(object? sender, DragEventArgs e) => DropHint.IsVisible = false;

    private async void OnArtworkDrop(object? sender, DragEventArgs e)
    {
        DropHint.IsVisible = false;
        if (CanEditArtwork() && DroppedImage(e) is { } file)
        {
            e.Handled = true;
            await UseImageAsync(() => File.ReadAllBytes(file));
        }
    }

    private static string? DroppedImage(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { } files
            ? files.Select(item => item.TryGetLocalPath()).OfType<string>().FirstOrDefault(CoverArt.IsImageFile)
            : null;

    private bool CanEditArtwork() => !_isLoading && !_isSaving && _tracks.Count > 0;

    /// <summary>Makes a picture the front cover (replacing the old one), shrinking it first if it's huge.</summary>
    private async Task UseImageAsync(Func<byte[]> readImage)
    {
        if (!CanEditArtwork())
        {
            return;
        }

        PictureItem item;
        EmbeddableImage image;
        try
        {
            (image, item) = await Task.Run(() =>
            {
                var prepared = CoverArt.PrepareForEmbedding(readImage());
                var picture = new TagPicture(PictureType.FrontCover, prepared.MimeType, string.Empty, prepared.Data);
                return (prepared, PictureItem.Create(picture));
            });
        }
        catch (Exception)
        {
            ShowStatus("That picture couldn't be used. Try a JPEG or PNG file.", warning: true);
            return;
        }

        if (item.Preview is null)
        {
            ShowStatus("That picture couldn't be used. Try a JPEG or PNG file.", warning: true);
            return;
        }

        if (_picturesDiffer)
        {
            _pictures.Clear();
            _pictures.Add(item);
        }
        else
        {
            var front = _pictures.ToList().FindIndex(picture => picture.Picture.Type == PictureType.FrontCover);
            if (front >= 0)
            {
                _pictures[front] = item;
            }
            else
            {
                _pictures.Insert(0, item);
            }
        }

        _picturesChanged = true;
        PictureList.SelectedItem = item;
        ArtworkNote.Text = image.WasShrunk
            ? $"Shrunk from {image.OriginalWidth} × {image.OriginalHeight} so the music files don't grow too much."
            : string.Empty;
        ShowStatus(string.Empty);
        ShowSelectedPicture();
    }

    private void OnRemoveImage(object? sender, RoutedEventArgs e)
    {
        if (!CanEditArtwork())
        {
            return;
        }

        if ((PictureList.SelectedItem as PictureItem ?? _pictures.FirstOrDefault()) is { } item)
        {
            _pictures.Remove(item);
            // With different covers, removing the one just added goes back to leaving them all alone.
            _picturesChanged = !_picturesDiffer || _pictures.Count > 0;
            ArtworkNote.Text = string.Empty;
        }
        else if (_picturesDiffer)
        {
            _picturesChanged = true;
        }

        PictureList.SelectedItem = _pictures.FirstOrDefault();
        ShowSelectedPicture();
    }

    private async void OnSaveImage(object? sender, RoutedEventArgs e)
    {
        if ((PictureList.SelectedItem as PictureItem ?? _pictures.FirstOrDefault()) is not { } item)
        {
            return;
        }

        var extension = ImageFormats.ExtensionFor(item.Picture.MimeType);
        var album = TextOf(_boxes[TagField.Album]);
        IStorageFile? file;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the picture",
                SuggestedFileName = ArtworkWindow.SafeFileName(string.IsNullOrWhiteSpace(album) ? null : album) + extension,
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType($"{ImageFormats.Name(item.Picture.MimeType)} image") { Patterns = new[] { "*" + extension } },
                    FilePickerFileTypes.All,
                },
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't open the save panel ({ex.Message}).", warning: true);
            return;
        }

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(item.Picture.Data);
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't save the picture there: {TagWriter.Explain(ex)}", warning: true);
        }
    }

    // ---- Genres ----

    private void BuildGenreChips()
    {
        foreach (var genre in CommonGenres)
        {
            var chip = new Button { Content = genre, Theme = (ControlTheme)this.FindResource("PillButton")!, Margin = new Thickness(0, 0, 6, 6) };
            chip.Click += (_, _) =>
            {
                GenreBox.Text = genre;
                GenrePopup.IsOpen = false;
                GenreBox.Focus();
                GenreBox.CaretIndex = TextOf(GenreBox).Length;
            };
            GenreChips.Children.Add(chip);
        }
    }

    private void OnGenreSuggestions(object? sender, RoutedEventArgs e)
    {
        // The click that closed the list (by clicking outside it, on this button) shouldn't reopen it.
        if (GenrePopup.IsOpen || (DateTime.UtcNow - _genrePopupClosedAt).TotalMilliseconds < 250)
        {
            GenrePopup.IsOpen = false;
            return;
        }

        GenrePopup.IsOpen = true;
    }

    // WPF's PopupAnimation="Fade".
    private void OnGenrePopupOpened(object? sender, EventArgs e) => _ = ArtworkWindow.FadeIn(GenrePanel, TimeSpan.FromMilliseconds(150));

    private void OnGenrePopupClosed(object? sender, EventArgs e) => _genrePopupClosedAt = DateTime.UtcNow;

    // ---- Saving ----

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_isSaving || _isLoading || _tracks.Count == 0)
        {
            return;
        }

        var problems = Validate();
        if (problems.Count > 0)
        {
            ShowStatus(string.Join(" ", problems), warning: true);
            return;
        }

        var changes = BuildChanges();
        if (changes.IsEmpty)
        {
            _closeConfirmed = true;
            Close();
            return;
        }

        _settings.KeepFileDatesWhenEditing = changes.KeepFileDates;
        _settings.Save();
        SetSaving(true);
        HideProblems();

        var saved = new List<string>();
        var failed = new List<(string Path, string Reason)>();
        var notStored = new HashSet<TagField>();
        for (var i = 0; i < _tracks.Count; i++)
        {
            var path = _tracks[i].Path;
            SaveButton.Content = _tracks.Count == 1 ? "Saving…" : $"Saving {i + 1} of {_tracks.Count}…";
            try
            {
                var result = await SaveOneAsync(path, changes);
                saved.Add(path);
                notStored.UnionWith(result.NotStored);
            }
            catch (Exception ex)
            {
                failed.Add((path, TagWriter.Explain(ex)));
            }
        }

        SetSaving(false);
        if (saved.Count > 0)
        {
            TagsSaved?.Invoke(this, new TagsSavedEventArgs(saved));
        }

        if (failed.Count > 0)
        {
            // Keep the edits and the files that didn't save, so pressing Save again retries just those.
            var failedPaths = new HashSet<string>(failed.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            _tracks = _tracks.Where(track => failedPaths.Contains(track.Path)).ToList();
            SubtitleText.Text = _tracks.Count == 1 ? $"{Path.GetFileName(_tracks[0].Path)} (not saved yet)" : $"{_tracks.Count} tracks not saved yet";
            ShowProblems(
                failed.Count == 1
                    ? "This file couldn't be saved. It hasn't been changed:"
                    : $"{failed.Count} files couldn't be saved. They haven't been changed:",
                failed);
            ShowStatus(
                saved.Count > 0
                    ? $"Saved {saved.Count} of {saved.Count + failed.Count}. Fix what's below, then press Save to try the rest again."
                    : "Fix what's below, then press Save to try again.",
                warning: true);
            return;
        }

        if (notStored.Count > 0)
        {
            // Show what really ended up in the files.
            await LoadAsync(saved);
            ShowStatus($"Saved. This kind of file has no place for: {string.Join(", ", notStored.Select(TagFields.Label))}.", warning: true);
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// Saves one file. The slow part (copying it and writing the tags into the copy) happens while it keeps
    /// playing; only the quick swap at the end needs the player to let go of it.
    /// </summary>
    private async Task<PreparedSave> SaveOneAsync(string path, TagChanges changes)
    {
        PreparedSave prepared;
        try
        {
            prepared = await Task.Run(() => TagWriter.Prepare(path, changes));
        }
        catch (IOException) when (_player.IsCurrentFile(path))
        {
            // The player is holding it too tightly to even copy, so do the whole save with it let go.
            PreparedSave? whole = null;
            await _player.RunWithFileReleasedAsync(path, () => Task.Run(() =>
            {
                whole = TagWriter.Prepare(path, changes);
                TagWriter.Commit(whole);
            }));
            return whole!;
        }

        try
        {
            await _player.RunWithFileReleasedAsync(path, () => Task.Run(() => TagWriter.Commit(prepared)));
            return prepared;
        }
        catch
        {
            TagWriter.Discard(prepared);
            throw;
        }
    }

    /// <summary>Checks the number fields. Returns what's wrong in plain words, and marks those boxes.</summary>
    private List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var box in _boxes.Values)
        {
            box.Classes.Remove("warning");
        }

        void Problem(TagField field, string message)
        {
            _boxes[field].Classes.Add("warning");
            problems.Add(message);
        }

        foreach (var field in _boxes.Keys.Where(TagFields.IsNumber).Where(IsChanged))
        {
            var label = TagFields.Label(field);
            if (!TagFields.TryParseNumber(TextOf(_boxes[field]), out var number))
            {
                var example = field switch
                {
                    TagField.Year => "like 1997",
                    TagField.Bpm => "like 120",
                    TagField.TrackCount => "like 12",
                    _ => "like 3",
                };
                Problem(field, $"{label} should be a whole number, {example}.");
            }
            else if (field == TagField.Year && number != 0 && number is < 1000 or > 9999)
            {
                Problem(field, "Year should have four digits, like 1997.");
            }
        }

        CheckOrder(TagField.Track, TagField.TrackCount, "The track number is bigger than the number of tracks.");
        CheckOrder(TagField.Disc, TagField.DiscCount, "The disc number is bigger than the number of discs.");
        return problems;

        void CheckOrder(TagField numberField, TagField totalField, string message)
        {
            if ((IsChanged(numberField) || IsChanged(totalField))
                && KnownNumber(numberField) is { } number && KnownNumber(totalField) is { } total
                && number > 0 && total > 0 && number > total)
            {
                Problem(IsChanged(numberField) ? numberField : totalField, message);
            }
        }
    }

    /// <summary>The number in a box, or null when it's not a number or differs between the chosen tracks.</summary>
    private uint? KnownNumber(TagField field)
    {
        var box = _boxes[field];
        if (box.Watermark == SeveralValues && !IsChanged(field))
        {
            return null;
        }

        return TagFields.TryParseNumber(TextOf(box), out var number) ? number : null;
    }

    private TagChanges BuildChanges()
    {
        var changes = new TagChanges { KeepFileDates = KeepDatesSwitch.IsChecked == true };
        foreach (var field in _boxes.Keys.Where(IsChanged))
        {
            changes.Fields[field] = TextOf(_boxes[field]);
        }

        if (_picturesChanged)
        {
            changes.Artwork = !_picturesDiffer
                ? ArtworkEdit.Replace(_pictures.Select(item => item.Picture))
                : _pictures.FirstOrDefault() is { } cover
                    ? ArtworkEdit.SetFrontCover(cover.Picture)
                    : ArtworkEdit.Replace(Array.Empty<TagPicture>());
        }

        return changes;
    }

    private void SetSaving(bool saving)
    {
        _isSaving = saving;
        ContentGrid.IsEnabled = !saving;
        KeepDatesSwitch.IsEnabled = !saving;
        SaveButton.IsEnabled = !saving;
        CancelButton.IsEnabled = !saving;
        SaveButton.Content = saving ? "Saving…" : "Save";
        if (saving)
        {
            ShowStatus("Saving. If a sound is playing it may pause for a moment.");
        }
    }

    private void UpdateKeepDatesHint() =>
        KeepDatesHint.Text = KeepDatesSwitch.IsChecked == true
            ? "Sounds stay where they are in the library's newest-first order."
            : "Edited sounds will count as new and move to the top of the library's newest-first order.";

    // ---- Messages ----

    private void ShowStatus(string text, bool warning = false)
    {
        StatusText.Text = text;
        StatusText.Classes.Set("warning", warning);
    }

    private void ShowProblems(string title, IEnumerable<(string Path, string Reason)> files)
    {
        ProblemTitle.Text = title;
        ProblemList.ItemsSource = files.Select(file => $"{Path.GetFileName(file.Path)}: {file.Reason}").ToList();
        ProblemPanel.IsVisible = true;
    }

    private void HideProblems() => ProblemPanel.IsVisible = false;

    /// <summary>An amber that reads well on the current theme, light or dark.</summary>
    private static IBrush CreateWarningBrush()
    {
        var light = Application.Current is { } app
            && app.TryGetResource("BackgroundBrush", app.ActualThemeVariant, out var background)
            && background is ISolidColorBrush { Color: var colour }
            && 0.2126 * colour.R + 0.7152 * colour.G + 0.0722 * colour.B > 128;
        return new ImmutableSolidColorBrush(light ? Color.FromRgb(0xB4, 0x53, 0x09) : Color.FromRgb(0xF0, 0xA0, 0x4B));
    }

    // ---- Window ----

    private void OnTab(object? sender, RoutedEventArgs e) => ShowTab((sender as RadioButton)?.Tag as string);

    private void ShowTab(string? tab)
    {
        SongTab.IsVisible = tab == "Song";
        LyricsTab.IsVisible = tab == "Lyrics";
        FileTab.IsVisible = tab == "File";
        if (tab == "Lyrics")
        {
            LyricsBox.Focus();
        }
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (_tracks.FirstOrDefault() is not { } track)
        {
            return;
        }

        try
        {
            // Opens a Finder window with the file picked out.
            var start = new ProcessStartInfo("open") { UseShellExecute = false };
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(track.Path);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't open Finder ({ex.Message}).", warning: true);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers == KeyModifiers.Meta;
        if (e.Key == Key.Escape)
        {
            if (GenrePopup.IsOpen)
            {
                GenrePopup.IsOpen = false;
            }
            else
            {
                Close();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.S && command)
        {
            _ = SaveAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.V && command)
        {
            // In a text box, ⌘V pastes text as usual, unless there's only a picture to paste. The
            // clipboard can only be read asynchronously here, so the key is taken and the text box is
            // told to paste once it's known there's text.
            e.Handled = true;
            _ = PasteAsync(FocusManager?.GetFocusedElement() as TextBox);
        }
    }

    private async Task PasteAsync(TextBox? textBox)
    {
        if (textBox is not null)
        {
            bool hasText;
            try
            {
                hasText = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
                    && !string.IsNullOrEmpty(await clipboard.TryGetTextAsync());
            }
            catch (Exception)
            {
                hasText = false;
            }

            if (hasText)
            {
                textBox.Paste();
                return;
            }
        }

        await TryPasteImageAsync();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_isSaving)
        {
            // Stopping halfway is safe for the files, but the user would be left not knowing which saved.
            e.Cancel = true;
            ShowStatus("Saving… the window closes by itself when it's done.");
            return;
        }

        if (!_closeConfirmed && HasChanges)
        {
            // The question is asked after this returns, since a Mac dialog is awaited rather than waited on.
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        }
    }

    private async Task ConfirmCloseAsync()
    {
        if (_askingToClose)
        {
            return;
        }

        _askingToClose = true;
        try
        {
            var answer = await MessageWindow.ShowAsync(this, "Close without saving your changes?", "Lucent",
                MessageButtons.YesNo, MessageResult.No);
            if (answer == MessageResult.Yes)
            {
                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _askingToClose = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _detecting?.Cancel();
        if (_open == this)
        {
            _open = null;
        }

        // Closing an owned window can leave nothing active; hand focus back to the main window.
        (Owner as Window)?.Activate();
    }

    private sealed record LoadResult(
        IReadOnlyList<TrackTags> Tracks,
        IReadOnlyList<(string Path, string Reason)> Failed,
        IReadOnlyList<PictureItem> Pictures,
        bool PicturesDiffer);

    /// <summary>One picture in the window: the stored picture plus decoded copies to show.</summary>
    public sealed class PictureItem
    {
        private PictureItem(TagPicture picture, Bitmap? preview, Bitmap? thumbnail, int width, int height)
        {
            Picture = picture;
            Preview = preview;
            Thumbnail = thumbnail;
            Width = width;
            Height = height;
        }

        public TagPicture Picture { get; }

        public Bitmap? Preview { get; }

        public Bitmap? Thumbnail { get; }

        public int Width { get; }

        public int Height { get; }

        public string Label => Picture.TypeLabel;

        public string InfoText => Preview is null
            ? $"{Label}  ·  {ImageFormats.Name(Picture.MimeType)}  ·  {SizeText(Picture.Data.Length)}"
            : $"{Label}  ·  {Width} × {Height}  ·  {ImageFormats.Name(Picture.MimeType)}  ·  {SizeText(Picture.Data.Length)}";

        /// <summary>Decodes a picture for showing. Pictures that can't be shown are still kept.</summary>
        public static PictureItem Create(TagPicture picture)
        {
            var preview = CoverArt.Decode(picture.Data, PreviewPixels);
            var thumbnail = CoverArt.Decode(picture.Data, ThumbnailPixels);
            return new PictureItem(picture, preview?.Image, thumbnail?.Image, preview?.PixelWidth ?? 0, preview?.PixelHeight ?? 0);
        }
    }
}
