namespace WinampInspiredVisualizer;

internal static class Diag
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lucid-diag.txt");

    public static void Log(string text)
    {
        try { System.IO.File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}"); } catch { }
    }
}
