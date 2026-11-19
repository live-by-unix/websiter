# Install the websiter CLI globally from a local build.
# Publishes to ~/.local/share/websiter and links websiter into ~/.local/bin.
Set-Location (Split-Path $PSScriptRoot -Parent)

$installDir = if ($env:WEBSITER_INSTALL_DIR) { $env:WEBSITER_INSTALL_DIR } else { "$HOME/.local/share/websiter" }
$binDir     = if ($env:WEBSITER_BIN_DIR)     { $env:WEBSITER_BIN_DIR }     else { "$HOME/.local/bin" }

dotnet publish src/Websiter.CLI -c Release -o $installDir --nologo
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

$shim = Join-Path $binDir "websiter.cmd"
@"
@echo off
"$installDir\websiter.exe" %*
"@ | Set-Content -Path $shim -Encoding ascii

Write-Host "==> Installed. Ensure $binDir is on your PATH, then run: websiter --version"
