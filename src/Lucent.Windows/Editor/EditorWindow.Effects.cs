using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lucent.Editor;
using Lucent.Editor.Effects;

namespace Lucent;

/// <summary>
/// The effects menu: every built-in effect and tool in one searchable list, like the plugin browser. Typing finds
/// an effect by name or what it does; the left column narrows the list to the recent ones or one category (Level,
/// Repair, Channels...). Choosing one runs it, or opens its card of settings above the Effects button.
/// </summary>
public partial class EditorWindow
{
    /// <summary>The left column's choice: null for every effect, <see cref="EffectCatalog.Recent"/>, or a category.</summary>
    private string? _effectSection;
    private DateTime _effectsPopupClosedAt;

    private void OnEffects(object sender, RoutedEventArgs e) => ShowEffects();

    private void ShowEffects()
    {
        // Clicking the button that opened it closes it (the click outside already did), rather than opening it again.
        if (_session is null || IsBusy || DateTime.UtcNow - _effectsPopupClosedAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        ShowEffectSections();
        ShowEffectList();
        EffectScope.Text = Selection.IsEmpty ? "Changes the whole sound" : "Changes the selection";
        EffectScroller.ScrollToTop();
        EffectsPopup.IsOpen = true;

        // Ready to type straight away. An earlier search is selected, so typing replaces it.
        Dispatcher.InvokeAsync(() =>
        {
            if (EffectsPopup.IsOpen)
            {
                EffectSearchBox.Focus();
                EffectSearchBox.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void OnEffectsPopupClosed(object? sender, EventArgs e) => _effectsPopupClosedAt = DateTime.UtcNow;

    private void ShowEffectSections()
    {
        EffectSections.Children.Clear();
        EffectSections.Children.Add(EffectSectionButton("All effects", null, EffectCatalog.All.Count));
        EffectSections.Children.Add(EffectSectionButton(EffectCatalog.Recent, EffectCatalog.Recent, _settings.RecentEffects.Count));
        EffectSections.Children.Add(new TextBlock
        {
            Text = "CATEGORIES",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 12, 0, 4),
            Foreground = (Brush)FindResource("MutedTextBrush"),
        });
        foreach (var category in EffectCatalog.Categories)
        {
            EffectSections.Children.Add(EffectSectionButton(category, category, EffectCatalog.All.Count(item => item.Category == category)));
        }
    }

    private Button EffectSectionButton(string label, string? section, int count)
    {
        var number = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("FaintTextBrush"),
        };
        DockPanel.SetDock(number, Dock.Right);
        var content = new DockPanel();
        content.Children.Add(number);
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        var button = new Button { Style = (Style)FindResource("PluginRow"), Content = content, Focusable = false };
        if (section == _effectSection)
        {
            button.Background = (Brush)FindResource("AccentSoftBrush");
        }

        button.Click += (_, _) =>
        {
            _effectSection = section;
            ShowEffectSections();
            ShowEffectList();
            EffectScroller.ScrollToTop();
        };
        return button;
    }

    /// <summary>The effects in the chosen section that match the search, under their categories.</summary>
    private void ShowEffectList()
    {
        var search = EffectSearchBox.Text.Trim();
        ClearEffectSearchButton.Visibility = search.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var groups = EffectCatalog.Shown(_effectSection, search, _settings.RecentEffects);
        var shown = groups.Sum(group => group.Items.Count);
        var all = EffectCatalog.All.Count;
        EffectCount.Text = shown == all ? $"{all} effects" : $"{shown} of {all}";

        EffectRows.Children.Clear();
        if (shown == 0)
        {
            EffectRows.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("OptionHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 6, 8, 0),
                Text = _effectSection == EffectCatalog.Recent && search.Length == 0
                    ? "Effects you use show up here, so they're quick to find again."
                    : "No effects match. Try fewer words, or look in All effects. Installed VST3 effects are under Plugins.",
            });
            return;
        }

        foreach (var (heading, items) in groups)
        {
            EffectRows.Children.Add(PluginHeading(heading, items.Count, first: EffectRows.Children.Count == 0));
            foreach (var item in items)
            {
                EffectRows.Children.Add(EffectRow(item));
            }
        }
    }

    private Button EffectRow(EffectItem item)
    {
        var channels = _session?.Audio.ChannelCount ?? 2;
        var fits = EffectCatalog.Fits(item, channels);
        var chosen = item.Id switch
        {
            "scope-both" => Waveform.SelectedChannel is null,
            "scope-left" => Waveform.SelectedChannel == 0,
            "scope-right" => Waveform.SelectedChannel == 1,
            _ => false,
        };

        var check = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("CheckIcon"),
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(8, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = chosen ? Visibility.Visible : Visibility.Collapsed,
        };
        DockPanel.SetDock(check, Dock.Right);
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        text.Children.Add(new TextBlock
        {
            Text = fits ? item.Hint : item.Needs == EffectNeeds.Stereo ? "Needs a stereo sound" : "Needs a mono sound",
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("FaintTextBrush"),
        });
        var content = new DockPanel();
        content.Children.Add(check);
        content.Children.Add(text);

        var row = new Button
        {
            Style = (Style)FindResource("PluginRow"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content,
            Tag = item,
            IsEnabled = fits,
            Opacity = fits ? 1 : 0.45,
            ToolTip = item.Hint,
        };
        row.Click += async (_, _) => await RunEffectAsync(item);
        return row;
    }

    /// <summary>Runs an effect from the menu, or opens its card, and puts it at the top of Recent.</summary>
    private async Task RunEffectAsync(EffectItem item)
    {
        EffectsPopup.IsOpen = false;
        if (_session is not { } session || IsBusy || !EffectCatalog.Fits(item, session.Audio.ChannelCount))
        {
            return;
        }

        EffectCatalog.Used(_settings, item.Id);
        _settings.Save();
        var none = new RoutedEventArgs();
        switch (item.Id)
        {
            case "normalize-peak-0.1":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Peak, -0.1));
                break;
            case "normalize-peak-1":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Peak, -1));
                break;
            case "normalize-peak-3":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Peak, -3));
                break;
            case "normalize-lufs-14":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Loudness, -14));
                break;
            case "normalize-lufs-16":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Loudness, -16));
                break;
            case "normalize-lufs-23":
                await ApplyAsync(new NormalizeEffect(NormalizeTarget.Loudness, -23));
                break;
            case "volume":
                OnGain(this, none);
                break;
            case "fade-in":
                await ApplyAsync(new FadeEffect(FadeDirection.In), FadeRange(FadeDirection.In));
                break;
            case "fade-out":
                await ApplyAsync(new FadeEffect(FadeDirection.Out), FadeRange(FadeDirection.Out));
                break;
            case "pitch":
                OnPitch(this, none);
                break;
            case "tempo":
                OnTempo(this, none);
                break;
            case "reverse":
                await ApplyAsync(new ReverseEffect());
                break;
            case "eq":
                OnEq(this, none);
                break;
            case "dynamics":
                OnDynamics(this, none);
                break;
            case "hum-auto":
                await ApplyAsync(new HumRemovalEffect(null));
                break;
            case "hum-50":
                await ApplyAsync(new HumRemovalEffect(50));
                break;
            case "hum-60":
                await ApplyAsync(new HumRemovalEffect(60));
                break;
            case "clicks":
                ClickPopup.IsOpen = true;
                break;
            case "noise":
                NoisePopup.IsOpen = true;
                break;
            case "dc-offset":
                await ApplyAsync(new DcOffsetEffect());
                break;
            case "swap-channels":
                await ApplyAsync(new SwapChannelsEffect(), bothChannels: true);
                break;
            case "mono-mix":
                await ApplyAsync(new MonoMixEffect(), bothChannels: true);
                break;
            case "width":
                WidthPopup.IsOpen = true;
                break;
            case "invert":
                await ApplyAsync(new InvertEffect());
                break;
            case "to-mono":
                await ConvertChannelsAsync(1);
                break;
            case "to-stereo":
                await ConvertChannelsAsync(2);
                break;
            case "scope-both":
                SetChannelScope(null);
                break;
            case "scope-left":
                SetChannelScope(0);
                break;
            case "scope-right":
                SetChannelScope(1);
                break;
            case "silence":
                await ApplyAsync(new SilenceEffect());
                break;
            case "stems":
                OnStems(this, none);
                break;
            case "analyze":
                OnAnalyze(this, none);
                break;
        }
    }

    /// <summary>Which channels edits and effects change: both (null), or only the left (0) or right (1).</summary>
    private void SetChannelScope(int? channel)
    {
        Waveform.SelectedChannel = channel;
        ShowStatus(channel is null
            ? "Edits and effects now change both channels."
            : $"Select part of the sound, and effects will change only the {(channel == 0 ? "left" : "right")} channel. Alt+drag does this too.");
    }

    private void OnEffectSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (EffectsPopup.IsOpen)
        {
            ShowEffectList();
            EffectScroller.ScrollToTop();
        }
    }

    private void OnClearEffectSearch(object sender, RoutedEventArgs e)
    {
        EffectSearchBox.Clear();
        EffectSearchBox.Focus();
    }

    private IReadOnlyList<Button> ShownEffectRows => EffectRows.Children.OfType<Button>().Where(row => row.IsEnabled).ToList();

    private async void OnEffectSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Runs the first match, like clicking it.
                e.Handled = true;
                if (ShownEffectRows.FirstOrDefault()?.Tag is EffectItem first)
                {
                    await RunEffectAsync(first);
                }

                break;
            case Key.Escape when EffectSearchBox.Text.Length > 0:
                EffectSearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Down when ShownEffectRows.Count > 0:
                // Into the list, to choose with the arrow keys and Enter.
                ShownEffectRows[0].Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Up and Down move through the list (Up from the top goes back to the search); Esc closes the menu.</summary>
    private void OnEffectsKeyDown(object sender, KeyEventArgs e)
    {
        // (Esc in the search clears it first.)
        if (e.Key == Key.Escape && !(e.OriginalSource is TextBox { Text.Length: > 0 }))
        {
            EffectsPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down) || Keyboard.FocusedElement is not Button focused)
        {
            return;
        }

        var rows = ShownEffectRows.ToList();
        var index = rows.IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        var next = index + (e.Key == Key.Down ? 1 : -1);
        if (next < 0)
        {
            EffectSearchBox.Focus();
        }
        else if (next < rows.Count)
        {
            rows[next].Focus();
            rows[next].BringIntoView();
        }

        e.Handled = true;
    }
}
