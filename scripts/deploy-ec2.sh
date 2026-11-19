#!/usr/bin/env bash
# Deploy a Websiter app to an EC2 instance via SSH.
# Usage: deploy-ec2.sh <host> <app-dir> [remote-dir]
# Requires SSH key access configured (e.g. ~/.ssh/config or ssh-agent).
set -euo pipefail
HOST="${1:?Usage: deploy-ec2.sh <ssh-host> <app-dir> [remote-dir]}"
APP_DIR="${2:?app dir required}"
REMOTE_DIR="${3:-/opt/websiter-app}"
APP_NAME="$(basename "$APP_DIR")"

echo "==> Building self-contained publish for linux-x64"
cd "$APP_DIR"
dotnet publish -c Release -r linux-x64 --self-contained -o "publish/linux-x64"

echo "==> Uploading to $HOST:$REMOTE_DIR"
ssh "$HOST" "sudo mkdir -p $REMOTE_DIR && sudo chown \$USER:$USER $REMOTE_DIR"
rsync -az --delete "publish/linux-x64/" "$HOST:$REMOTE_DIR/"

echo "==> Installing systemd unit (if websiter CLI available remotely)"
ssh "$HOST" "if command -v websiter >/dev/null; then cd $REMOTE_DIR && websiter deploy systemd --install; fi" || \
  echo "NOTE: install websiter CLI on the host or copy a systemd unit manually (see docs/deployment.md)."

echo "==> Deployment complete. Verify with: ssh $HOST 'curl -s localhost:8080/__websiter/health'"
