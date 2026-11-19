# Start a built Websiter application.
param([string]$AppDir = ".")
Set-Location $AppDir
try { dotnet run -c Release --project . } catch { dotnet run --project . }
