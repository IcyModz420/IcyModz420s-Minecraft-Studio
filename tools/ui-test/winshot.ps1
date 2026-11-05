param([int]$ProcessId, [string]$Title = "", [string]$Out = "", [string]$Keys = "", [int]$DelayMs = 600, [string]$Click = "", [switch]$Double)
# Screenshot (and optionally drive) one top-level window of a process, found by title substring.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
if (-not ("WinShot" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class WinShot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  public static IntPtr Find(int pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (sb.Length > 0 && (title.Length == 0 || sb.ToString().Contains(title))) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
}
[WinShot]::SetProcessDPIAware() | Out-Null
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { $h = [WinShot]::Find($ProcessId, $Title); if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 } }
if ($h -eq [IntPtr]::Zero) { "window not found: $Title"; exit 1 }
# Only bring the window forward when it is about to be clicked or typed into; capturing works without it, so the
# person using this PC keeps their focus.
if ($Click -ne "" -or $Keys -ne "") { [WinShot]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 250 }
$r = New-Object WinShot+RECT
[WinShot]::GetWindowRect($h, [ref]$r) | Out-Null
if ($Click -ne "") {
  # Click at window-relative pixel coordinates "x,y".
  $xy = $Click.Split(","); [WinShot]::SetCursorPos($r.L + [int]$xy[0], $r.T + [int]$xy[1]) | Out-Null
  Start-Sleep -Milliseconds 100
  [WinShot]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [WinShot]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
  if ($Double) { Start-Sleep -Milliseconds 60; [WinShot]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [WinShot]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds $DelayMs
}
if ($Keys -ne "") { [System.Windows.Forms.SendKeys]::SendWait($Keys); Start-Sleep -Milliseconds $DelayMs }
if ($Out -eq "") { "ok"; exit 0 }
# The keys may have opened another window; capture the one asked for again.
$h2 = [WinShot]::Find($ProcessId, $Title); if ($h2 -ne [IntPtr]::Zero) { $h = $h2 }
[WinShot]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[WinShot]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ($w x $hgt)"
