$ErrorActionPreference = 'Stop'
trap { Write-Host "::warning::ERROR $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage -replace "`n",' ')"; exit 1 }
Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Drawing;
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
  public static int SetRes(int w, int h) { var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettings(null, -1, ref dm); dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000; return ChangeDisplaySettings(ref dm, 1); }
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  public static void Key(byte k) { keybd_event(k, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(k, 0, 2, IntPtr.Zero); System.Threading.Thread.Sleep(600); }
  public static void Shot(string path) { var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; using (var bmp = new Bitmap(b.Width, b.Height)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
  public static void ShotOf(R r, string path) { int w = r.Ri - r.L, h = r.B - r.T; using (var bmp = new Bitmap(w, h)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, h)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  public static List<IntPtr> Of(uint pid) { var l = new List<IntPtr>(); EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static R Box(IntPtr h) { R v; DwmGetWindowAttribute(h, 9, out v, 16); return v; }
  public static R Rect(IntPtr h) { R v; GetWindowRect(h, out v); return v; }
  public static void Click(int x, int y, int wait) { SetCursorPos(x, y); System.Threading.Thread.Sleep(80); mouse_event(2, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(4, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(wait); }
  public static void Move(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(400); }
}
"@

[void][W]::SetProcessDpiAwarenessContext([IntPtr]-4)
$out = Join-Path $env:RUNNER_TEMP 'shots'; New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "::notice::res $([W]::SetRes(1920, 1080))"; Start-Sleep 3

# A few short songs, so the library and queue have something in them.
$music = Join-Path $env:RUNNER_TEMP 'music'; New-Item -ItemType Directory -Force $music | Out-Null
function Tone($name, $freq) {
  $rate = 44100; $n = $rate * 20; $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ms)
  $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $n * 2)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
  $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$rate); $bw.Write([int]($rate * 2)); $bw.Write([int16]2); $bw.Write([int16]16)
  $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]($n * 2))
  for ($i = 0; $i -lt $n; $i++) { $t = $i / $rate; $env = [Math]::Exp(-(($t * 2) % 1) * 4); $bw.Write([int16](9000 * $env * [Math]::Sin(2 * [Math]::PI * $freq * $t))) }
  $bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $music "$name.wav"), $ms.ToArray())
}
Tone 'Daft Pulse - Around the Grid' 110
Tone 'Analog Hearts - Night Drive' 146.8
Tone 'The Llamas - Whipping Along' 196

function Find($t) { [W]::Of([uint32]$script:proc.Id) | Where-Object { [W]::Title($_) -eq $t } | Select-Object -First 1 }
function Press($k) { $pl = Find 'Lucid'; if ($pl) { [void][W]::SetForegroundWindow($pl); Start-Sleep 1; [W]::Key($k); Start-Sleep 2 } }

function Run($skin, $theme) {
  $dir = Join-Path $env:APPDATA 'Lucid'
  Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $dir | Out-Null
  $settings = @{ Skin = $skin; Theme = $theme; LibraryFolders = @($music) } | ConvertTo-Json
  Set-Content -Path (Join-Path $dir 'settings.json') -Value $settings
  $started = Get-Date
  $script:proc = Start-Process -FilePath (Resolve-Path 'publish/Lucid.exe') -PassThru
  Start-Sleep 30
  $tag = "$skin-$theme" -replace ' ', ''
  try {
    Press 0x44   # D: dock everything to the player
    # Play the library: double-click its first song.
    $lib = Find 'Library'
    if ($lib) { $b = [W]::Box($lib); [W]::Click($b.L + 160, $b.T + 150, 100); [W]::Click($b.L + 160, $b.T + 150, 2500) }
    $pl = Find 'Lucid'
    $p = [W]::Box($pl)
    [W]::Move($p.L + 300, $p.B + 300)
    [W]::Shot("$out/$tag-all.png")
    [W]::ShotOf($p, "$out/$tag-player.png")
    # Options, on the Look tab.
    Press 0x4F
    $popup = [W]::Of([uint32]$script:proc.Id) | Where-Object { [W]::Title($_) -eq '' } | ForEach-Object { @{ H = $_; R = [W]::Rect($_) } } |
      Where-Object { ($_.R.Ri - $_.R.L) -gt 300 -and ($_.R.Ri - $_.R.L) -lt 420 } | Select-Object -First 1
    if ($popup) {
      $r = $popup.R
      [W]::Click($r.L + 16 + 14 + 260, $r.T + 16 + 14 + 15, 1500)
      $r = [W]::Rect($popup.H)
      [W]::ShotOf($r, "$out/$tag-look.png")
      Write-Host "::notice::$tag popup $($r.L),$($r.T) $($r.Ri - $r.L)x$($r.B - $r.T)"
    } else { Write-Host "::warning::$tag no options popup: $(([W]::Of([uint32]$script:proc.Id) | ForEach-Object { "'$([W]::Title($_))'" }) -join ' ')" }
  } catch { Write-Host "::warning::$tag ERROR $($_.Exception.Message) line $($_.InvocationInfo.ScriptLineNumber)" }
  if ($script:proc.HasExited) { Write-Host "::warning::$tag EXITED code $($script:proc.ExitCode)" }
  Stop-Process -Id $script:proc.Id -ErrorAction SilentlyContinue; Start-Sleep 3
  $crash = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; StartTime = $started } -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($crash) { Write-Host "::warning::CRASH $tag :: $(($crash.Message -replace "`r?`n", ' | ').Substring(0, [Math]::Min(3500, $crash.Message.Length)))" }
}



Run 'Glass' 'Deep Ocean'
Run 'Neon' 'Ultraviolet'

Run 'Glass' 'Glacier'

