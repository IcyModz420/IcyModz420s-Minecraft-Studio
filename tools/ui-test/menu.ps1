param([int]$ProcessId, [string[]]$Do = @(), [string]$ShotDir = "")
# Drives a process's windows through UI Automation patterns only (never the mouse or keyboard):
#   open:Menu Button        expands a toolbar drop-down (ExpandCollapse or Invoke)
#   invoke:Name             invokes a button or menu item found in any window of the process
#   set:Window|Field=value  sets an edit box (by name) inside the window whose title contains Window
#   wait:Window             waits for a window whose title contains Window
#   shot:Window|file.png    saves a picture of that window (PrintWindow, no focus change)
#   gone:Window             waits until no window title contains Window
#   sleep:ms                waits that many milliseconds
#   answer:Yes              presses a message box's button (OK, Cancel, Yes, No) by posting its command
#   toggle:Name             toggles a check box
#   patterns:Name           lists the patterns an element supports
#   until:Window|text       waits until text shows anywhere in that window (a label, or a box's value)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# Plain Win32 controls (a message box's buttons) only have patterns through the client-side proxies.
try { [System.Windows.Automation.ClientSettings]::RegisterClientSideProviderAssembly([UIAutomationClientsideProviders.UIAutomationClientSideProviders].Assembly.GetName()) } catch { }
if (-not ("MsgBox" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MsgBox {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dlg, int id);
}
"@
}
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$S = Split-Path -Parent $MyInvocation.MyCommand.Path

function Windows {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
  $AE::RootElement.FindAll($Scope::Children, $cond)
}

function Find-Any([string]$name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  for ($i = 0; $i -lt 40; $i++) {
    foreach ($w in Windows) {
      if ($w.Current.Name -eq $name) { return $w }
      $e = $w.FindFirst($Scope::Descendants, $cond)
      if ($e) { return $e }
    }
    Start-Sleep -Milliseconds 250
  }
  throw "not found: $name"
}

function Find-Window([string]$title) {
  for ($i = 0; $i -lt 40; $i++) {
    foreach ($w in Windows) {
      if ($w.Current.Name -like "*$title*") { return $w }
      foreach ($c in $w.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($c.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $c.Current.Name -like "*$title*") { return $c }
      }
    }
    Start-Sleep -Milliseconds 250
  }
  throw "window not found: $title"
}

function Pattern($e, $p) { $x = $null; if ($e.TryGetCurrentPattern($p, [ref]$x)) { return $x }; return $null }

# The first element with that name that can be invoked (a message box's "Yes" is a pane holding the button).
function Find-Invokable([string]$name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  for ($i = 0; $i -lt 40; $i++) {
    foreach ($w in Windows) {
      foreach ($e in $w.FindAll($Scope::Descendants, $cond)) {
        $inv = Pattern $e ([System.Windows.Automation.InvokePattern]::Pattern)
        if ($inv) { return $inv }
      }
    }
    Start-Sleep -Milliseconds 250
  }
  throw "nothing named '$name' can be invoked"
}

foreach ($step in $Do) {
  $verb, $rest = $step.Split(":", 2)
  switch ($verb) {
    "open" {
      $e = Find-Any $rest
      $ec = Pattern $e ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
      if ($ec) { $ec.Expand() } else { (Pattern $e ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
      Start-Sleep -Milliseconds 400
    }
    "invoke" {
      $inv = Find-Invokable $rest
      # A button that opens a modal dialog only returns when the dialog closes: UI Automation then reports a
      # timeout, but the click has happened.
      try { $inv.Invoke() } catch [System.Runtime.InteropServices.COMException] { if ($_.Exception.HResult -ne -2146233083) { throw } }
      catch { if ($_.Exception.InnerException.HResult -ne -2146233083 -and $_.Exception.HResult -ne -2146233083) { throw } }
      Start-Sleep -Milliseconds 400
    }
    "set" {
      $where, $assign = $rest.Split("|", 2)
      $field, $value = $assign.Split("=", 2)
      $w = Find-Window $where
      $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $field)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
      $e = $null
      for ($i = 0; $i -lt 20 -and -not $e; $i++) { $e = $w.FindFirst($Scope::Descendants, $cond); if (-not $e) { Start-Sleep -Milliseconds 250 } }
      if (-not $e) { throw "edit box not found: $field" }
      (Pattern $e ([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($value)
    }
    "wait" { $null = Find-Window $rest }
    "sleep" { Start-Sleep -Milliseconds ([int]$rest) }
    "answer" {
      # A message box's buttons have no UI Automation patterns: the button's command is posted to the box instead,
      # which it handles as that button being pressed (no mouse, keyboard or focus change).
      $ids = @{ "OK" = 1; "Cancel" = 2; "Yes" = 6; "No" = 7 }
      if (-not $ids.ContainsKey($rest)) { throw "answer: OK, Cancel, Yes or No" }
      $box = $null
      $isBox = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, "#32770")
      for ($i = 0; $i -lt 40 -and -not $box; $i++) {
        foreach ($w in Windows) {
          if ($w.Current.ClassName -eq "#32770") { $box = $w; break }
          $c = $w.FindFirst($Scope::Children, $isBox)
          if ($c) { $box = $c; break }
        }
        if (-not $box) { Start-Sleep -Milliseconds 250 }
      }
      if (-not $box) { throw "no message box is open" }
      $h = [IntPtr]$box.Current.NativeWindowHandle
      $id = $ids[$rest]
      if ([MsgBox]::GetDlgItem($h, $id) -eq [IntPtr]::Zero) { throw "the message box has no $rest button" }
      [void][MsgBox]::PostMessage($h, 0x0111, [IntPtr]$id, [MsgBox]::GetDlgItem($h, $id))
      Start-Sleep -Milliseconds 400
    }
    "toggle" {
      $e = Find-Any $rest
      $t = Pattern $e ([System.Windows.Automation.TogglePattern]::Pattern)
      if (-not $t) { throw "'$rest' cannot be toggled" }
      $t.Toggle()
      Start-Sleep -Milliseconds 200
    }
    "patterns" {
      # Lists what an element supports (for scripts that cannot invoke something).
      $e = Find-Any $rest
      "  $($e.Current.ControlType.ProgrammaticName) '$($e.Current.Name)': " + (($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }) -join ', ')
    }
    "until" {
      # Waits (up to 3 minutes) for text to appear in a window, e.g. a status label when background work finishes.
      $title, $text = $rest.Split("|", 2)
      $w = Find-Window $title
      $seen = $false
      for ($i = 0; $i -lt 720 -and -not $seen; $i++) {
        foreach ($e in $w.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
          if ($e.Current.Name -like "*$text*") { $seen = $true; break }
          $v = Pattern $e ([System.Windows.Automation.ValuePattern]::Pattern)
          if ($v -and $v.Current.Value -like "*$text*") { $seen = $true; break }
        }
        if (-not $seen) { Start-Sleep -Milliseconds 250 }
      }
      if (-not $seen) { throw "text not seen in ${title}: $text" }
    }
    "gone" {
      for ($i = 0; $i -lt 40; $i++) {
        if (-not (Windows | Where-Object { $_.Current.Name -like "*$rest*" })) { break }
        Start-Sleep -Milliseconds 250
      }
    }
    "shot" {
      $title, $file = $rest.Split("|", 2)
      $w = Find-Window $title
      & (Join-Path $S "winshot.ps1") -ProcessId $ProcessId -Title $w.Current.Name -Out (Join-Path $ShotDir $file) | Out-Null
    }
  }
  "done $step"
}
