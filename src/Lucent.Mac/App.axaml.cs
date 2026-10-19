using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Lucent.Mac;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ErrorLog.Watch();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Like Windows: closing the player closes Lucent, whatever else is open.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var player = new MainWindow();
            desktop.MainWindow = player;

            // Music opened from the Finder (Lucent as the default player, or Open With), whether it
            // starts Lucent or Lucent is already running.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs files
                        && files.Files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList() is { Count: > 0 } paths)
                    {
                        player.OpenFiles(paths);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
