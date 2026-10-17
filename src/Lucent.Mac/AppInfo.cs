using System.Reflection;
using Lucent.Mac.Interop;
using SharpMetal.Metal;

namespace Lucent.Mac;

/// <summary>The facts about this copy of Lucent and this Mac that a bug report needs.</summary>
public static class AppInfo
{
    /// <summary>Lucent's version, as CI stamps it (0.1.build).</summary>
    public static string Version { get; } = ReadVersion();

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

    /// <summary>"macOS 15.1 (Apple Silicon)", say. Goes in the bug report's operating system field.</summary>
    public static string SystemVersion
    {
        get
        {
            var version = Environment.OSVersion.Version;
            var chip = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "Apple Silicon"
                : "Intel";
            return $"macOS {version.Major}.{version.Minor}{(version.Build > 0 ? $".{version.Build}" : string.Empty)} ({chip})";
        }
    }

    /// <summary>The graphics chip Metal draws with.</summary>
    public static string GraphicsCards
    {
        get
        {
            try
            {
                var device = MTLDevice.CreateSystemDefaultDevice();
                return device.NativePtr == IntPtr.Zero ? "none found" : StringHelper.Read(device.Name) ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
