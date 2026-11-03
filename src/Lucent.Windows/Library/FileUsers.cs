using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lucent.Library;

/// <summary>
/// Which programs have a file open, by asking Windows' Restart Manager (the same thing installers ask), so a save
/// that's blocked can say "Ableton Live is using the file" rather than just "another program".
/// </summary>
internal static class FileUsers
{
    private const int ErrorMoreData = 234;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    /// <summary>The other programs that have any of these files open, joined into words, or null if none can be named.</summary>
    public static string? Describe(IReadOnlyList<string> paths)
    {
        try
        {
            var names = Find(paths);
            return names.Count switch
            {
                0 => null,
                1 => names[0],
                2 => $"{names[0]} and {names[1]}",
                _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
            };
        }
        catch (Exception)
        {
            // Not knowing who has it just means the plain message.
            return null;
        }
    }

    private static List<string> Find(IReadOnlyList<string> paths)
    {
        var key = new char[33];
        if (RmStartSession(out var session, 0, key) != 0)
        {
            return new List<string>();
        }

        try
        {
            var files = paths.ToArray();
            if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0)
            {
                return new List<string>();
            }

            uint needed = 0, count = 0;
            RM_PROCESS_INFO[]? found = null;
            var result = RmGetList(session, out needed, ref count, null, out _);
            for (var tries = 0; result == ErrorMoreData && tries < 3; tries++)
            {
                found = new RM_PROCESS_INFO[needed];
                count = needed;
                result = RmGetList(session, out needed, ref count, found, out _);
            }

            if (result != 0 || found is null)
            {
                return new List<string>();
            }

            var self = Environment.ProcessId;
            return found.Take((int)count)
                .Where(info => info.Process.dwProcessId != self)
                .Select(info => Name(info))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            RmEndSession(session);
        }
    }

    // The name people know a program by: its own (from its version info) if Windows gave none.
    private static string Name(RM_PROCESS_INFO info)
    {
        if (!string.IsNullOrWhiteSpace(info.strAppName))
        {
            return info.strAppName.Trim();
        }

        try
        {
            using var process = Process.GetProcessById(info.Process.dwProcessId);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, char[] strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
        uint nApplications, RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, out uint lpdwRebootReasons);
}
