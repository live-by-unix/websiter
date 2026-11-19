param([string]$Configuration = "Debug")
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build Websiter.slnx -c $Configuration --nologo
