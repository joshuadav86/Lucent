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
  public static void Shot(string path) { var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; using (var bmp = new Bitmap(b.Width, b.Height)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  public static List<IntPtr> Of(uint pid) { var l = new List<IntPtr>(); EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static R Box(IntPtr h) { R v; DwmGetWindowAttribute(h, 9, out v, 16); return v; }
  public static void Click(int x, int y, int wait) { SetCursorPos(x, y); System.Threading.Thread.Sleep(80); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(wait); }
  public static string Track(IntPtr h, int x, int y, int dx, int dy, int steps, int stepMs) {
    var sb = new StringBuilder(); var b0 = Box(h); sb.Append("start " + b0.L + "," + b0.T + "," + b0.Ri + "," + b0.B + " |");
    SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(300);
    for (int i = 1; i <= steps; i++) { SetCursorPos(x + dx * i / steps, y + dy * i / steps); System.Threading.Thread.Sleep(stepMs); var b = Box(h); sb.Append(" " + (dx * i / steps) + ":" + b.L + "," + b.T + "," + b.Ri + "," + b.B); }
    System.Threading.Thread.Sleep(300); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800);
    var e = Box(h); sb.Append(" | end " + e.L + "," + e.T + "," + e.Ri + "," + e.B); return sb.ToString();
  }
  public static void Drag(int x, int y, int dx, int dy) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(300);
    SetCursorPos(x + 3, y + 3); System.Threading.Thread.Sleep(400);
    for (int i = 1; i <= 30; i++) { SetCursorPos(x + 3 + (dx - 3) * i / 30, y + 3 + (dy - 3) * i / 30); System.Threading.Thread.Sleep(60); }
    System.Threading.Thread.Sleep(300); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800);
  }
}
"@

Write-Host "::notice::dpi aware $([W]::SetProcessDpiAwarenessContext([IntPtr]-4))"
$out = Join-Path $env:RUNNER_TEMP 'shots'; New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "::notice::res $([W]::SetRes(1920, 1080))"; Start-Sleep 3
$script:log = ""
function Find($t) { [W]::Of([uint32]$script:proc.Id) | Where-Object { [W]::Title($_) -eq $t } | Select-Object -First 1 }
function TestDrag($tag) {
  $pl = Find 'Lucid'
  if (-not $pl) { $script:log += " || ${tag}: no player [$(Windows)]"; return }
  $p = [W]::Box($pl)
  [void][W]::SetForegroundWindow($pl); Start-Sleep 1
  [W]::Drag($p.L + 60, $p.T + 12, 60, 40)
  $p2 = [W]::Box($pl)
  $script:log += " || ${tag}: player $($p.L),$($p.T) moved $($p2.L - $p.L),$($p2.T - $p.T)"
}
function Windows() { if ($script:proc.HasExited) { return "EXITED code $($script:proc.ExitCode)" }; ([W]::Of([uint32]$script:proc.Id) | ForEach-Object { $b = [W]::Box($_); "'$([W]::Title($_))' $($b.L),$($b.T)" }) -join '; ' }
function Press($k) { $pl = Find 'Lucid'; if (-not $pl) { $script:log += " || key ${k}: no player [$(Windows)]"; return }; [void][W]::SetForegroundWindow($pl); Start-Sleep 1; [W]::Key($k); Start-Sleep 2 }
function Run($label, [scriptblock]$steps) {
  Remove-Item -Recurse -Force (Join-Path $env:APPDATA 'Lucid') -ErrorAction SilentlyContinue
  $script:started = Get-Date
  $script:proc = Start-Process -FilePath (Resolve-Path 'publish/Lucid.exe') -PassThru
  Start-Sleep 35
  try { & $steps } catch { $script:log += " || ERROR $($_.Exception.Message) line $($_.InvocationInfo.ScriptLineNumber)" }
  Stop-Process -Id $script:proc.Id -ErrorAction SilentlyContinue; Start-Sleep 3
  Write-Host "::notice::$label $($script:log)"; $script:log = ""
  $crash = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; StartTime = $script:started } -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($crash) { Write-Host "::warning::CRASH $label :: $(($crash.Message -replace "`r?`n", ' | ').Substring(0, [Math]::Min(3500, $crash.Message.Length)))" }
}
function Bands($order, $wait) {
  $eq = Find 'Equalizer'
  if (-not $eq) { $script:log += " || no equalizer [$(Windows)]"; return }
  $b = [W]::Box($eq); [W]::Shot("$out/eq-$wait.png")
  foreach ($i in $order) {
    if ($script:proc.HasExited) { $script:log += " || EXITED after clicking band button $i (wait $wait)"; return }
    [W]::Click($b.L + 111 + 30 * $i, $b.T + 57, $wait)
  }
  Start-Sleep 2; [W]::Shot("$out/eq-after-$wait.png")
  $script:log += " || wait ${wait}: alive=$(-not $script:proc.HasExited) [$(Windows)]"
}
Write-Host "::notice::animations on $([W]::SystemParametersInfo(0x1043, 0, [IntPtr]1, 3))"
Write-Host "::notice::dpi 100 -> $([W]::SetDpi(100))"; Start-Sleep 4
function Fresh($settings) {
  Remove-Item -Recurse -Force (Join-Path $env:APPDATA 'Lucid') -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force (Join-Path $env:APPDATA 'Lucid') | Out-Null
  $settings | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $env:APPDATA 'Lucid\settings.json')
}
function RunWith($label, $settings, [scriptblock]$steps) {
  Fresh $settings
  $script:started = Get-Date
  $script:proc = Start-Process -FilePath (Resolve-Path 'publish/Lucid.exe') -PassThru
  Start-Sleep 35
  try { & $steps } catch { $script:log += " || ERROR $($_.Exception.Message) line $($_.InvocationInfo.ScriptLineNumber)" }
  Stop-Process -Id $script:proc.Id -ErrorAction SilentlyContinue; Start-Sleep 3
  Write-Host "::notice::$label $($script:log)"; $script:log = ""
}
function Boxes() { ([W]::Of([uint32]$script:proc.Id) | ForEach-Object { $b = [W]::Box($_); "'$([W]::Title($_))' $($b.L),$($b.T),$($b.Ri),$($b.B)" }) -join '; ' }
function Move($title, $dx, $dy, $tag) {
  $h = Find $title; if (-not $h) { $script:log += " || ${tag}: no $title"; return }
  $b = [W]::Box($h); [void][W]::SetForegroundWindow($h); Start-Sleep 1
  $t = [W]::Track($h, $b.L + 150, $b.T + 14, $dx, $dy, 40, 25)
  [W]::Shot("$out/$tag.png")
  Write-Host "::notice::$tag $t"
  $script:log += " || ${tag}: [$(Boxes)]"
}
function Size($title, $dx, $tag) {
  $h = Find $title; if (-not $h) { $script:log += " || ${tag}: no $title"; return }
  $b = [W]::Box($h); [void][W]::SetForegroundWindow($h); Start-Sleep 1
  $t = [W]::Track($h, $b.Ri - 2, [int](($b.T + $b.B) / 2), $dx, 0, 40, 25)
  [W]::Shot("$out/$tag.png")
  Write-Host "::notice::$tag $t"
  $script:log += " || ${tag}: [$(Boxes)]"
}
$lib = @{ ShowLibrary = $true }
foreach ($dpi in 100, 150) {
  Write-Host "::notice::dpi $dpi -> $([W]::SetDpi($dpi))"; Start-Sleep 4
  RunWith "rest $dpi" $lib { [W]::Shot("$out/rest-$dpi.png"); $script:log += " [$(Boxes)]" }
  RunWith "visuals $dpi" $lib {
    Move 'Lucid visuals' 120 0 "v-away-$dpi"
    Move 'Lucid visuals' -114 0 "v-gap6-$dpi"
    Move 'Lucid visuals' 120 0 "v-away2-$dpi"
    Move 'Lucid visuals' -130 0 "v-over10-$dpi"
    Move 'Lucid visuals' 120 0 "v-away3-$dpi"
    Move 'Lucid visuals' -150 0 "v-over30-$dpi"
  }
  RunWith "library $dpi" $lib {
    Move 'Lucid library' -120 0 "l-away-$dpi"
    Move 'Lucid library' 130 0 "l-over10-$dpi"
    Size 'Lucid visuals' -150 "v-size-$dpi"
  }
}
