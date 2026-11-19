Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet clean Websiter.slnx --nologo -v q
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "==> Cleaned."
