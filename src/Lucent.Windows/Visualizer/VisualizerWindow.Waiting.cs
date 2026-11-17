using System.Windows.Threading;

namespace Lucent;

/// <summary>
/// Says so in the title bar while a visual that's been picked is still compiling, since the one before it stays on
/// screen meanwhile. That's usually a moment, but after an update every visual compiles again, and a large imported
/// one can take a minute or two. Kept in its own file.
/// </summary>
public partial class VisualizerWindow
{
    // A visual that's ready within this long changes without a word.
    private static readonly TimeSpan QuietWait = TimeSpan.FromSeconds(1);

    private DispatcherTimer? _waitTimer;
    private string _waitingFor = string.Empty;
    private DateTime _waitStarted;
    private string? _waitMessage;

    /// <summary>Watches the visual just asked for until it's showing, and says it's getting ready if that takes a while.</summary>
    private void WatchPresetReady(string label)
    {
        _waitingFor = label;
        _waitStarted = DateTime.UtcNow;
        if (_waitTimer is null)
        {
            _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _waitTimer.Tick += (_, _) => CheckPresetReady();
        }

        _waitTimer.Start();
        CheckPresetReady();
    }

    private void CheckPresetReady()
    {
        if (_host is { WaitingForPreset: true })
        {
            if (DateTime.UtcNow - _waitStarted >= QuietWait)
            {
                ShowWaitMessage($"Getting {_waitingFor} ready. After an update, a large visual can take a minute or two the first time.");
            }

            return;
        }

        _waitTimer?.Stop();
        ShowWaitMessage(null);
    }

    /// <summary>Shows (or with null, takes away) the getting-ready note, leaving any other message in its place alone.</summary>
    private void ShowWaitMessage(string? message)
    {
        var mine = string.IsNullOrEmpty(VisualizerStatus.Text) || VisualizerStatus.Text == _waitMessage;
        _waitMessage = message;
        if (mine)
        {
            VisualizerStatus.Text = message ?? string.Empty;
            VisualizerStatus.ToolTip = message;
        }
    }
}
