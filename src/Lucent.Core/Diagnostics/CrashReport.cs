using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace WinampInspiredVisualizer.Diagnostics;

/// <summary>
/// One crash, as Lucent keeps it until it's sent: what was thrown (or what the operating system saw), where,
/// and the errors leading up to it. Everything in it has been through <see cref="CrashReportScrubber"/>.
/// </summary>
public sealed class CrashReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>When it crashed, in UTC.</summary>
    public DateTime Time { get; set; } = DateTime.UtcNow;

    /// <summary>"Unhandled exception" for .NET code, or what the operating system calls it ("Native crash").</summary>
    public string Kind { get; set; } = "Unhandled exception";

    /// <summary>Lucent's version when it crashed (the report may go out after an update).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The exception and the ones inside it, outermost first.</summary>
    public List<CrashException> Exceptions { get; set; } = new();

    /// <summary>The newest entries of errors.log.</summary>
    public string RecentErrors { get; set; } = string.Empty;

    /// <summary>More from the operating system's own crash record, when there is one.</summary>
    public string SystemLog { get; set; } = string.Empty;

    /// <summary>How long Lucent had been running, in seconds.</summary>
    public double Uptime { get; set; }

    /// <summary>A report of a .NET exception, with the exceptions inside it.</summary>
    public static CrashReport FromException(Exception exception)
    {
        var report = new CrashReport();
        Exception? current = exception;
        // Inner exceptions can nest without end in theory; a few are all a report needs.
        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            report.Exceptions.Add(CrashException.From(current));
            current = current is AggregateException { InnerExceptions.Count: > 0 } aggregate
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }

        try
        {
            report.Uptime = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        }
        catch (Exception)
        {
            // Not knowing how long it ran doesn't stop the report.
        }

        return report;
    }

    /// <summary>The report as a Sentry event (https://develop.sentry.dev/sdk/event-payloads/).</summary>
    public JsonObject ToSentryEvent(CrashReportHost host)
    {
        // Sentry wants chained exceptions oldest first, so the innermost (the cause) leads and the
        // outermost, which is what crashed Lucent, comes last; and each stack from its first call to the crash.
        var values = new JsonArray();
        for (var i = Exceptions.Count - 1; i >= 0; i--)
        {
            var exception = Exceptions[i];
            var frames = new JsonArray();
            for (var f = exception.Frames.Count - 1; f >= 0; f--)
            {
                frames.Add(exception.Frames[f].ToSentryFrame());
            }

            var value = new JsonObject
            {
                ["type"] = exception.Type,
                ["value"] = exception.Message,
                ["mechanism"] = new JsonObject { ["type"] = Kind, ["handled"] = false },
            };
            if (frames.Count > 0)
            {
                value["stacktrace"] = new JsonObject { ["frames"] = frames };
            }

            values.Add(value);
        }

        var extra = new JsonObject { ["uptime_seconds"] = Math.Round(Uptime) };
        if (RecentErrors.Length > 0)
        {
            extra["recent_errors"] = RecentErrors;
        }

        if (SystemLog.Length > 0)
        {
            extra["system_crash_log"] = SystemLog;
        }

        return new JsonObject
        {
            ["event_id"] = Id,
            ["timestamp"] = Time.ToString("o"),
            ["platform"] = "csharp",
            ["level"] = "fatal",
            ["release"] = $"lucent@{Version}",
            ["environment"] = "production",
            ["tags"] = new JsonObject
            {
                ["platform"] = host.Platform,
                ["kind"] = Kind,
            },
            ["contexts"] = new JsonObject
            {
                ["os"] = new JsonObject { ["name"] = host.Platform, ["version"] = Safe(host.SystemVersion) },
                ["gpu"] = new JsonObject { ["name"] = Safe(host.GraphicsCards) },
                ["runtime"] = new JsonObject { ["name"] = ".NET", ["version"] = Environment.Version.ToString() },
                ["device"] = new JsonObject { ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() },
                ["app"] = new JsonObject { ["app_name"] = "Lucent", ["app_version"] = Version },
            },
            ["exception"] = new JsonObject { ["values"] = values },
            ["extra"] = extra,
        };
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return CrashReportScrubber.Scrub(read());
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}

/// <summary>One exception in a crash report.</summary>
public sealed class CrashException
{
    /// <summary>The exception's full type name, or the operating system's name for the crash (EXC_BAD_ACCESS, 0xc0000005).</summary>
    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Where it was thrown, innermost call (where it went wrong) first.</summary>
    public List<CrashFrame> Frames { get; set; } = new();

    public static CrashException From(Exception exception)
    {
        var crash = new CrashException
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = CrashReportScrubber.Scrub(exception.Message),
        };

        try
        {
            foreach (var frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames().Take(80))
            {
                var method = frame.GetMethod();
                if (method is null)
                {
                    continue;
                }

                var module = method.Module.Assembly.GetName().Name;
                var file = frame.GetFileName();
                var line = frame.GetFileLineNumber();
                crash.Frames.Add(new CrashFrame
                {
                    Function = $"{method.DeclaringType?.FullName}.{method.Name}",
                    Module = module,
                    // Only the file's name: the folder is wherever the app was built.
                    File = file is null ? null : Path.GetFileName(file.Replace('\\', '/')),
                    Line = line > 0 ? line : null,
                    InApp = module is not null && module.StartsWith("Lucent", StringComparison.Ordinal),
                });
            }
        }
        catch (Exception)
        {
            // A stack that can't be read leaves the type and message, which still say a lot.
        }

        return crash;
    }
}

/// <summary>One call on a crashed stack.</summary>
public sealed class CrashFrame
{
    public string Function { get; set; } = string.Empty;

    /// <summary>The .NET assembly, or the native library or framework, the call is in.</summary>
    public string? Module { get; set; }

    public string? File { get; set; }

    public int? Line { get; set; }

    /// <summary>Whether it's Lucent's own code rather than .NET's, a library's or the system's.</summary>
    public bool InApp { get; set; }

    public JsonObject ToSentryFrame()
    {
        var frame = new JsonObject { ["function"] = Function, ["in_app"] = InApp };
        if (Module is not null)
        {
            frame["package"] = Module;
        }

        if (File is not null)
        {
            frame["filename"] = File;
        }

        if (Line is not null)
        {
            frame["lineno"] = Line;
        }

        return frame;
    }
}
