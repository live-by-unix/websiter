param([string]$AppDir = ".")
Set-Location $AppDir
if (Get-Command websiter -ErrorAction SilentlyContinue) { websiter dev }
else { dotnet run --project "$PSScriptRoot\..\src\Websiter.CLI" -- dev --root $AppDir }
