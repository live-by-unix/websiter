# Publish Websiter packages to NuGet (requires NUGET_API_KEY).
$ErrorActionPreference = "Stop"
if (-not $env:NUGET_API_KEY) { Write-Error "NUGET_API_KEY is not set"; exit 1 }
Set-Location (Split-Path $PSScriptRoot)
dotnet pack Websiter.slnx -c Release -o artifacts/packages
dotnet nuget push "artifacts/packages/*.nupkg" --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
