# Restart a Websiter app: stop then start.
param([string]$AppDir = ".")
& "$PSScriptRoot/stop.ps1" $AppDir
& "$PSScriptRoot/start.ps1" $AppDir
