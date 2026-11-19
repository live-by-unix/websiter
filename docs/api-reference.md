# API Reference

Root namespace `Websiter` — `using Websiter;` gives you the whole surface.

## Application

```csharp
var app = WebsiterApplication.Create(args);
await app.RunAsync(ct);
```

`WebsiterApplication` exposes the server API: `MapGet/Post/Put/Delete/Patch/Head/Map/MapWebSocket`, `Use*`, `Routes`.

## Static services

| API | Purpose |
|---|---|
| `Log.Info/Warn/Error/Fatal` | Structured logging (initialize via host/worker bootstrap) |
| `Config.Get<T>(key, fallback)` | Layered configuration |
| `Config.Root` | `IConfiguration` root |
| `WebsiterEnvironment.IsDevelopment/IsProduction/Name` | Environment detection |
| `Paths.*` | `Root`, `StateDir`, `LogFile`, `WorkerStateFile`, `PagesDir`, ... |
| `IPC.SendAsync / RequestAsync<T> / PublishAsync / SendEventAsync` | IPC client calls |
| `IPC.RegisterHandler / Subscribe` | IPC server-side handlers |
| `Secrets.Get/TryGet/ResolveReference/Set` | Secret resolution |
| `Validation.Is*` | Input validators |

## HTTP helpers (`HttpContext` extensions)

`ctx.Param<T>(name)`, `ctx.Param(name)`, `ctx.Query<T>(name)`, `ctx.Query(name)`,
`ctx.ReadJsonAsync<T>(ct)`, `ctx.WriteJsonAsync(value, status, ct)`,
`ctx.WriteTextAsync(text, contentType, ct)`, `ctx.StatusAsync(code, ct)`,
`ctx.RedirectAsync(url, status, ct)`, `ctx.Cookie(name)`, `ctx.SetCookie(...)`.

## Worker

```csharp
public abstract class Worker {
    public virtual string Name { get; }          // default: class name
    public virtual IReadOnlyList<string> DependsOn { get; }
    public abstract Task ExecuteAsync(CancellationToken ct);
    protected void RegisterHandler(string action, Func<JsonElement?, CancellationToken, Task<object?>> h);
    protected void RegisterHandler(string action, Func<IpcEnvelope, CancellationToken, Task<object?>> h);
    protected void Subscribe(string topic, Func<JsonElement?, CancellationToken, Task> h);
    protected Task SendAsync / RequestAsync<T> / PublishAsync(...);
    protected void Stop();
    protected Task RunAsync(CancellationToken ct);   // call at end of ExecuteAsync
}
```

## IPC types (`Websiter.Ipc` / `Websiter.IPC`)

`IpcEnvelope` (kind, id, correlationId, from/to, action, payload, ok, error),
`IpcMessageKind` (Request/Send/Response/Event), `IpcOptions`,
`IpcException`, `IPC` facade.

## Server options (`WebsiterServerOptions`)

`Host`, `Port`, `WebRoot`, `PagesRoot`, `ComponentsRoot`, `StaticCacheControl`, `UseWebSockets`, `MaxRequestBodySize`.

## Diagnostics

`Health.CheckAsync(ct)` → status report; `Metrics.RecordRequest/Snapshot`;
`GET /__websiter/{health,metrics,status,routes}`.

## Deployment

`DeploymentPackager`, `SystemdUnitGenerator` — used by `websiter deploy`.
