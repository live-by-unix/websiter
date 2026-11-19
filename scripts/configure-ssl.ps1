# Enable HTTPS via a reverse proxy with automatic certificate renewal.
param([Parameter(Mandatory=$true)][string]$Domain, [string]$Email = "")
Write-Output "Configuring TLS for $Domain"
Write-Output "Option A (Caddy): install Caddyfile with '$Domain { reverse_proxy localhost:8080 }' — auto HTTPS."
Write-Output "Option B (nginx + certbot): run 'sudo certbot --nginx -d $Domain' then add an HTTPS server block."
Write-Output "Set 'server.https' in websiter.json only if terminating TLS in-app."
Write-Output "See docs/ssl.md."
