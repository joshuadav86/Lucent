$ErrorActionPreference = 'Stop'
trap { Write-Host "::warning::ERROR $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage -replace "`n",' ')"; exit 1 }
Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Drawing;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; public override string ToString() { return L+","+T+" - "+Ri+","+B+" ("+(Ri-L)+"x"+(B-T)+")"; } }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  public static void Key(byte k) { keybd_event(k, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(k, 0, 2, IntPtr.Zero); System.Threading.Thread.Sleep(250); }
  public static void RightClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(8, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(16, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(1200); }
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  public static List<IntPtr> Of(uint pid) { var l = new List<IntPtr>(); EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static string Rects(IntPtr h) { R o, v; GetWindowRect(h, out o); DwmGetWindowAttribute(h, 9, out v, 16); return "window " + o + " | visible " + v + " | dpi " + GetDpiForWindow(h); }
  public static void Drag(int x, int y, int dx, int dy) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(300);
    SetCursorPos(x + 3, y + 3); System.Threading.Thread.Sleep(400);
    for (int i = 1; i <= 30; i++) { SetCursorPos(x + 3 + (dx - 3) * i / 30, y + 3 + (dy - 3) * i / 30); System.Threading.Thread.Sleep(60); }
    System.Threading.Thread.Sleep(300); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800);
  }
  public static void Shot(string path) { var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; using (var bmp = new Bitmap(b.Width, b.Height)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size); bmp.Save(path); } }
}
"@

Write-Host '::notice::types loaded'
$out = Join-Path $env:RUNNER_TEMP 'shots'; New-Item -ItemType Directory -Force $out | Out-Null
$exe = Resolve-Path 'publish/Lucid.exe'
function Dump($label) {
  Write-Host "::notice::=== $label"
  foreach ($h in [W]::Of([uint32]$proc.Id)) { Write-Host ("::notice::{0,-22} {1}" -f [W]::Title($h), [W]::Rects($h)) }
}
function Find($title) { [W]::Of([uint32]$proc.Id) | Where-Object { [W]::Title($_) -eq $title } | Select-Object -First 1 }
function Press($key, $label, $shot) {
  [void][W]::SetForegroundWindow((Find 'Lucid')); Start-Sleep 1
  [W]::Key($key); Start-Sleep 3
  Dump $label; [W]::Shot("$out/$shot.png")
}
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 35
Dump 'first run'; [W]::Shot("$out/0-first-run.png")
[void]$proc.CloseMainWindow(); Start-Sleep 5
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id }
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 30
Dump 'second launch'; [W]::Shot("$out/1-second-launch.png")
Press 0x51 'queue off (Q)' '2-queue-off'
Press 0x4C 'library off (L)' '3-library-off'
Press 0x45 'equalizer off (E)' '4-eq-off'
Press 0x45 'equalizer on (E)' '5-eq-on'
Press 0x51 'queue on (Q)' '6-queue-on'
Press 0x4C 'library on (L)' '7-library-on'
[void]$proc.CloseMainWindow(); Start-Sleep 5
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 30
Dump 'third launch'; [W]::Shot("$out/8-third-launch.png")
Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
