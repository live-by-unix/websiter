Write-Host "== Websiter doctor =="
$v = dotnet --version 2>$null
if ($v) { Write-Host "dotnet: $v" } else { Write-Host "dotnet: MISSING" }
$w = websiter --version 2>$null
if ($w) { Write-Host "websiter CLI: $w" } else { Write-Host "websiter CLI: not installed" }
Write-Host "os: $([System.Environment]::OSVersion)"
