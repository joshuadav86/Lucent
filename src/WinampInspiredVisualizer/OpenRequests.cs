using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace WinampInspiredVisualizer;

/// <summary>
/// Keeps songs opened from Explorer in one Lucent: when Lucent is already running, a second Lucent
/// started to open a song hands the song to it over a pipe and exits.
/// </summary>
public static class OpenRequests
{
    private static readonly string PipeName = $"Lucent.Open.{Environment.UserName}";

    /// <summary>Hands the files to a running Lucent. False if there isn't one.</summary>
    public static bool SendToRunning(IReadOnlyList<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(500);
            // Explorer let this Lucent come to the front; let the running one do it instead.
            AllowSetForegroundWindow(AsfwAny);
            using var writer = new StreamWriter(pipe);
            foreach (var path in paths)
            {
                writer.WriteLine(Path.GetFullPath(path));
            }

            return true;
        }
        catch (Exception)
        {
            // Nothing listening (no Lucent running), or it closed as we connected: open them here.
            return false;
        }
    }

    /// <summary>Passes on the files other Lucents hand over, on a background thread, until Lucent closes.</summary>
    public static async void Listen(Action<IReadOnlyList<string>> open)
    {
        while (true)
        {
            var paths = new List<string>();
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 0)
                    {
                        paths.Add(line);
                    }
                }
            }
            catch (IOException)
            {
                // Another Lucent (started without a song) already listens; it gets them instead.
                return;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Listening for songs opened from Explorer", ex);
                return;
            }

            if (paths.Count > 0)
            {
                open(paths);
            }
        }
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
