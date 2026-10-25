using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// What the screen showing the visualizer can do: whether Windows has HDR turned on for it,
/// the brightness Windows uses for normal (SDR) white there, its peak brightness, in nits, and how
/// many times a second it refreshes.
/// </summary>
public readonly record struct DisplayInfo(bool HdrOn, float SdrWhiteNits, float PeakNits, int RefreshRate = 60)
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetSdrWhiteLevel = 11;

    public static DisplayInfo Standard => new(false, 80f, 80f);

    /// <summary>Describes the screen that shows most of the given window. Safe to call from any thread.</summary>
    public static DisplayInfo ForWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var window))
        {
            return Standard;
        }

        // A fresh factory each time, so a screen that was just switched to HDR (or plugged in) is seen.
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIOutput6? best = null;
        long bestArea = -1;
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var bounds = output.Description.DesktopCoordinates;
                        long width = Math.Min(window.Right, bounds.Right) - Math.Max(window.Left, bounds.Left);
                        long height = Math.Min(window.Bottom, bounds.Bottom) - Math.Max(window.Top, bounds.Top);
                        var area = Math.Max(width, 0) * Math.Max(height, 0);
                        if (area > bestArea && output.QueryInterfaceOrNull<IDXGIOutput6>() is { } output6)
                        {
                            best?.Dispose();
                            best = output6;
                            bestArea = area;
                        }
                    }
                }
            }
        }

        if (best is null)
        {
            return Standard;
        }

        using (best)
        {
            var description = best.Description1;
            var hdrOn = description.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
            var (sdrWhite, refreshRate) = ReadTarget(description.DeviceName);
            var white = sdrWhite ?? 200f;

            // Some screens report nonsense here; fall back to a typical HDR monitor.
            var peak = description.MaxLuminance is >= 200f and <= 10000f ? description.MaxLuminance : 1000f;
            return new DisplayInfo(hdrOn, white, Math.Max(peak, white * 1.25f), refreshRate);
        }
    }

    /// <summary>
    /// The "SDR content brightness" Windows uses on an HDR screen, so normal white matches the desktop,
    /// and the screen's refresh rate.
    /// </summary>
    private static (float? SdrWhiteNits, int RefreshRate) ReadTarget(string deviceName)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
            {
                return (null, 60);
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return (null, 60);
            }

            for (var i = 0; i < pathCount; i++)
            {
                var source = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DeviceInfoGetSourceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = paths[i].SourceAdapterId,
                        Id = paths[i].SourceId,
                    },
                    ViewGdiDeviceName = string.Empty,
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0
                    || !string.Equals(source.ViewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var refreshRate = paths[i].RefreshRateDenominator > 0
                    ? (int)Math.Round((double)paths[i].RefreshRateNumerator / paths[i].RefreshRateDenominator)
                    : 60;
                var white = new DisplayConfigSdrWhiteLevel
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DeviceInfoGetSdrWhiteLevel,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSdrWhiteLevel>(),
                        AdapterId = paths[i].TargetAdapterId,
                        Id = paths[i].TargetId,
                    },
                };
                // Reported in thousandths of 80 nits.
                var whiteNits = DisplayConfigGetDeviceInfo(ref white) == 0 && white.SdrWhiteLevel > 0
                    ? white.SdrWhiteLevel / 1000f * 80f
                    : (float?)null;
                return (whiteNits, refreshRate > 0 ? refreshRate : 60);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows: assume the usual brightness.
        }

        return (null, 60);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    // DISPLAYCONFIG_PATH_INFO (72 bytes): only the ids and refresh rate are read, the rest keeps the layout.
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId;
        public uint SourceModeInfoIndex;
        public uint SourceStatusFlags;
        public Luid TargetAdapterId;
        public uint TargetId;
        public uint TargetModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO (64 bytes): not read, only passed along.
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public ulong Data0;
        public ulong Data1;
        public ulong Data2;
        public ulong Data3;
        public ulong Data4;
        public ulong Data5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSdrWhiteLevel
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint SdrWhiteLevel;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSdrWhiteLevel request);
}
