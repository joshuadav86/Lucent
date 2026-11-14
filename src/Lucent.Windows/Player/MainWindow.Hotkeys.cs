using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Lucent.Audio;
using Lucent.Settings;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// Global shortcuts (Options > Sound "Shortcuts in every app"): keys people choose for play or pause, next, volume and
/// so on, which work while another app is in front. None are set until people set them. Kept in its own file.
/// </summary>
public partial class MainWindow
{
    private GlobalHotkeys? _globalHotkeys;

    // The action whose shortcut is being typed in the options, if one is.
    private HotkeyAction? _typingHotkey;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyButtons = new();

    private void StartGlobalHotkeys()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        _globalHotkeys = new GlobalHotkeys(source);
        _globalHotkeys.Pressed += RunHotkey;
        Closed += (_, _) => _globalHotkeys.Dispose();
        ApplyHotkeys();
    }

    /// <summary>Registers the shortcuts in the settings, and says if another app has any of their keys.</summary>
    private void ApplyHotkeys()
    {
        if (_globalHotkeys is null)
        {
            return;
        }

        var taken = _globalHotkeys.Set(Hotkey.Read(_settings.GlobalHotkeys));
        ShowHotkeyProblem(taken.Count == 0
            ? null
            : $"Another app already uses the keys for {string.Join(", ", taken.Select(action => Hotkey.ActionLabel(action).ToLowerInvariant()))}. Pick other keys.");
    }

    private void ShowHotkeyProblem(string? problem)
    {
        HotkeyProblem.Text = problem ?? "";
        HotkeyProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RunHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.PlayPause:
                TogglePlayPause();
                break;
            case HotkeyAction.Stop:
                StopPlayback();
                break;
            case HotkeyAction.Next:
                PlayNext(wrap: true);
                break;
            case HotkeyAction.Previous:
                PlayPrevious();
                break;
            case HotkeyAction.VolumeUp:
                VolumeSlider.Value = Math.Min(VolumeSlider.Maximum, VolumeSlider.Value + 0.05);
                break;
            case HotkeyAction.VolumeDown:
                VolumeSlider.Value = Math.Max(VolumeSlider.Minimum, VolumeSlider.Value - 0.05);
                break;
            case HotkeyAction.Favorite:
                ToggleTrackFavorite();
                break;
            case HotkeyAction.ShowLucent:
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Show();
                Activate();
                break;
        }

        UpdateMediaControls();
    }

    /// <summary>Lists each action with its shortcut in Options > Sound.</summary>
    private void ShowHotkeys()
    {
        HotkeyRows.Children.Clear();
        _hotkeyButtons.Clear();
        var shortcuts = Hotkey.Read(_settings.GlobalHotkeys);
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = Hotkey.ActionLabel(action), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(label);

            var button = new Button
            {
                Content = shortcuts.TryGetValue(action, out var hotkey) ? hotkey.Label(mac: false) : "Not set",
                MinWidth = 120,
                ToolTip = "Click, then press the keys. Backspace clears it, Esc leaves it as it was.",
            };
            button.SetResourceReference(StyleProperty, "PillButton");
            button.Click += (_, _) => StartTypingHotkey(action);
            // Straight to the button too: Options is a pop-up, whose keys may not pass through the player's window.
            button.PreviewKeyDown += (_, e) => TypeHotkey(e);
            button.LostKeyboardFocus += (_, _) =>
            {
                if (_typingHotkey == action)
                {
                    StopTypingHotkey();
                }
            };
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            HotkeyRows.Children.Add(row);
            _hotkeyButtons[action] = button;
        }
    }

    private void StartTypingHotkey(HotkeyAction action)
    {
        _typingHotkey = action;
        // Let go of them all meanwhile, so pressing one that's set already comes here rather than doing what it does.
        _globalHotkeys?.Clear();
        ShowHotkeyProblem(null);
        if (_hotkeyButtons.TryGetValue(action, out var button))
        {
            button.Content = "Press keys…";
            Keyboard.Focus(button);
        }
    }

    private void StopTypingHotkey()
    {
        _typingHotkey = null;
        ApplyHotkeys();
        ShowHotkeys();
    }

    /// <summary>
    /// While a shortcut is being typed in the options, takes the keys pressed (before the usual shortcuts see them).
    /// Returns true when it took the key.
    /// </summary>
    private bool TypeHotkey(KeyEventArgs e)
    {
        if (_typingHotkey is not { } action)
        {
            return false;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Still to come: the key they're held with.
            return true;
        }

        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopTypingHotkey();
            return true;
        }

        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
        {
            _settings.GlobalHotkeys.Remove(action.ToString());
            _settings.Save();
            StopTypingHotkey();
            return true;
        }

        var name = Hotkey.KeyForWindowsCode(KeyInterop.VirtualKeyFromKey(key));
        var hotkey = new Hotkey(
            (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0)
            | (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0)
            | (modifiers.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Command : 0),
            name ?? "");
        if (name is null || !hotkey.IsUsable)
        {
            ShowHotkeyProblem(name is null
                ? "That key can't be a shortcut. Try a letter, a number, or an F key."
                : "Hold Ctrl, Alt, or the Windows key with it, so it doesn't get in the way of typing.");
            return true;
        }

        // One shortcut does one thing: an action that had these keys loses them.
        foreach (var (other, text) in _settings.GlobalHotkeys.ToList())
        {
            if (Hotkey.Parse(text) == hotkey)
            {
                _settings.GlobalHotkeys.Remove(other);
            }
        }

        _settings.GlobalHotkeys[action.ToString()] = hotkey.ToString();
        _settings.Save();
        StopTypingHotkey();
        return true;
    }
}
