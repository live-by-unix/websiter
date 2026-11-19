#!/usr/bin/env bash
# Enable TLS for a domain using certbot (nginx plugin).
set -euo pipefail
DOMAIN="${1:?Usage: configure-ssl.sh <domain>}"
if ! command -v certbot >/dev/null 2>&1; then
  echo "certbot not found. Install it first (e.g. 'apt install certbot python3-certbot-nginx')." >&2
  exit 1
fi
certbot --nginx -d "$DOMAIN" --redirect --agree-tos --non-interactive || \
  certbot --nginx -d "$DOMAIN" --agree-tos
certbot renew --dry-run
echo "==> TLS enabled for $DOMAIN. Renewal timer: 'systemctl status certbot.timer'"
