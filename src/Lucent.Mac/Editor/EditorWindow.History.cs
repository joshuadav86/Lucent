using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Lucent.Editor;

namespace Lucent.Mac;

/// <summary>
/// The history panel beside the waveform: every step taken, with its settings and when, built on the undo
/// history. Clicking a step undoes or redoes back to it.
/// </summary>
public partial class EditorWindow
{
    private void OnHistoryToggle(object? sender, RoutedEventArgs e) => ShowHistoryPanel(!HistoryPanel.IsVisible);

    private void ShowHistoryPanel(bool show)
    {
        _settings.ShowHistory = show;
        _settings.Save();
        HistoryToggle.IsChecked = show;
        HistoryPanel.IsVisible = show;
        UpdateHistoryList();
    }

    /// <summary>Fills the history list from the session; called whenever the history changes.</summary>
    private void UpdateHistoryList()
    {
        if (!HistoryPanel.IsVisible || _session is not { } session)
        {
            return;
        }

        var history = session.History;
        HistoryList.Children.Clear();
        Button? current = null;
        for (var i = 0; i < history.Count; i++)
        {
            var position = i;
            var row = new Button { Theme = FindTheme("HistoryRow"), Content = HistoryRow(history[i], i, session.HistoryPosition) };
            if (i == session.HistoryPosition)
            {
                row.Classes.Add("current");
                current = row;
            }

            row.Click += async (_, _) => await JumpToHistoryAsync(position);
            HistoryList.Children.Add(row);
        }

        current?.BringIntoView();
    }

    private Control HistoryRow(HistoryEntry entry, int index, int position)
    {
        var time = new TextBlock
        {
            Text = entry.Time.ToString("T", CultureInfo.CurrentCulture),
            FontSize = 11,
            Margin = new Thickness(8, 1, 0, 0),
            Foreground = ThemeBrush("FaintTextBrush"),
        };
        DockPanel.SetDock(time, Dock.Right);
        var title = new TextBlock
        {
            Text = index == 0 ? entry.Description : $"{index}. {entry.Description}",
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("TextBrush"),
        };
        var top = new DockPanel();
        top.Children.Add(time);
        top.Children.Add(title);
        var row = new StackPanel { Opacity = entry.IsUndone ? 0.45 : 1 };
        row.Children.Add(top);
        if (entry.Details is { } details)
        {
            row.Children.Add(new TextBlock
            {
                Text = details,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("MutedTextBrush"),
            });
        }

        ToolTip.SetTip(row, entry.IsUndone
            ? "Undone. Click to redo up to here."
            : index == position ? "Where the sound is now" : "Click to go back to here");
        return row;
    }

    private async Task JumpToHistoryAsync(int position)
    {
        if (_session is not { } session || position == session.HistoryPosition || IsBusy)
        {
            return;
        }

        var steps = Math.Abs(position - session.HistoryPosition);
        var going = position < session.HistoryPosition ? "Undoing" : "Redoing";
        FrameRange? changed = null;
        if (await RunBusyAsync($"{going} {steps} step{(steps == 1 ? string.Empty : "s")}…", async (_, _) =>
            {
                // Not cancellable, like Undo: stopping halfway would leave the history inconsistent.
                changed = await Task.Run(() => session.JumpTo(position));
            }))
        {
            ShowAudio();
            if (changed is { IsEmpty: false } range)
            {
                Waveform.Selection = range;
            }

            ShowStatus($"Now at: {session.History[session.HistoryPosition].Description}.");
        }
    }

    private IBrush? ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var brush) ? brush as IBrush : null;
}
