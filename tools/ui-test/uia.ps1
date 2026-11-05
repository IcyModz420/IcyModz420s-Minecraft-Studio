param([int]$ProcessId, [string]$Window, [string[]]$Do = @(), [switch]$List)
# Drives a window of a process through UI Automation (no mouse or keyboard):
#   set:Name=value   toggle:Name   invoke:Name   select:Name (tiles/radio)   combo:Name=item text   read:Name
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
if (-not ("UiaMouse" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UiaMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(60); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
}
"@
}
[UiaMouse]::SetProcessDPIAware() | Out-Null
$Scope = [System.Windows.Automation.TreeScope]

function Find-Window {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
  for ($i = 0; $i -lt 60; $i++) {
    foreach ($w in $AE::RootElement.FindAll($Scope::Children, $cond)) {
      if ($w.Current.Name -like "*$Window*") { return $w }
      # Owned dialogs can also appear under their owner.
      foreach ($c in $w.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($c.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $c.Current.Name -like "*$Window*") { return $c }
      }
    }
    Start-Sleep -Milliseconds 250
  }
  return $null
}

function Find-Element($win, [string]$name, $type = $null) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  if ($type) { $cond = New-Object System.Windows.Automation.AndCondition($cond, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type))) }
  for ($i = 0; $i -lt 20; $i++) {
    $e = $win.FindFirst($Scope::Descendants, $cond)
    if ($e) { return $e }
    Start-Sleep -Milliseconds 200
  }
  throw "element not found: $name"
}

function Try-Pattern($e, $pattern) {
  $p = $null
  if ($e.TryGetCurrentPattern($pattern, [ref]$p)) { return $p }
  return $null
}

function Need-Pattern($e, $pattern) {
  $p = Try-Pattern $e $pattern
  if (-not $p) { throw "'$($e.Current.Name)' ($($e.Current.ControlType.ProgrammaticName)) does not support $($pattern.ProgrammaticName)" }
  return $p
}

$win = Find-Window
if (-not $win) { "window not found: $Window"; exit 1 }
if ($List) {
  foreach ($e in $win.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    "{0,-14} {1}" -f $e.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""), $e.Current.Name
  }
}
foreach ($step in $Do) {
  $verb, $rest = $step.Split(":", 2)
  $name, $value = $rest.Split("=", 2)
  $type = switch ($verb) { "set" { [System.Windows.Automation.ControlType]::Edit } "toggle" { [System.Windows.Automation.ControlType]::CheckBox } "invoke" { [System.Windows.Automation.ControlType]::Button } default { $null } }
  $e = Find-Element $win $name $type
  switch ($verb) {
    "set" {
      $vp = Need-Pattern $e ([System.Windows.Automation.ValuePattern]::Pattern)
      $vp.SetValue($value)
      if ($vp.Current.Value -ne $value) { throw "'$name' is '$($vp.Current.Value)' after setting it" }
    }
    "toggle" { (Need-Pattern $e ([System.Windows.Automation.TogglePattern]::Pattern)).Toggle() }
    "invoke" { (Need-Pattern $e ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    "click" {
      $r = $e.Current.BoundingRectangle
      [UiaMouse]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    }
    "select" {
      $inv = Try-Pattern $e ([System.Windows.Automation.InvokePattern]::Pattern)
      if ($inv) { $inv.Invoke() } else { (Try-Pattern $e ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
    }
    "combo" {
      $ec = Try-Pattern $e ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
      if ($ec) { $ec.Expand(); Start-Sleep -Milliseconds 300 }
      $item = Find-Element $e $value
      (Try-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
      if ($ec) { $ec.Collapse() }
    }
    "read" {
      $vp = Try-Pattern $e ([System.Windows.Automation.ValuePattern]::Pattern)
      if ($vp) { "$name = $($vp.Current.Value)" } else { "$name = $($e.Current.Name)" }
    }
  }
  Start-Sleep -Milliseconds 250
  "done $step"
}
