# Configuration

Websiter configuration is layered, lowest to highest precedence:

1. `websiter.json` — base configuration
2. `websiter.<Environment>.json` — environment overrides (e.g. `websiter.Production.json`)
3. Environment variables — `WEBSITER_SECTION__KEY` (double underscore = `:`)
4. CLI overrides where supported

## websiter.json

```json
{
  "application": {
    "name": "MyWebsite",
    "environment": "Development"
  },
  "server": {
    "host": "0.0.0.0",
    "port": 8080,
    "https": { "enabled": false, "port": 8443, "certificatePath": null, "keyPath": null }
  },
  "workers": {
    "autoRestart": true,
    "restartLimit": 5,
    "restartDelayMs": 1000,
    "maxRestartDelayMs": 30000,
    "startupTimeoutMs": 30000
  },
  "ipc": {
    "transport": "auto",
    "requestTimeoutSeconds": 30,
    "maxMessageBytes": 4194304
  },
  "security": {
    "headers": { "enabled": true },
    "rateLimit": { "enabled": true, "requestsPerWindow": 120, "windowSeconds": 60 },
    "cors": { "enabled": false, "allowedOrigins": [] }
  }
}
```

## Environment variables

Any key can be set via environment: `server:port` → `WEBSITER_SERVER__PORT=9090`.

Special variables:

| Variable | Purpose |
|---|---|
| `WEBSITER_APP` | Application name (IPC endpoint namespace) |
| `WEBSITER_ENV` | Environment name (`Development`, `Production`, ...) |
| `WEBSITER_IPC_DIR` | Override IPC socket/registry directory |
| `WEBSITER_ROOT` | Override project root |

## Accessing values in code

```csharp
var name = Config.Get("application:name");
var port = Config.Get<int>("server:port", 8080);
var connStr = Secrets.ResolveReference(Config.Get("database:connectionString"));
```

`Websiter.Environment.IsDevelopment` / `.IsProduction` reflect the resolved environment.

## Secrets

Never commit credentials. Store them in environment variables and reference them:

```json
{ "database": { "connectionString": "secretref:DbConnectionString" } }
```

`secretref:NAME` resolves `WEBSITER_SECRET_NAME` (or `NAME` as fallback). See [Security](security.md).
