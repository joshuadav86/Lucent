using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lucent.Settings;

namespace Lucent;

/// <summary>
/// %AppData%\Lucent\errors.log: the errors that would otherwise crash Lucent without a trace. Bug reports
/// include its last few entries, so there's something to go on besides "it closed".
/// </summary>
public static class ErrorLog
{
    // Kept small: only the newest entries matter, and a bug report can only carry so much.
    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(AppDataFolder.Path, "errors.log");

    /// <summary>Records anything thrown on the UI thread, a background thread or an unobserved task.</summary>
    public static void Watch(System.Windows.Application app)
    {
        app.DispatcherUnhandledException += (_, e) => Write("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Background", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Task", e.Exception);
    }

    public static void Write(string where, Exception? exception)
    {
        if (exception is not null)
        {
            Append(where, exception.ToString());
        }
    }

    /// <summary>Records a problem that didn't throw, such as part of a MilkDrop preset that had to be left out.</summary>
    public static void Write(string where, string message) => Append(where, message);

    private static void Append(string where, string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppDataFolder.Path);
                TrimIfLarge();
                File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where} ({AppInfo.Version}): {text}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Lucent is already in trouble; failing to write the log mustn't make it worse.
        }
    }

    /// <summary>The newest <paramref name="maxChars"/> characters of the log, or an empty string if there's none.</summary>
    public static string ReadTail(int maxChars)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(Path))
                {
                    return string.Empty;
                }

                var text = File.ReadAllText(Path, Encoding.UTF8).Trim();
                return text.Length <= maxChars ? text : "…" + text[^maxChars..];
            }
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static void TrimIfLarge()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length <= MaxBytes)
        {
            return;
        }

        var text = File.ReadAllText(Path, Encoding.UTF8);
        File.WriteAllText(Path, text[(text.Length / 2)..], Encoding.UTF8);
    }
}
