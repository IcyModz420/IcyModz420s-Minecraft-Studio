param([int]$ProcessId, [string]$Title = "", [string]$From, [string]$To, [string]$Path = "", [int]$Steps = 12, [string]$Modifier = "")
# Drags the left mouse button inside a window (window-relative pixels): From "x,y" to To "x,y", or along Path "x,y;x,y;…".
if (-not ("DragShot" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class DragShot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
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
[DragShot]::SetProcessDPIAware() | Out-Null
$h = [DragShot]::Find($ProcessId, $Title)
if ($h -eq [IntPtr]::Zero) { "window not found"; exit 1 }
[DragShot]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 200
$r = New-Object DragShot+RECT
[DragShot]::GetWindowRect($h, [ref]$r) | Out-Null
$points = @()
if ($Path -ne "") { foreach ($p in $Path.Split(";")) { $xy = $p.Split(","); $points += ,@([int]$xy[0], [int]$xy[1]) } }
else {
  $a = $From.Split(","); $b = $To.Split(",")
  for ($i = 0; $i -le $Steps; $i++) { $points += ,@([int]([int]$a[0] + ([int]$b[0] - [int]$a[0]) * $i / $Steps), [int]([int]$a[1] + ([int]$b[1] - [int]$a[1]) * $i / $Steps)) }
}
$vk = switch ($Modifier) { "alt" { 0x12 } "shift" { 0x10 } "ctrl" { 0x11 } default { 0 } }
if ($vk -ne 0) { [DragShot]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero) }
[DragShot]::SetCursorPos($r.L + $points[0][0], $r.T + $points[0][1]) | Out-Null
Start-Sleep -Milliseconds 80
[DragShot]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero)
foreach ($p in $points) { [DragShot]::SetCursorPos($r.L + $p[0], $r.T + $p[1]) | Out-Null; Start-Sleep -Milliseconds 25 }
Start-Sleep -Milliseconds 60
[DragShot]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
if ($vk -ne 0) { [DragShot]::keybd_event([byte]$vk, 0, 2, [IntPtr]::Zero) }
"dragged $($points.Count) points"
