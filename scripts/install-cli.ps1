Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet pack src/Websiter.CLI/Websiter.CLI.csproj -c Release -o artifacts --nologo
dotnet tool install --global websiter --add-source ./artifacts 2>$null
if ($LASTEXITCODE -ne 0) { dotnet tool update --global websiter --add-source ./artifacts }
Write-Host "==> Installed. Run: websiter --version"
