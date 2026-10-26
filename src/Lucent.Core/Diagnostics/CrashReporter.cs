using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lucent.Settings;

namespace Lucent.Diagnostics;

/// <summary>What each app tells the crash reporter about itself and its operating system.</summary>
public sealed class CrashReportHost
{
    /// <summary>"Windows" or "macOS".</summary>
    public required string Platform { get; init; }

    public required string Version { get; init; }

    public required Func<string> SystemVersion { get; init; }

    public required Func<string> GraphicsCards { get; init; }

    /// <summary>The newest characters of errors.log, at most the number given.</summary>
    public required Func<int, string> RecentErrors { get; init; }

    /// <summary>
    /// Crashes the operating system recorded for Lucent since the time given (UTC): the ones .NET never saw,
    /// in a graphics driver, Metal or Direct3D, or a stack overflow. Already scrubbed.
    /// </summary>
    public required Func<DateTime, IEnumerable<CrashReport>> FindSystemCrashes { get; init; }
}

/// <summary>
/// Sends crash reports to Lucent's developer without asking, unless Options › Updates says not to. A crash
/// is written to "Crash reports" in Lucent's data folder as it happens and sent the next time Lucent starts,
/// along with any crash the operating system caught that Lucent couldn't. Reports go to Sentry, at the
/// address CI builds in from the LUCENT_CRASH_DSN repository secret; a build without it sends nothing and
/// keeps nothing. No file paths, names, or library contents go in a report (see <see cref="CrashReportScrubber"/>).
/// </summary>
public static class CrashReporter
{
    // Enough for a crash at every start for a while without filling the disk or Sentry's free quota.
    private const int MaxKept = 10;
    private const int MaxSentPerStart = 5;
    private const int MaxRecentErrors = 3000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    // Sending waits a moment so it never slows the start, but not long: a Lucent that crashes a few seconds
    // after every launch has to get its reports out before the next crash, or none ever arrive.
    private static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(2);

    // A .NET crash is also caught by the operating system, a moment later; one report is enough.
    private static readonly TimeSpan SameCrash = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static CrashReportHost? _host;
    private static bool _enabled;

    private static string Folder => Path.Combine(AppDataFolder.Path, "Crash reports");

    // The last time the operating system's crash records were looked through.
    private static string CheckedFile => Path.Combine(Folder, "checked");

    /// <summary>The Sentry address built in by CI, or null in a build without one.</summary>
    public static Uri? Dsn { get; } = ReadDsn();

    /// <summary>Whether reports are kept and sent: the Options switch. Turning it off drops any waiting.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                DeletePending();
            }
        }
    }

    /// <summary>Watches for crashes from here on, and in the background sends what earlier runs left.</summary>
    public static void Start(CrashReportHost host)
    {
        if (Dsn is null || _host is not null)
        {
            return;
        }

        _host = host;
        _enabled = AppSettings.Load().SendCrashReports;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Record(exception);
            }
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SendDelay).ConfigureAwait(false);
                CollectSystemCrashes();
                await SendPendingAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Reporting a crash must never cause one.
            }
        });
    }

    /// <summary>Writes a report for an exception that's about to end Lucent. Sent the next time it starts.</summary>
    public static void Record(Exception exception)
    {
        if (!_enabled || _host is null)
        {
            return;
        }

        try
        {
            var report = CrashReport.FromException(exception);
            report.Version = _host.Version;
            report.RecentErrors = CrashReportScrubber.Scrub(_host.RecentErrors(MaxRecentErrors));
            Save(report);
        }
        catch (Exception)
        {
            // Lucent is already going down; this mustn't make it worse.
        }
    }

    private static void Save(CrashReport report)
    {
        Directory.CreateDirectory(Folder);
        var pending = Pending().ToList();
        // Oldest go first when there are too many: the newest crashes are the ones on the current build.
        foreach (var old in pending.OrderBy(file => file.LastWriteTimeUtc).Take(Math.Max(0, pending.Count - MaxKept + 1)))
        {
            old.Delete();
        }

        File.WriteAllText(Path.Combine(Folder, report.Id + ".json"), JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
    }

    /// <summary>Adds the crashes the operating system recorded since the last look, except the ones .NET already reported.</summary>
    private static void CollectSystemCrashes()
    {
        var now = DateTime.UtcNow;
        var since = ReadChecked();
        WriteChecked(now);
        // The first run with crash reports only starts the clock: older crashes may be from long-fixed builds.
        if (since is null || !_enabled || _host is null)
        {
            return;
        }

        var known = Pending().Select(Load).OfType<CrashReport>().Select(report => report.Time).ToList();
        foreach (var crash in _host.FindSystemCrashes(since.Value))
        {
            if (known.Any(time => (time - crash.Time).Duration() < SameCrash))
            {
                continue;
            }

            crash.Version = string.IsNullOrEmpty(crash.Version) ? _host.Version : crash.Version;
            crash.RecentErrors = CrashReportScrubber.Scrub(_host.RecentErrors(MaxRecentErrors));
            Save(crash);
            known.Add(crash.Time);
        }
    }

    private static async Task SendPendingAsync()
    {
        if (!_enabled || _host is null || Dsn is null)
        {
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var sent = 0;
        foreach (var file in Pending().OrderBy(file => file.LastWriteTimeUtc))
        {
            var report = Load(file);
            if (report is null || DateTime.UtcNow - report.Time > MaxAge)
            {
                file.Delete();
                continue;
            }

            if (sent++ >= MaxSentPerStart || !_enabled)
            {
                break;
            }

            var outcome = await SendAsync(http, report).ConfigureAwait(false);
            if (outcome == SendOutcome.TryLater)
            {
                // Offline, or Sentry is busy: the rest wait for the next start too.
                break;
            }

            file.Delete();
        }
    }

    private enum SendOutcome
    {
        Sent,
        Refused,
        TryLater,
    }

    private static async Task<SendOutcome> SendAsync(HttpClient http, CrashReport report)
    {
        var dsn = Dsn!;
        var key = dsn.UserInfo.Split(':')[0];
        var path = dsn.AbsolutePath.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var project = path[(slash + 1)..];
        var prefix = path[..Math.Max(0, slash)];
        var endpoint = new Uri($"{dsn.Scheme}://{dsn.Authority}{prefix}/api/{project}/envelope/");

        var payload = report.ToSentryEvent(_host!).ToJsonString();
        var envelope = new StringBuilder()
            .Append(new JsonObject { ["event_id"] = report.Id, ["sent_at"] = DateTime.UtcNow.ToString("o") }.ToJsonString()).Append('\n')
            .Append(new JsonObject { ["type"] = "event", ["length"] = Encoding.UTF8.GetByteCount(payload) }.ToJsonString()).Append('\n')
            .Append(payload).Append('\n')
            .ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/x-sentry-envelope"),
        };
        request.Headers.TryAddWithoutValidation("X-Sentry-Auth", $"Sentry sentry_version=7, sentry_client=lucent/{_host!.Version}, sentry_key={key}");

        try
        {
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode ? SendOutcome.Sent
                : status == 429 || status >= 500 ? SendOutcome.TryLater
                // Anything else (a bad key, a report too big) won't go any better next time.
                : SendOutcome.Refused;
        }
        catch (Exception)
        {
            return SendOutcome.TryLater;
        }
    }

    private static IEnumerable<FileInfo> Pending()
    {
        var folder = new DirectoryInfo(Folder);
        return folder.Exists ? folder.GetFiles("*.json") : Enumerable.Empty<FileInfo>();
    }

    private static CrashReport? Load(FileInfo file)
    {
        try
        {
            return JsonSerializer.Deserialize<CrashReport>(File.ReadAllText(file.FullName, Encoding.UTF8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void DeletePending()
    {
        try
        {
            foreach (var file in Pending())
            {
                file.Delete();
            }
        }
        catch (Exception)
        {
            // Left for the next start, which drops them while the switch is off.
        }
    }

    private static DateTime? ReadChecked()
    {
        try
        {
            return File.Exists(CheckedFile)
                && DateTime.TryParse(File.ReadAllText(CheckedFile), null, System.Globalization.DateTimeStyles.RoundtripKind, out var time)
                ? time.ToUniversalTime()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteChecked(DateTime time)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CheckedFile, time.ToString("o"));
        }
        catch (Exception)
        {
            // The next start looks back from the last time it could write this.
        }
    }

    private static Uri? ReadDsn()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LUCENT_CRASH_DSN");
        var built = typeof(CrashReporter).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CrashReportDsn")?.Value;
        var text = !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : built;
        return Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var dsn) && dsn.UserInfo.Length > 0 && dsn.AbsolutePath.Length > 1
            ? dsn
            : null;
    }
}
