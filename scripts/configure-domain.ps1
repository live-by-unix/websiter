# Print DNS + reverse-proxy guidance for a domain (does not touch registrar/DNS).
param([Parameter(Mandatory=$true)][string]$Domain, [string]$AppDir = ".")
Write-Output "Domain: $Domain"
Write-Output "1. Create an A record for $Domain pointing at this server's public IP."
Write-Output "2. Configure your reverse proxy (nginx/Caddy/Traefik) to forward to the app port."
Write-Output "3. Run 'websiter ssl enable' or your proxy's TLS automation for certificates."
Write-Output "See docs/domains.md and docs/ssl.md for details."
