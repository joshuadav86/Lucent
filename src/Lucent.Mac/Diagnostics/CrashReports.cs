using System.IO;
using System.Text;
using System.Text.Json;
using Lucent.Diagnostics;

namespace Lucent.Mac;

/// <summary>
/// Starts the crash reporter (Lucent.Core's <see cref="CrashReporter"/>) with what it needs from macOS: the
/// version, graphics chip, errors.log, and the crash reports macOS wrote for Lucent that .NET never saw,
/// such as one inside Metal or a graphics driver.
/// </summary>
public static class CrashReports
{
    public static void Start() => CrashReporter.Start(new CrashReportHost
    {
        Platform = "macOS",
        Version = AppInfo.Version,
        SystemVersion = () => AppInfo.SystemVersion,
        GraphicsCards = () => AppInfo.GraphicsCards,
        RecentErrors = ErrorLog.ReadTail,
        FindSystemCrashes = FindSystemCrashes,
    });

    // Where macOS keeps the crash reports of the user's own apps, as Lucent-<date>-<time>.ips.
    private static string ReportsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");

    private static IEnumerable<CrashReport> FindSystemCrashes(DateTime since)
    {
        var reports = new List<CrashReport>();
        try
        {
            var folder = new DirectoryInfo(ReportsFolder);
            if (!folder.Exists)
            {
                return reports;
            }

            foreach (var file in folder.GetFiles("Lucent*.ips")
                         .Where(file => file.LastWriteTimeUtc > since)
                         .OrderBy(file => file.LastWriteTimeUtc)
                         .Take(10))
            {
                if (Read(file) is { } report)
                {
                    reports.Add(report);
                }
            }
        }
        catch (Exception)
        {
            // Crash reports that can't be read just mean only .NET's own crashes are reported.
        }

        return reports;
    }

    /// <summary>
    /// A macOS .ips crash report: a line of JSON about the app, then the crash as JSON. Only what was thrown
    /// and the crashed thread's calls come across; the rest (and every path in it) stays on the Mac.
    /// </summary>
    private static CrashReport? Read(FileInfo file)
    {
        try
        {
            var text = File.ReadAllText(file.FullName, Encoding.UTF8);
            var newline = text.IndexOf('\n');
            if (newline < 0)
            {
                return null;
            }

            using var header = JsonDocument.Parse(text[..newline]);
            // 309 is a crash; hangs and other reports share the folder.
            if (!header.RootElement.TryGetProperty("bug_type", out var bugType) || bugType.GetString() != "309"
                || !string.Equals(ReadString(header.RootElement, "app_name") ?? ReadString(header.RootElement, "name"), "Lucent", StringComparison.Ordinal))
            {
                return null;
            }

            using var body = JsonDocument.Parse(text[(newline + 1)..]);
            var root = body.RootElement;
            var exception = root.TryGetProperty("exception", out var thrown) ? thrown : default;
            var type = exception.ValueKind == JsonValueKind.Object ? ReadString(exception, "type") ?? "Crash" : "Crash";
            var signal = exception.ValueKind == JsonValueKind.Object ? ReadString(exception, "signal") : null;
            var subtype = exception.ValueKind == JsonValueKind.Object ? ReadString(exception, "subtype") : null;

            var crash = new CrashException
            {
                Type = signal is null ? type : $"{type} ({signal})",
                Message = CrashReportScrubber.Scrub(subtype ?? ReadString(root, "termination", "indicator") ?? type),
                Frames = CrashedThreadFrames(root),
            };

            var notes = new StringBuilder();
            if (ReadString(root, "termination", "indicator") is { } indicator)
            {
                notes.AppendLine($"Termination: {indicator}");
            }

            // Application-specific information: what an assertion or abort() said just before the crash.
            if (root.TryGetProperty("asi", out var asi) && asi.ValueKind == JsonValueKind.Object)
            {
                foreach (var library in asi.EnumerateObject())
                {
                    if (library.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in library.Value.EnumerateArray().Take(5))
                        {
                            notes.AppendLine($"{library.Name}: {line}");
                        }
                    }
                }
            }

            return new CrashReport
            {
                Kind = "Native crash",
                Time = file.LastWriteTimeUtc,
                Version = ReadString(header.RootElement, "app_version") ?? string.Empty,
                Exceptions = { crash },
                SystemLog = CrashReportScrubber.Scrub(notes.ToString().Trim()),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The crashed thread's calls, innermost first, named by symbol where macOS knew it.</summary>
    private static List<CrashFrame> CrashedThreadFrames(JsonElement root)
    {
        var frames = new List<CrashFrame>();
        if (!root.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array)
        {
            return frames;
        }

        var images = root.TryGetProperty("usedImages", out var used) && used.ValueKind == JsonValueKind.Array
            ? used.EnumerateArray().Select(image => ReadString(image, "name") ?? "???").ToList()
            : new List<string>();

        var faulting = root.TryGetProperty("faultingThread", out var index) && index.TryGetInt32(out var number) ? number : -1;
        var thread = threads.EnumerateArray()
            .Select((value, i) => (value, i))
            .FirstOrDefault(entry => entry.i == faulting
                || (faulting < 0 && entry.value.TryGetProperty("triggered", out var triggered) && triggered.ValueKind == JsonValueKind.True))
            .value;
        if (thread.ValueKind != JsonValueKind.Object || !thread.TryGetProperty("frames", out var calls))
        {
            return frames;
        }

        foreach (var call in calls.EnumerateArray().Take(60))
        {
            var image = call.TryGetProperty("imageIndex", out var imageIndex) && imageIndex.TryGetInt32(out var i) && i >= 0 && i < images.Count
                ? images[i]
                : "???";
            var offset = call.TryGetProperty("imageOffset", out var imageOffset) && imageOffset.TryGetInt64(out var o) ? o : 0;
            frames.Add(new CrashFrame
            {
                // JIT-compiled .NET code has no image or symbol: "???" is all macOS can say about it.
                Function = ReadString(call, "symbol") ?? $"{image}+0x{offset:x}",
                Module = image,
                InApp = image.StartsWith("Lucent", StringComparison.Ordinal),
            });
        }

        return frames;
    }

    private static string? ReadString(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
