# The Windows layout check (src/Lucent.Windows/Diagnostics/LayoutCheck.cs): runs Lucent on fresh settings
# at a 1920 x 1080 screen, at 100% and then 150% scaling, and fails unless every check passed.
# Usage (on a Windows runner, from the repository): tools/windows/layout-check.ps1 publish/Lucent.exe
param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class Screen {
  [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
  [StructLayout(LayoutKind.Sequential)] public struct PathSource { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PathTarget { public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation; public int scaling; public uint rrN; public uint rrD; public int scanLineOrdering; public int targetAvailable; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PathInfo { public PathSource sourceInfo; public PathTarget targetInfo; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct Header { public int type; public int size; public LUID adapterId; public uint id; }
  [StructLayout(LayoutKind.Sequential)] public struct GetScale { public Header header; public int min; public int cur; public int max; }
  [StructLayout(LayoutKind.Sequential)] public struct SetScale { public Header header; public int rel; }
  [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint f, out uint p, out uint m);
  [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint f, ref uint np, [Out] PathInfo[] p, ref uint nm, [Out] byte[] m, IntPtr t);
  [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref GetScale g);
  [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SetScale s);
  public static string SetScaling(int percent) {
    uint np, nm; GetDisplayConfigBufferSizes(2, out np, out nm); var paths = new PathInfo[np]; var modes = new byte[nm * 64];
    QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
    var g = new GetScale(); g.header.type = -3; g.header.size = Marshal.SizeOf(typeof(GetScale)); g.header.adapterId = paths[0].sourceInfo.adapterId; g.header.id = paths[0].sourceInfo.id;
    DisplayConfigGetDeviceInfo(ref g);
    int[] steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    var s = new SetScale(); s.header.type = -4; s.header.size = Marshal.SizeOf(typeof(SetScale)); s.header.adapterId = g.header.adapterId; s.header.id = g.header.id;
    s.rel = Array.IndexOf(steps, percent) + g.min;
    return "set " + DisplayConfigSetDeviceInfo(ref s);
  }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
    public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName;
    public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string d, int m, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int f);
  public static int SetSize(int w, int h) { var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettings(null, -1, ref dm); dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000; return ChangeDisplaySettings(ref dm, 1); }
}
"@

Write-Host "screen 1920 x 1080: $([Screen]::SetSize(1920, 1080))"
$failed = $false
foreach ($percent in 100, 150) {
  Write-Host "scaling ${percent}%: $([Screen]::SetScaling($percent))"
  Start-Sleep 4
  $data = Join-Path $env:RUNNER_TEMP "layout-check-data-$percent"
  $result = Join-Path $env:RUNNER_TEMP "layout-check-$percent.txt"
  Remove-Item -Recurse -Force $data, $result -ErrorAction SilentlyContinue
  $env:LUCENT_DATA_FOLDER = $data
  $env:LUCENT_LAYOUT_CHECK = $result
  $lucent = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
  if (-not $lucent.WaitForExit(150000)) {
    Stop-Process -Id $lucent.Id -Force -ErrorAction SilentlyContinue
    Write-Host "::error::Lucent was still running after 150 seconds (${percent}% scaling)."
  }

  Write-Host "---- ${percent}% scaling ----"
  if (Test-Path $result) { Get-Content $result | Write-Host } else { Write-Host "(the layout check wrote nothing)" }
  if (-not ((Test-Path $result) -and (Select-String -Path $result -Pattern '^PASSED$' -Quiet))) {
    Write-Host "::error::The Windows layout check failed at ${percent}% scaling: see the MISMATCH lines above."
    $failed = $true
  }
}

[void][Screen]::SetScaling(100)
Remove-Item Env:LUCENT_DATA_FOLDER, Env:LUCENT_LAYOUT_CHECK -ErrorAction SilentlyContinue
if ($failed) { exit 1 }
