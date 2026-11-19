# Deploy a packaged Websiter app to an EC2 host over SSH.
param(
  [Parameter(Mandatory=$true)][string]$Host,
  [Parameter(Mandatory=$true)][string]$User,
  [string]$KeyFile = "",
  [string]$RemoteDir = "/opt/websiter",
  [string]$AppDir = "."
)
$sshArgs = @("-o", "StrictHostKeyChecking=accept-new")
if ($KeyFile) { $sshArgs += @("-i", $KeyFile) }
& scp @sshArgs -r "$AppDir/publish" "${User}@${Host}:${RemoteDir}/app"
& ssh @sshArgs "${User}@${Host}" "sudo systemctl restart websiter && sudo systemctl status websiter --no-pager"
