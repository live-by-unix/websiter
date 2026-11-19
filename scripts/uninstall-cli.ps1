# Remove the globally installed websiter CLI.
$installDir = if ($env:WEBSITER_INSTALL_DIR) { $env:WEBSITER_INSTALL_DIR } else { "$HOME/.local/share/websiter" }
$binDir     = if ($env:WEBSITER_BIN_DIR)     { $env:WEBSITER_BIN_DIR }     else { "$HOME/.local/bin" }

Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $binDir "websiter.cmd")
dotnet tool uninstall --global websiter 2>$null
if (Test-Path $installDir) {
  Remove-Item -Recurse -Force $installDir
  Write-Host "==> Removed $installDir"
} else {
  Write-Host "==> No installation found at $installDir"
}
