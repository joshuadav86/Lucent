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
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 35
[W]::Shot("$out/0-first-run.png")
[void]$proc.CloseMainWindow(); Start-Sleep 5
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id }
Add-Content (Join-Path $env:TEMP 'lucid-diag.txt') '--- second launch'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 30
function Dump($label) {
  Write-Host "::notice::=== $label"
  foreach ($h in [W]::Of([uint32]$proc.Id)) { Write-Host ("::notice::{0,-22} {1}" -f [W]::Title($h), [W]::Rects($h)) }
}
function Find($title) { [W]::Of([uint32]$proc.Id) | Where-Object { [W]::Title($_) -eq $title } | Select-Object -First 1 }
function Box($h) { $r = New-Object W+R; [void][W]::GetWindowRect($h, [ref]$r); $r }
Dump 'start'; [W]::Shot("$out/1-start.png")

$player = Find 'Lucid'
$r = Box $player
[void][W]::SetForegroundWindow($player)
[W]::Drag($r.L + 40, $r.T + 14, 80, 60)
Dump 'after dragging the player by 80,60'; [W]::Shot("$out/2-player-dragged.png")

$queue = Find 'Lucid queue'
if ($queue) {
  $q = Box $queue
  [W]::Drag($q.L + 60, $q.T + 18, 0, 150)
  Dump 'after dragging the queue down 150'; [W]::Shot("$out/3-queue-away.png")
  $q2 = Box $queue
  [W]::Drag($q2.L + 60, $q2.T + 18, 0, -140)
  Dump 'after dragging the queue back up 140 (should snap 10px)'; [W]::Shot("$out/4-queue-back.png")
}
Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Out-Null
if ($proc.HasExited) { Write-Host "::notice::Lucid exited with $($proc.ExitCode)" }
Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue

Copy-Item (Join-Path $env:TEMP 'lucid-diag.txt') $out -ErrorAction SilentlyContinue
