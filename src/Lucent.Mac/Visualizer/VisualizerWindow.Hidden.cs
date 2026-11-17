using Avalonia.Controls;
using Lucent.Settings;
using Lucent.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// Hiding visuals the user doesn't like, so the picker, Auto, N and B leave them out, and showing them again from
/// the picker's Hidden list. Kept in its own file.
/// </summary>
public partial class VisualizerWindow
{
    /// <summary>Whether the user hid this visual.</summary>
    private bool IsHidden(VisualChoice choice) =>
        _settings.HiddenVisuals.Contains(choice.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a hidden visual is being left out right now: everywhere but the Hidden list.</summary>
    private bool IsLeftOut(VisualChoice choice) =>
        _settings.ActiveVisualList != AppSettings.HiddenListName && IsHidden(choice);

    /// <summary>Hides the visual that's showing and moves on to the next one.</summary>
    public void HideCurrent()
    {
        if (CurrentChoice() is { } current)
        {
            SetHidden(current, true);
        }
    }

    /// <summary>"Hide" or "Unhide" for a visual, for the menus.</summary>
    private MenuItem HideItem(VisualChoice choice)
    {
        var hidden = IsHidden(choice);
        return Item(hidden ? "Unhide" : "Hide", null, () => SetHidden(choice, !hidden), hidden ? null : MenuTips.Hide);
    }

    /// <summary>
    /// Hides a visual or shows it again. If the one on screen is hidden it moves on to the next one in the list,
    /// and when the last hidden visual is shown again the picker goes back to All Visuals.
    /// </summary>
    private void SetHidden(VisualChoice choice, bool hidden)
    {
        _settings.HiddenVisuals.RemoveAll(name => string.Equals(name, choice.Name, StringComparison.OrdinalIgnoreCase));
        if (hidden)
        {
            _settings.HiddenVisuals.Add(choice.Name);
        }

        if (_settings.ActiveVisualList == AppSettings.HiddenListName && _settings.HiddenVisuals.Count == 0)
        {
            _settings.ActiveVisualList = null;
        }

        _settings.Save();
        var showing = CurrentChoice() == choice;
        var position = _shown.IndexOf(choice);
        ShowActiveList();
        if (showing && !_shown.Contains(choice) && _shown.Count > 0 && _host is not null)
        {
            // What was after it in the list is in its place now.
            _host.PresetIndex = _shown[Math.Max(0, position) % _shown.Count].Index;
            ShowPreset();
        }

        var message = hidden
            ? $"{choice.Name} is hidden. The picker's Hidden list can bring it back."
            : $"{choice.Name} is back in the picker and Auto.";
        VisualizerStatus.Text = message;
        ToolTip.SetTip(VisualizerStatus, message);
    }

    /// <summary>Shows every hidden visual again.</summary>
    private void UnhideAll()
    {
        _settings.HiddenVisuals.Clear();
        UseList(null);
    }
}
