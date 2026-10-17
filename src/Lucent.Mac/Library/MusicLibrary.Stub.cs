// STUB: owned by library worker
namespace Lucent.Mac.Library;

public sealed class MusicLibrary : IDisposable
{
    public void SetFolders(IEnumerable<string> folders, IEnumerable<string> hidden)
    {
    }

    public Task RereadTagsAsync(IEnumerable<string> paths) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
