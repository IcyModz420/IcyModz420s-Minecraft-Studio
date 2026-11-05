param([int]$ProcessId, [string]$Window, [string]$Control = "", [string]$From = "", [string]$To = "", [int]$Steps = 8, [string]$Key = "", [switch]$Where, [switch]$Close)
# tools/ui-test/post.ps1: drives one window of a process by posting it window messages — never the mouse, the keyboard or
# the focus, so it is safe while someone uses the PC. The window is found by title (a part of it):
#   -Control Name -From "x,y" [-To "x,y"]   a left press at From, moved to To in a few steps and let go there (no To: a
#                                          click); x, y in the control's own pixels. The control is found by its name
#                                          (accessible name, e.g. the map pickers' "Map").
#   -Control Name -Where                   where the control is inside the window: subtract it from winshot.ps1 picture pixels.
#   -Control Name -Key Space               a key pressed on the control (Space, Enter, Escape, Tab, arrows, Delete, a letter
#                                          or digit), posted so the script returns even when it opens a modal dialog
#                                          (a start/Overview tile is pressed with Space: its accessibility action is not
#                                          offered to UI Automation).
#   -Close                                 closes the window (as its title bar's X would), without looking for a button.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ("PostMsg" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PostMsg {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static IntPtr At(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
}
"@
}
[PostMsg]::SetProcessDPIAware() | Out-Null
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

# The window: a top-level one of the process, or a dialog owned by one, whose title contains $Window.
$byProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
$win = $null
for ($i = 0; $i -lt 40 -and -not $win; $i++) {
  foreach ($w in $AE::RootElement.FindAll($Scope::Children, $byProcess)) {
    if ($w.Current.Name -like "*$Window*") { $win = $w; break }
    foreach ($c in $w.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($c.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $c.Current.Name -like "*$Window*") { $win = $c; break }
    }
    if ($win) { break }
  }
  if (-not $win) { Start-Sleep -Milliseconds 250 }
}
if (-not $win) { throw "window not found: $Window" }
$wh = [IntPtr]$win.Current.NativeWindowHandle
if ($Close) { [void][PostMsg]::PostMessage($wh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); "closed '$($win.Current.Name)'"; return }

$e = $win.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Control)))
if (-not $e) { throw "control not found: $Control" }
$h = [IntPtr]$e.Current.NativeWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "'$Control' has no window of its own to post to" }
$wr = New-Object PostMsg+RECT
$cr = New-Object PostMsg+RECT
[void][PostMsg]::GetWindowRect($wh, [ref]$wr)
[void][PostMsg]::GetWindowRect($h, [ref]$cr)
if ($Where) { "'$Control' is at $($cr.L - $wr.L), $($cr.T - $wr.T) in the window, $($cr.R - $cr.L) x $($cr.B - $cr.T)"; return }
if ($Key -ne "") {
  $named = @{ "Space" = 0x20; "Enter" = 0x0D; "Escape" = 0x1B; "Tab" = 0x09; "Left" = 0x25; "Up" = 0x26; "Right" = 0x27; "Down" = 0x28; "Delete" = 0x2E }
  if ($named.ContainsKey($Key)) { $vk = $named[$Key] }
  elseif ($Key.Length -eq 1 -and [char]::IsLetterOrDigit($Key[0])) { $vk = [int][char]::ToUpperInvariant($Key[0]) }
  else { throw "key: Space, Enter, Escape, Tab, Left, Up, Right, Down, Delete, or one letter or digit" }
  [void][PostMsg]::PostMessage($h, 0x0100, [IntPtr]$vk, [IntPtr]1)            # WM_KEYDOWN
  [void][PostMsg]::PostMessage($h, 0x0101, [IntPtr]$vk, [IntPtr]0xC0000001)   # WM_KEYUP
  "pressed $Key on '$Control'"; return
}

$fx, $fy = $From.Split(",") | ForEach-Object { [int]$_.Trim() }
if ($To -ne "") { $tx, $ty = $To.Split(",") | ForEach-Object { [int]$_.Trim() } } else { $tx, $ty = $fx, $fy; $Steps = 0 }
[void][PostMsg]::SendMessage($h, 0x0200, [IntPtr]::Zero, [PostMsg]::At($fx, $fy))   # WM_MOUSEMOVE: the pointer arrives
[void][PostMsg]::SendMessage($h, 0x0201, [IntPtr]1, [PostMsg]::At($fx, $fy))        # WM_LBUTTONDOWN
for ($s = 1; $s -le $Steps; $s++) {
  $x = $fx + [int](($tx - $fx) * $s / $Steps); $y = $fy + [int](($ty - $fy) * $s / $Steps)
  [void][PostMsg]::SendMessage($h, 0x0200, [IntPtr]1, [PostMsg]::At($x, $y))        # WM_MOUSEMOVE, the button held
  Start-Sleep -Milliseconds 15
}
[void][PostMsg]::SendMessage($h, 0x0202, [IntPtr]::Zero, [PostMsg]::At($tx, $ty))   # WM_LBUTTONUP
"pressed '$Control' at $fx,$fy" + $(if ($Steps -gt 0) { ", let go at $tx,$ty" } else { "" })
