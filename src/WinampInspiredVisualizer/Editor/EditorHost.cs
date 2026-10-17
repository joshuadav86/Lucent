using System.IO;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// What the editor and converter need from the main player: pausing it while the editor plays,
/// and letting go of a file so it can be replaced.
/// </summary>
/// <remarks>
/// The player keeps the playing file open, and Windows won't replace an open file. Until
/// <see cref="AudioPlayer"/> can say which file it has open and release it by itself, this asks
/// the main window for the current path and closes and reopens the track around the save.
/// </remarks>
public sealed class EditorHost
{
    private readonly AudioPlayer _player;
    private readonly Func<string?> _currentPath;
    private readonly Action _playerStateChanged;

    /// <param name="player">The main player.</param>
    /// <param name="currentPath">The path of the track loaded in the main player, or null.</param>
    /// <param name="playerStateChanged">Called after the editor pauses or reloads the main player, to refresh its buttons.</param>
    public EditorHost(AudioPlayer player, Func<string?> currentPath, Action playerStateChanged)
    {
        _player = player;
        _currentPath = currentPath;
        _playerStateChanged = playerStateChanged;
    }

    /// <summary>The main player's volume, so the editor plays at a similar level.</summary>
    public float Volume => _player.Volume;

    /// <summary>Pauses the main player, if it's playing, so two songs don't play over each other.</summary>
    public void PauseMainPlayer()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            _playerStateChanged();
        }
    }

    /// <summary>True when the main player has <paramref name="path"/> open (playing or paused).</summary>
    public bool IsOpenInPlayer(string path) =>
        _player.HasTrack && _currentPath() is { } current && SamePath(current, path);

    /// <summary>
    /// Has the main player let go of <paramref name="path"/> for a moment. Disposing the result opens it
    /// again at the same spot, playing if it was playing. Does nothing if the player doesn't have it open.
    /// </summary>
    public IDisposable ReleaseFile(string path)
    {
        if (!IsOpenInPlayer(path))
        {
            return new Reopen(null);
        }

        var position = _player.Position;
        var wasPlaying = _player.IsPlaying;
        _player.Close();
        return new Reopen(() =>
        {
            try
            {
                _player.Load(path);
                _player.Position = position;
                if (wasPlaying)
                {
                    _player.Play();
                }
            }
            catch (Exception)
            {
                // The file is saved either way; the user can press play again.
            }

            _playerStateChanged();
        });
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private sealed class Reopen : IDisposable
    {
        private Action? _action;

        public Reopen(Action? action) => _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
