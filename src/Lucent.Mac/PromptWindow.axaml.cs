using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>A small themed box that asks for a name.</summary>
public partial class PromptWindow : Window
{
    private Func<string, string?>? _check;

    public PromptWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
    }

    /// <summary>
    /// Asks for a line of text and returns it trimmed, or null if cancelled.
    /// <paramref name="check"/> returns a message when the answer can't be used, and the box stays open.
    /// </summary>
    /// <remarks>Windows' version waits in ShowDialog; Avalonia's dialogs are awaited instead.</remarks>
    public static async Task<string?> AskAsync(Window owner, string heading, string hint, string action, string initial = "", Func<string, string?>? check = null)
    {
        var prompt = new PromptWindow { _check = check };
        prompt.Heading.Text = heading;
        prompt.Hint.Text = hint;
        prompt.OkButton.Content = action;
        prompt.Answer.Text = initial;
        prompt.Opened += (_, _) =>
        {
            prompt.Answer.Focus();
            prompt.Answer.SelectAll();
        };
        var answered = await prompt.ShowDialog<bool>(owner);
        return answered ? (prompt.Answer.Text ?? string.Empty).Trim() : null;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Finish();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAnswerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void Finish()
    {
        var answer = (Answer.Text ?? string.Empty).Trim();
        var problem = answer.Length == 0 ? "Type a name first." : _check?.Invoke(answer);
        if (problem is not null)
        {
            Hint.Text = problem;
            Hint.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            Answer.Focus();
            return;
        }

        Close(true);
    }
}
