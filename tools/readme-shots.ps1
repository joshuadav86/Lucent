$ErrorActionPreference = 'Stop'
trap { Write-Host "::warning::ERROR $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage -replace "`n",' ')"; exit 1 }
Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @"
using System; using System.IO; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Drawing;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
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
  public static string Modes() { var s = new StringBuilder(); var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++) s.Append(dm.dmPelsWidth + "x" + dm.dmPelsHeight + " "); return s.ToString(); }
  public static int SetRes(int w, int h) { var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettings(null, -1, ref dm); dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000; return ChangeDisplaySettings(ref dm, 1); }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  public static void Key(byte k) { keybd_event(k, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(k, 0, 2, IntPtr.Zero); System.Threading.Thread.Sleep(300); }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800); }
  public static void DoubleClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(300); for (int i = 0; i < 2; i++) { mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); } System.Threading.Thread.Sleep(800); }
  public static void RightClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(8, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(16, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(1200); }
  public static void Drag(int x, int y, int dx, int dy) { SetCursorPos(x, y); System.Threading.Thread.Sleep(300); mouse_event(2, 0, 0, 0, IntPtr.Zero); for (int i = 1; i <= 20; i++) { SetCursorPos(x + dx * i / 20, y + dy * i / 20); System.Threading.Thread.Sleep(40); } mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(600); }
  public static List<IntPtr> Of(uint pid) { var l = new List<IntPtr>(); EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static R Box(IntPtr h) { R v; DwmGetWindowAttribute(h, 9, out v, 16); return v; }
  public static void Shot(string path) { var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; using (var bmp = new Bitmap(b.Width, b.Height)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
  public static void ShotRect(string path, int l, int t, int r, int b) { using (var bmp = new Bitmap(r - l, b - t)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(new Point(l, t), Point.Empty, new Size(r - l, b - t)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
  // A short synth loop, so the meters, visuals and editor have something to show.
  public static void Song(string path, double seconds, double bpm, double root, int seed) {
    int rate = 44100, n = (int)(seconds * rate); var rnd = new Random(seed);
    var ms = new MemoryStream(); var w = new BinaryWriter(ms);
    double beat = 60.0 / bpm; double[] chord = { 0, 3, 7, 10 }; int[] prog = { 0, 5, 3, 7 };
    for (int i = 0; i < n; i++) {
      double t = (double)i / rate, bt = t % beat; int bar = (int)(t / (beat * 4)) % 4; double r0 = root * Math.Pow(2, prog[bar] / 12.0);
      double kick = Math.Sin(2 * Math.PI * (50 + 120 * Math.Exp(-bt * 30)) * bt) * Math.Exp(-bt * 7);
      double hb = (t + beat / 2) % beat; double hat = (rnd.NextDouble() * 2 - 1) * Math.Exp(-hb * 60) * 0.25;
      double bass = Math.Sin(2 * Math.PI * r0 / 2 * t) * 0.35 * (0.6 + 0.4 * Math.Exp(-bt * 4));
      double pad = 0; foreach (var c in chord) pad += Math.Sin(2 * Math.PI * r0 * 2 * Math.Pow(2, c / 12.0) * t + Math.Sin(t * 3 + c)) * 0.07;
      double step = beat / 4, at = t % step; int k = (int)(t / step) % 8; double arp = Math.Sin(2 * Math.PI * r0 * 4 * Math.Pow(2, chord[k % 4] / 12.0) * t) * Math.Exp(-at * 18) * 0.18;
      double fade = Math.Min(1, Math.Min(t / 1.5, (seconds - t) / 2));
      double l = (kick * 0.8 + hat + bass + pad + arp * 1.2) * fade * 0.6, rr = (kick * 0.8 + hat * 0.7 + bass + pad + arp * 0.6) * fade * 0.6;
      w.Write((short)Math.Max(-32767, Math.Min(32767, l * 32767))); w.Write((short)Math.Max(-32767, Math.Min(32767, rr * 32767)));
    }
    var data = ms.ToArray(); using (var f = new BinaryWriter(File.Create(path))) {
      f.Write(Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + data.Length); f.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(rate); f.Write(rate * 4); f.Write((short)4); f.Write((short)16);
      f.Write(Encoding.ASCII.GetBytes("data")); f.Write(data.Length); f.Write(data); }
  }
}
"@

$out = Join-Path $env:RUNNER_TEMP 'shots'; New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "::notice::modes $([W]::Modes())"
Write-Host "::notice::set 1920x1080 -> $([W]::SetRes(1920, 1080))"
Start-Sleep 3
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host "::notice::screen $($b.Width)x$($b.Height)"
try { Start-Service audiosrv -ErrorAction Stop; Write-Host '::notice::audio service started' } catch { Write-Host "::notice::audio service: $($_.Exception.Message)" }

# Demo music in a library folder, and settings that point the library at it.
$music = 'C:\LucidDemo\Music'; New-Item -ItemType Directory -Force $music | Out-Null
$songs = @(@('Midnight Drive', 118, 55, 1), @('Neon Skyline', 124, 49, 2), @('Aurora', 110, 58.27, 3), @('Night Swim', 100, 46.25, 4), @('Glass City', 128, 51.91, 5), @('Afterglow', 96, 43.65, 6))
foreach ($s in $songs) { [W]::Song("$music\$($s[0]).wav", 75, $s[1], $s[2], $s[3]); Start-Sleep -Milliseconds 300 }
$dir = Join-Path $env:APPDATA 'Lucid'; New-Item -ItemType Directory -Force $dir | Out-Null
@{ LibraryFolders = @($music); LastVisual = 'Cosmic Jellyfish'; AutoCycle = $false; Equalizer = @{ Enabled = $true; BandCount = 10; Gains = @(5, 4, 2.5, 0, -1.5, -1, 1, 3, 4, 4.5); PreampDb = -2; PresetName = $null } } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dir 'settings.json')

$exe = Resolve-Path 'publish/Lucid.exe'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 40
function Dump($label) { Write-Host "::notice::=== $label"; foreach ($h in [W]::Of([uint32]$proc.Id)) { $r = [W]::Box($h); Write-Host ("::notice::{0} {1},{2}-{3},{4}" -f [W]::Title($h), $r.L, $r.T, $r.Ri, $r.B) } }
function Find($title) { [W]::Of([uint32]$proc.Id) | Where-Object { [W]::Title($_) -eq $title } | Select-Object -First 1 }
function Front { [void][W]::SetForegroundWindow((Find 'Lucid')); Start-Sleep 1 }
function GroupBox { $l = 99999; $t = 99999; $r = 0; $b = 0; foreach ($n in 'Lucid', 'Lucid library', 'Lucid visuals', 'Lucid queue', 'Equalizer') { $h = Find $n; if ($h) { $x = [W]::Box($h); $l = [Math]::Min($l, $x.L); $t = [Math]::Min($t, $x.T); $r = [Math]::Max($r, $x.Ri); $b = [Math]::Max($b, $x.B) } }; return @($l, $t, $r, $b) }

# Select the second song in the queue, so the layout looks in use.
$q = Find 'Lucid queue'
if ($q) { $r = [W]::Box($q); [W]::Click($r.L + 120, $r.T + 126) }
Start-Sleep 4
$g = GroupBox; [W]::ShotRect("$out/01-layout.png", $g[0], $g[1], $g[2], $g[3])

$vis = Find 'Lucid visuals'
foreach ($name in @('Cosmic Jellyfish', 'Mandelbulb Bloom', 'Neon Downpour', 'Galaxy Collision', 'Kaleido Cathedral', 'Menger Voyage', 'Strange Attractors', 'Sunset Highway', 'Black Hole', 'Rose Window', 'Flower of Life', 'Hyperspace Rings')) {
  Front; [W]::Key(0x56); Start-Sleep 1
  [void][W]::SetForegroundWindow($vis); Start-Sleep 1
  [System.Windows.Forms.SendKeys]::SendWait($name); Start-Sleep 1
  if ($name -eq 'Kaleido Cathedral') { $g = GroupBox; [W]::ShotRect("$out/02-picker.png", $g[0], $g[1], $g[2], $g[3]) }
  [W]::Key(0x0D); Start-Sleep 1
  [W]::Key(0x1B); [W]::Key(0x1B); Start-Sleep 5
  Front; [W]::Key(0x7A); Start-Sleep 6
  [W]::ShotRect("$out/10-fs-$($name -replace '[^A-Za-z]','').png", 43, 0, 1877, 1032)
  if ($name -eq 'Neon Downpour') { [W]::RightClick(1200, 300); [W]::ShotRect("$out/03-fs-menu.png", 43, 0, 1877, 1032); [W]::Key(0x1B) }
  Front; [W]::Key(0x7A); Start-Sleep 2
}

# The audio editor: right-click a song in the library and choose Open in editor.
$lib = Find 'Lucid library'
if ($lib) {
  $r = [W]::Box($lib)
  [W]::Click($r.L + 80, $r.T + 196); [W]::RightClick($r.L + 80, $r.T + 196)
  1..5 | ForEach-Object { [W]::Key(0x28) }
  [W]::Key(0x0D); Start-Sleep 8
  Dump 'editor'
  $ed = [W]::Of([uint32]$proc.Id) | Where-Object { [W]::Title($_) -notin 'Lucid', 'Lucid library', 'Lucid visuals', 'Lucid queue', 'Equalizer', '' } | Select-Object -First 1
  if ($ed) {
    [void][W]::SetForegroundWindow($ed); Start-Sleep 1
    $e = [W]::Box($ed); [W]::Drag($e.L + 300, ($e.T + $e.B) / 2, 500, 0); Start-Sleep 1
    $e = [W]::Box($ed); [W]::ShotRect("$out/04-editor.png", $e.L, $e.T, $e.Ri, $e.B)
  } else { [W]::Shot("$out/04-editor-missing.png") }
}
Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
