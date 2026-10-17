using System.Windows;
using System.Windows.Input;

namespace WinampInspiredVisualizer;

/// <summary>A small themed box that asks for a name.</summary>
public partial class PromptWindow : Window
{
    private Func<string, string?>? _check;

    private PromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Asks for a line of text and returns it trimmed, or null if cancelled.
    /// <paramref name="check"/> returns a message when the answer can't be used, and the box stays open.
    /// </summary>
    public static string? Ask(Window owner, string heading, string hint, string action, string initial = "", Func<string, string?>? check = null)
    {
        var prompt = new PromptWindow { Owner = owner, _check = check };
        prompt.Heading.Text = heading;
        prompt.Hint.Text = hint;
        prompt.OkButton.Content = action;
        prompt.Answer.Text = initial;
        prompt.Loaded += (_, _) =>
        {
            prompt.Answer.Focus();
            prompt.Answer.SelectAll();
        };
        return prompt.ShowDialog() == true ? prompt.Answer.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => Finish();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnAnswerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Finish()
    {
        var answer = Answer.Text.Trim();
        var problem = answer.Length == 0 ? "Type a name first." : _check?.Invoke(answer);
        if (problem is not null)
        {
            Hint.Text = problem;
            Hint.SetResourceReference(ForegroundProperty, "AccentBrush");
            Answer.Focus();
            return;
        }

        DialogResult = true;
    }
}
