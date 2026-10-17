// STUB: owned by store worker
namespace Lucent.Mac.Updates;

public sealed class AppUpdater
{
    public event Action<string>? UpdateReady;

    public Task RunAsync() => Task.CompletedTask;

    public void InstallOnExit(bool restart) => UpdateReady?.GetType();
}
