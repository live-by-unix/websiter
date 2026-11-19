# Databases

Websiter does not ship a proprietary database layer. It is designed to sit on top of the .NET ecosystem's mature providers — typically inside a dedicated worker so the host stays thin.

## Recommended pattern

```text
HTTP request → host route → IPC → DatabaseWorker → Npgsql/MySql/Data.SqlClient/SQLite
```

The worker owns the connection pool and exposes typed IPC actions (`GetUsers`, `CreateOrder`, ...). This gives you:

- Connection pooling via the provider (e.g. Npgsql, MySqlConnector, Microsoft.Data.SqlClient, Microsoft.Data.Sqlite)
- Parameterized queries only — never string-concatenate SQL
- Transaction support inside worker handlers
- Crash isolation: a DB worker restart doesn't drop HTTP connections
- Health checks via IPC `$status` / custom actions

## Configuration

```json
{
  "database": {
    "provider": "postgres",
    "connectionString": "secretref:DbConnectionString"
  }
}
```

Resolve secrets with `Secrets.ResolveReference` — see [Security](security.md).

## Migrations

Use established tooling per provider: EF Core Migrations, DbUp, FluentMigrator, or provider-native tools. Run migrations as a worker startup step or a dedicated CLI command — not on every request.

## Health checks

Register a custom health check or expose an IPC action like `DatabaseWorker.Ping` and surface it via `/__websiter/health`.
