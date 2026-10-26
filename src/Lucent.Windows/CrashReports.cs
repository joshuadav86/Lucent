using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using WinampInspiredVisualizer.Diagnostics;

namespace WinampInspiredVisualizer;

/// <summary>
/// Starts the crash reporter (Lucent.Core's <see cref="CrashReporter"/>) with what it needs from Windows: the
/// version, graphics card, errors.log, and the crashes Windows recorded in its Application event log that
/// .NET never saw, such as one inside a graphics driver.
/// </summary>
public static class CrashReports
{
    public static void Start() => CrashReporter.Start(new CrashReportHost
    {
        Platform = "Windows",
        Version = AppInfo.Version,
        SystemVersion = () => AppInfo.WindowsVersion,
        GraphicsCards = () => AppInfo.GraphicsCards,
        RecentErrors = ErrorLog.ReadTail,
        FindSystemCrashes = FindSystemCrashes,
    });

    /// <summary>
    /// Windows Error Reporting's "Application Error" (1000) entries for Lucent.exe: the faulting module and
    /// exception code. And .NET's own (1026), for what can't be caught, like a stack overflow.
    /// </summary>
    private static IEnumerable<CrashReport> FindSystemCrashes(DateTime since)
    {
        var reports = new List<CrashReport>();
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName,
                "*[System[(Provider[@Name='Application Error'] and EventID=1000 or Provider[@Name='.NET Runtime'] and EventID=1026)" +
                $" and TimeCreated[@SystemTime>='{since.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}']]]");
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null && reports.Count < 10; record = reader.ReadEvent())
            {
                using (record)
                {
                    var report = record.Id == 1000 ? FromApplicationError(record) : FromDotNetRuntime(record);
                    if (report is not null)
                    {
                        report.Time = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow;
                        reports.Add(report);
                    }
                }
            }
        }
        catch (Exception)
        {
            // An event log that can't be read just means only .NET's own crashes are reported.
        }

        return reports;
    }

    private static CrashReport? FromApplicationError(EventRecord record)
    {
        // Faulting application name, version, time stamp; module name, version, time stamp; exception code; offset.
        var values = record.Properties.Select(property => property.Value?.ToString() ?? string.Empty).ToList();
        if (values.Count < 8 || !values[0].Equals("Lucent.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var module = values[3];
        var code = values[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? values[6]
            : uint.TryParse(values[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? $"0x{number:x8}"
            : values[6];
        return new CrashReport
        {
            Kind = "Native crash",
            Version = values[1],
            Exceptions =
            {
                new CrashException
                {
                    Type = code,
                    Message = $"{Describe(code)} in {module} {values[4]}".Trim(),
                    Frames = { new CrashFrame { Function = $"{module}+{values[7]}", Module = module } },
                },
            },
        };
    }

    private static CrashReport? FromDotNetRuntime(EventRecord record)
    {
        var text = record.Properties.FirstOrDefault()?.Value?.ToString() ?? string.Empty;
        if (!text.Contains("Lucent.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var description = text.Split('\n').FirstOrDefault(line => line.StartsWith("Description:", StringComparison.Ordinal))?
            ["Description:".Length..].Trim() ?? "The .NET runtime stopped Lucent.";
        return new CrashReport
        {
            Kind = ".NET runtime",
            Exceptions = { new CrashException { Type = ".NET Runtime", Message = CrashReportScrubber.Scrub(description) } },
            SystemLog = CrashReportScrubber.Scrub(text),
        };
    }

    /// <summary>The common exception codes, in words.</summary>
    private static string Describe(string code) => code.ToLowerInvariant() switch
    {
        "0xc0000005" => "Access violation",
        "0xc00000fd" => "Stack overflow",
        "0xc0000409" => "Stack buffer overrun or fail-fast",
        "0xc0000374" => "Heap corruption",
        "0xe0434352" => ".NET exception",
        "0x80000003" => "Breakpoint",
        "0xc000001d" => "Illegal instruction",
        _ => "Crash",
    };
}
