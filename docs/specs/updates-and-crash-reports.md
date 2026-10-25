# Updates and crash reports

## Updates

- Velopack checks 20 s after start, then every 4 hours, downloads in the background, and installs when Lucent closes.
- **Install updates automatically** (Options > Updates, default on). Off: it downloads and shows "ready", and installs only on "Restart and install".
- **Channels:** stable (default) or every build. Until the first stable release exists, everyone follows the "latest" build.
- What's new shows only changes to that platform.
- Settings persist between updates (`FreshStart.Generation` is 0). Raise it only if Joshua asks for a fresh start.

## Crash reports

- Sent in the background, no pop-up. Switch: Options > Updates "Send crash reports" (default on).
- Saved at crash time, sent 2 s after the next start, to Sentry over plain HTTP (no SDK). No DSN in the build means nothing is sent.
- Everything personal is scrubbed (`CrashReportScrubber`): paths, URLs, emails, user and machine names. No IP or location.
- Reports carry the GPU, release, uptime, and the tail of `errors.log`.

## Report a bug

The Options button opens a pre-filled GitHub issue (`bug_report.yml`) in the browser.

## Data folder

`%AppData%\Lucent` on Windows, `~/Library/Application Support/Lucent` on Mac. Holds settings, `errors.log`, skins, chains, stems, and recordings in progress.

## Windows

Crash reports read the Application event log (1000/1026). Installs per user with no admin rights; not yet code-signed (SmartScreen warns).

## Mac

Crash reports read `~/Library/Logs/DiagnosticReports/Lucent*.ips`. Signed and notarized (Developer ID). Keychain items are re-saved once per signing change.

## Parity gaps

Windows code signing waits on Azure identity validation.

## Code

- `Lucent.Core/Diagnostics/`, `Lucent.Core/Settings/AppDataFolder.cs`, `FreshStart.cs`
- `Updates/AppUpdater.cs`, `CrashReports.cs`, `BugReport.cs`, `ErrorLog.cs` in each app
- Settings: `EveryBuild`, `InstallUpdatesAutomatically`, crash-report switch in `AppSettings`
