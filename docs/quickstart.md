# Quickstart

```bash
websiter new MyWebsite
cd MyWebsite
websiter dev
```

Open <http://localhost:8080>. The `basic` template gives you:

- `Program.cs` — route mappings and startup
- `Pages/` — HTML pages served at clean URLs
- `Components/` — reusable HTML fragments
- `wwwroot/` — CSS/JS/images
- `websiter.json` — configuration

## Add a JSON API route

Edit `Program.cs`:

```csharp
app.MapGet("/api/hello", ctx =>
    ctx.WriteJsonAsync(new { message = "Hello from Websiter" }));
```

`websiter dev` rebuilds and restarts automatically.

## Add a worker

```bash
websiter new Shop --template fullstack   # includes a Workers/ folder
```

Or create `Workers/GreeterWorker.cs`:

```csharp
public class GreeterWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Greet", message =>
            Task.FromResult<object?>(new { hello = "world" }));
        await RunAsync(cancellationToken);
    }
}
```

Workers are discovered and launched automatically as independent processes.
See [Workers](workers.md) and [IPC](ipc.md).
