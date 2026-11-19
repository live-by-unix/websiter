# Build and package a Websiter app for deployment (publish + systemd unit).
param([string]$AppDir = ".", [string]$Rid = "")
Set-Location $AppDir
$args = @("publish", "-c", "Release", "-o", "publish")
if ($Rid) { $args += @("-r", $Rid, "--self-contained", "false") }
dotnet @args
Write-Output "Package ready: $(Resolve-Path publish)"
