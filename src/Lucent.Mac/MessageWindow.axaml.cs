using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>Which buttons a <see cref="MessageWindow"/> has, like WPF's MessageBoxButton.</summary>
public enum MessageButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
}

/// <summary>What was clicked, like WPF's MessageBoxResult. Closing the window counts as Cancel (or No when there's no Cancel).</summary>
public enum MessageResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
}

/// <summary>
/// A themed message box. The Windows app uses WPF's MessageBox.Show, which waits; this is awaited:
/// <c>await MessageWindow.ShowAsync(this, text, "Lucent", MessageButtons.YesNo)</c>.
/// </summary>
public partial class MessageWindow : Window
{
    private MessageResult _result = MessageResult.None;
    private MessageResult _escape = MessageResult.Cancel;

    public MessageWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = _escape;
                Close();
            }
        };
    }

    public static async Task<MessageResult> ShowAsync(Window? owner, string text, string title = "Lucent",
        MessageButtons buttons = MessageButtons.Ok, MessageResult defaultResult = MessageResult.None)
    {
        var box = new MessageWindow { Title = title };
        box.Heading.Text = title;
        box.Body.Text = text;
        var choices = buttons switch
        {
            MessageButtons.OkCancel => new[] { MessageResult.Cancel, MessageResult.Ok },
            MessageButtons.YesNo => new[] { MessageResult.No, MessageResult.Yes },
            MessageButtons.YesNoCancel => new[] { MessageResult.Cancel, MessageResult.No, MessageResult.Yes },
            _ => new[] { MessageResult.Ok },
        };
        box._escape = choices.Contains(MessageResult.Cancel) ? MessageResult.Cancel : choices[0];
        var preferred = defaultResult != MessageResult.None ? defaultResult : choices[^1];
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice.ToString(),
                Height = 32,
                Padding = new Avalonia.Thickness(16, 0),
                IsDefault = choice == preferred,
                Theme = (Avalonia.Styling.ControlTheme?)box.FindResource("PickerButton"),
            };
            if (choice == preferred)
            {
                button.SetResourceReference(ForegroundProperty, "AccentBrush");
            }

            button.Click += (_, _) =>
            {
                box._result = choice;
                box.Close();
            };
            box.Buttons.Children.Add(button);
        }

        if (owner is { IsVisible: true })
        {
            await box.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            box.Closed += (_, _) => closed.TrySetResult();
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            box.Show();
            await closed.Task;
        }

        return box._result == MessageResult.None ? box._escape : box._result;
    }
}
