# Websiter repo setup (Windows PowerShell).
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error "dotnet SDK not found. Install .NET 10 SDK: https://dotnet.microsoft.com/download"
}
dotnet restore Websiter.slnx
dotnet build Websiter.slnx --nologo -v q
Write-Host "==> Setup complete."
