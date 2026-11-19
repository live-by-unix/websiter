# AWS EC2

## Quick path

```bash
./scripts/deploy-ec2.sh <ssh-host> ./MyWebsite /opt/mywebsite
```

The script: builds a self-contained `linux-x64` publish, rsyncs it to the instance, and installs a systemd unit when the `websiter` CLI is present remotely.

## Manual steps

1. **Launch** an EC2 instance (Ubuntu 24.04 LTS recommended), open ports 22 and 80/443.
2. **Prepare**: `sudo apt update && sudo apt install -y dotnet-sdk-10.0` *or* use self-contained publish (no runtime needed).
3. **Upload**: `rsync -az publish/linux-x64/ host:/opt/mywebsite/`
4. **Configure**: `/etc/mywebsite/env` with `WEBSITER_ENV=Production` and `secretref:` backing env vars.
5. **Service**: install the generated systemd unit, `systemctl enable --now websiter`.
6. **Verify**: `curl http://<ip>/__websiter/health` → `{"status":"healthy"}`.

## Security groups

- 22/tcp — SSH (restrict to your IP)
- 80/443/tcp — public HTTP/S
- Nothing else. IPC stays on localhost sockets; Kestrel binds `127.0.0.1` behind the proxy.

## Rollback

Keep `releases/<ts>/` dirs; repoint and restart. Database migrations should be backward-compatible or run as a separate deploy step.
