using System.Diagnostics;
using Lucent.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// Starts the apps Lucent controls, and opens web players in a browser, for the player's Open button
/// (<see cref="IRemoteOpener"/>), through macOS's <c>open</c>.
/// </summary>
/// <remarks>Windows finds apps by their links, installers' places and Store packages instead (see Windows' Streaming/AppOpener.cs).</remarks>
internal static class AppOpener
{
    private const string Open = "/usr/bin/open";

    /// <summary>True when a process named <paramref name="name"/> (an app's executable, such as "Spotify") is running.</summary>
    public static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>Starts the app with <paramref name="bundleId"/>, saying <paramref name="missing"/> when it isn't on this Mac.</summary>
    public static Task OpenAppAsync(string bundleId, string missing, CancellationToken cancel) =>
        RunAsync(["-b", bundleId], missing, cancel);

    /// <summary>Opens <paramref name="url"/> in <paramref name="browser"/> (an app's name), or the default browser for null.</summary>
    public static Task OpenWebsiteAsync(string url, string? browser, CancellationToken cancel) =>
        RunAsync(browser is null ? new[] { url } : new[] { "-a", browser, url }, $"Lucent couldn't open {url} in your web browser.", cancel);

    private static async Task RunAsync(string[] arguments, string problem, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(Open) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new RemoteProblem(problem);
        await process.StandardError.ReadToEndAsync(cancel);
        await process.WaitForExitAsync(cancel);
        if (process.ExitCode != 0)
        {
            throw new RemoteProblem(problem);
        }
    }
}
