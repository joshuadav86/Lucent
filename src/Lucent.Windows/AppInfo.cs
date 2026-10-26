using System.Reflection;
using Vortice.DXGI;

namespace Lucent;

/// <summary>The facts about this copy of Lucent and this PC that a bug report needs.</summary>
public static class AppInfo
{
    /// <summary>Lucent's version, as CI stamps it (1.0.build).</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The commit this copy was built from, or null if the build didn't record it.</summary>
    public static string? Commit { get; } = ReadCommit();

    private static string? ReadCommit()
    {
        var version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+') ?? -1;
        return plus >= 0 ? version![(plus + 1)..] : null;
    }

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // The SDK adds "+<commit>" when it can; the build number is what people can match to a release.
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    /// <summary>"Windows 11 (10.0.22631)", say. Windows 11 still calls itself 10.0, so the build tells them apart.</summary>
    public static string WindowsVersion
    {
        get
        {
            var version = Environment.OSVersion.Version;
            var name = version.Major == 10 && version.Build >= 22000 ? "Windows 11" : $"Windows {version.Major}.{version.Minor}";
            return $"{name} ({version.Major}.{version.Minor}.{version.Build}){(Environment.Is64BitOperatingSystem ? ", 64-bit" : string.Empty)}";
        }
    }

    /// <summary>The graphics cards Windows knows about, leaving out its software renderer.</summary>
    public static string GraphicsCards
    {
        get
        {
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                var names = new List<string>();
                for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
                {
                    using (adapter)
                    {
                        var description = adapter.Description1;
                        if ((description.Flags & AdapterFlags.Software) == 0)
                        {
                            names.Add(description.Description.Trim());
                        }
                    }
                }

                return names.Count > 0 ? string.Join(", ", names.Distinct()) : "none found";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
