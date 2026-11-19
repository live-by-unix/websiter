#!/usr/bin/env bash
# Assist with domain setup: prints DNS records and reverse-proxy guidance.
# Websiter cannot configure registrar DNS without provider credentials.
set -euo pipefail
DOMAIN="${1:?Usage: configure-domain.sh <domain> [port]}"
PORT="${2:-8080}"
cat <<MSG
Domain setup for $DOMAIN (Websiter listens on port $PORT)

1. DNS — create these records at your registrar/DNS provider:
   - A record:    $DOMAIN  -> <your-server-ip>
   - (optional) AAAA record for IPv6
   - www CNAME:   www.$DOMAIN -> $DOMAIN

2. Reverse proxy (recommended for TLS + static caching):
   nginx example — /etc/nginx/sites-available/$DOMAIN:

     server {
       listen 80;
       server_name $DOMAIN;
       location / {
         proxy_pass http://127.0.0.1:$PORT;
         proxy_set_header Host \$host;
         proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
       }
     }

3. TLS — after DNS propagates, run configure-ssl.sh $DOMAIN
   (uses certbot; ensure port 80 is reachable).

4. Verify: curl -I https://$DOMAIN/__websiter/health
MSG
