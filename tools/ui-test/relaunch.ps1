param([string[]]$Paths = @(), [string]$WorldArg = "", [int]$WaitSeconds = 7, [switch]$NoBuild)
# Stops the previous test instance (only the one this script started), rebuilds into bin\TestRun and starts it again.
$S = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repo = Split-Path -Parent (Split-Path -Parent $S)
$T = Join-Path $Repo "MinecraftStudio\bin\TestRun"
$pidFile = Join-Path $S "testpid.txt"
if (Test-Path $pidFile) {
  $old = [int](Get-Content $pidFile)
  $proc = Get-Process -Id $old -ErrorAction SilentlyContinue
  if ($proc -and $proc.Path -like "$T*") { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
}
if (-not $NoBuild) {
  Push-Location $Repo
  $out = dotnet build MinecraftStudio/MinecraftStudio.csproj -c Debug -o $T 2>&1
  Pop-Location
  $errors = $out | Select-String -Pattern ": error " | Select-Object -Unique
  if ($errors) { $errors | Select-Object -First 15; exit 1 }
  $out | Select-String -Pattern "warning CS" | Select-Object -Unique | Select-Object -First 10
}
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "$T\MinecraftStudio.exe"
if ($WorldArg -ne "") { $Paths = @($WorldArg) + $Paths }
$psi.Arguments = ($Paths | ForEach-Object { "`"$_`"" }) -join " "
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $T
$p = [System.Diagnostics.Process]::Start($psi)
Set-Content -Path $pidFile -Value $p.Id
Start-Sleep -Seconds $WaitSeconds
"started pid $($p.Id)"
