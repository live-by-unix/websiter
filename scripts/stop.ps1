# Stop a running Websiter app via its pid/state files.
param([string]$AppDir = ".")
$pidFile = Join-Path $AppDir ".websiter/app.pid"
if (Test-Path $pidFile) {
  $procId = [int](Get-Content $pidFile)
  try { Stop-Process -Id $procId -ErrorAction Stop; Write-Output "Stopped app (pid $procId)" }
  catch { Write-Output "Process $procId not running" }
} else {
  Write-Error "No running app state found in $(Split-Path $pidFile)"
  exit 1
}
