$ErrorActionPreference = 'Stop'
trap { Write-Host "::warning::ERROR $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage -replace "`n",' ')"; exit 1 }
Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Drawing;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
  [StructLayout(LayoutKind.Sequential)] public struct PathSource { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PathTarget { public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation; public int scaling; public uint rrN; public uint rrD; public int scanLineOrdering; public int targetAvailable; public uint statusFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct PathInfo { public PathSource sourceInfo; public PathTarget targetInfo; public uint flags; }
  [StructLayout(LayoutKind.Sequential)] public struct Header { public int type; public int size; public LUID adapterId; public uint id; }
  [StructLayout(LayoutKind.Sequential)] public struct GetScale { public Header header; public int min; public int cur; public int max; }
  [StructLayout(LayoutKind.Sequential)] public struct SetScale { public Header header; public int rel; }
  [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint f, out uint p, out uint m);
  [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint f, ref uint np, [Out] PathInfo[] p, ref uint nm, [Out] byte[] m, IntPtr t);
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref GetScale g);
  [DllImport("user32.dll")] public static extern int DisplayConfigSetDeviceInfo(ref SetScale s);
  public static string SetDpi(int percent) {
    uint np, nm; GetDisplayConfigBufferSizes(2, out np, out nm); var paths = new PathInfo[np]; var modes = new byte[nm * 64];
    int q = QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
    var g = new GetScale(); g.header.type = -3; g.header.size = Marshal.SizeOf(typeof(GetScale)); g.header.adapterId = paths[0].sourceInfo.adapterId; g.header.id = paths[0].sourceInfo.id;
    int r1 = DisplayConfigGetDeviceInfo(ref g);
    int[] steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    int want = Array.IndexOf(steps, percent), rec = -g.min;
    var s = new SetScale(); s.header.type = -4; s.header.size = Marshal.SizeOf(typeof(SetScale)); s.header.adapterId = g.header.adapterId; s.header.id = g.header.id; s.rel = want - rec;
    int r2 = DisplayConfigSetDeviceInfo(ref s);
    return "query=" + q + " get=" + r1 + " min=" + g.min + " cur=" + g.cur + " max=" + g.max + " set=" + r2;
  }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
    public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName;
    public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettings(string d, int m, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int ChangeDisplaySettings(ref DEVMODE dm, int f);
  public static int SetRes(int w, int h) { var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettings(null, -1, ref dm); dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000; return ChangeDisplaySettings(ref dm, 1); }
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  public static void Key(byte k) { keybd_event(k, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(k, 0, 2, IntPtr.Zero); System.Threading.Thread.Sleep(600); }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  public static List<IntPtr> Of(uint pid) { var l = new List<IntPtr>(); EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static R Box(IntPtr h) { R v; DwmGetWindowAttribute(h, 9, out v, 16); return v; }
  public static void Drag(int x, int y, int dx, int dy) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(300);
    SetCursorPos(x + 3, y + 3); System.Threading.Thread.Sleep(400);
    for (int i = 1; i <= 30; i++) { SetCursorPos(x + 3 + (dx - 3) * i / 30, y + 3 + (dy - 3) * i / 30); System.Threading.Thread.Sleep(60); }
    System.Threading.Thread.Sleep(300); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800);
  }
}
"@
Write-Host "::notice::res $([W]::SetRes(2560, 1600)) / $([W]::SetRes(1920, 1080))"; Start-Sleep 3
function Rects($tag) {
  $all = [W]::Of([uint32]$script:proc.Id)
  $script:player = $all | Where-Object { [W]::Title($_) -eq 'Lucid' } | Select-Object -First 1
  $script:lib = $all | Where-Object { [W]::Title($_) -eq 'Lucid library' } | Select-Object -First 1
}
function TestDrag($tag) {
  Rects
  $p = [W]::Box($script:player); $l = [W]::Box($script:lib)
  $line = "$tag windowDpi=$([W]::GetDpiForWindow($script:player)) player=$($p.L),$($p.T),$($p.Ri),$($p.B) library=$($l.L),$($l.T),$($l.Ri),$($l.B) gapX=$($p.L - $l.Ri)"
  [void][W]::SetForegroundWindow($script:player); Start-Sleep 1
  [W]::Drag($p.L + 60, $p.T + 12, 60, 40)
  $p2 = [W]::Box($script:player); $l2 = [W]::Box($script:lib)
  $script:log += " || $line | player moved $($p2.L - $p.L),$($p2.T - $p.T) library moved $($l2.L - $l.L),$($l2.T - $l.T)"
}
foreach ($pct in 150, 175, 200) {
  Write-Host "::notice::dpi $pct -> $([W]::SetDpi($pct))"; Start-Sleep 4
  foreach ($tol in 2, 6) {
    $env:LUCID_TOLERANCE = "$tol"; $script:log = ""
    Remove-Item -Recurse -Force (Join-Path $env:APPDATA 'Lucid') -ErrorAction SilentlyContinue
    $script:proc = Start-Process -FilePath (Resolve-Path 'publish/Lucid.exe') -PassThru
    Start-Sleep 35
    TestDrag "dpi=$pct tol=$tol fresh"
    Rects; [void][W]::SetForegroundWindow($script:player); [W]::Key(0x4C); Start-Sleep 1; [W]::Key(0x4C); Start-Sleep 2
    TestDrag "dpi=$pct tol=$tol L-toggled"
    [void][W]::SetForegroundWindow($script:player); [W]::Key(0x44); Start-Sleep 2
    TestDrag "dpi=$pct tol=$tol D-docked"
    [void]$script:proc.CloseMainWindow(); Start-Sleep 5
    $script:proc = Start-Process -FilePath (Resolve-Path 'publish/Lucid.exe') -PassThru
    Start-Sleep 30
    TestDrag "dpi=$pct tol=$tol relaunched"
    Stop-Process -Id $script:proc.Id -ErrorAction SilentlyContinue; Start-Sleep 3
    Write-Host "::notice::$($script:log)"
  }
}
