using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lucent.Editor;

namespace Lucent;

/// <summary>
/// The history panel beside the waveform: every step taken, with its settings and when, built on the undo
/// history. Clicking a step undoes or redoes back to it.
/// </summary>
public partial class EditorWindow
{
    // Set while the list is being rebuilt, so selecting the current step doesn't count as a click.
    private bool _showingHistory;

    private void OnHistoryToggle(object sender, RoutedEventArgs e) => ShowHistoryPanel(HistoryPanel.Visibility != Visibility.Visible);

    private void ShowHistoryPanel(bool show)
    {
        _settings.ShowHistory = show;
        _settings.Save();
        HistoryToggle.IsChecked = show;
        HistoryPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateHistoryList();
    }

    /// <summary>Fills the history list from the session; called whenever the history changes.</summary>
    private void UpdateHistoryList()
    {
        if (HistoryPanel.Visibility != Visibility.Visible || _session is not { } session)
        {
            return;
        }

        _showingHistory = true;
        try
        {
            var history = session.History;
            HistoryList.Items.Clear();
            for (var i = 0; i < history.Count; i++)
            {
                HistoryList.Items.Add(new ListBoxItem { Content = HistoryRow(history[i], i), Tag = i });
            }

            HistoryList.SelectedIndex = session.HistoryPosition;
            HistoryList.ScrollIntoView(HistoryList.SelectedItem);
        }
        finally
        {
            _showingHistory = false;
        }
    }

    private FrameworkElement HistoryRow(HistoryEntry entry, int index)
    {
        var time = new TextBlock
        {
            Text = entry.Time.ToString("T", CultureInfo.CurrentCulture),
            FontSize = 11,
            Margin = new Thickness(8, 1, 0, 0),
            Foreground = (Brush)FindResource("FaintTextBrush"),
        };
        DockPanel.SetDock(time, Dock.Right);
        var title = new TextBlock
        {
            Text = index == 0 ? entry.Description : $"{index}. {entry.Description}",
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextBrush"),
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
                Foreground = (Brush)FindResource("MutedTextBrush"),
            });
        }

        row.ToolTip = entry.IsUndone
            ? "Undone. Click to redo up to here."
            : index == _session?.HistoryPosition ? "Where the sound is now" : "Click to go back to here";
        return row;
    }

    private async void OnHistoryPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_showingHistory || HistoryList.SelectedItem is not ListBoxItem { Tag: int position } || _session is not { } session ||
            position == session.HistoryPosition)
        {
            return;
        }

        if (IsBusy)
        {
            UpdateHistoryList();
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
        else
        {
            UpdateHistoryList();
        }
    }
}
