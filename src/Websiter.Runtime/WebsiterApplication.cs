using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Websiter.Diagnostics;
using Websiter.Server;
using Websiter.Workers;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace Websiter;

/// <summary>
/// Composition root for a Websiter application. Created via
/// <see cref="WebsiterApplication.Create(string[])"/>; wires configuration,
/// logging, the HTTP server, the IPC host endpoint and the worker supervisor.
///
/// <code>
/// var app = WebsiterApplication.Create(args);
/// app.MapGet("/", ctx =&gt; ctx.Response.WriteAsync("Hello Websiter!"));
/// await app.RunAsync();
/// </code>
/// </summary>
public sealed class WebsiterApplication : IAsyncDisposable
{
    private readonly string[] _args;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _workersStarted;
    private int _disposed;

    private WebsiterApplication(string root, string[] args)
    {
        Root = root;
        _args = args;
        Server = new WebsiterServer(root);
        Workers = null;

        // Secure, functional defaults — every app gets exception handling,
        // security headers, rate limiting, compression, CORS (when configured)
        // and static/page serving out of the box. Use* calls are idempotent.
        Server.UseExceptionHandler()
              .UseSecurityHeaders()
              .UseRateLimiting()
              .UseCompression()
              .UseCors()
              .UseStaticFiles();
    }

    /// <summary>Project root directory.</summary>
    public string Root { get; }

    /// <summary>The underlying HTTP server.</summary>
    public WebsiterServer Server { get; }

    /// <summary>The worker supervisor (null until workers are discovered/started).</summary>
    public WorkerSupervisor? Workers { get; private set; }

    /// <summary>Effective listen URL.</summary>
    public string Url => Server.Url;

    /// <summary>
    /// Creates the application: resolves the project root, loads configuration,
    /// initializes environment, logging and secrets.
    /// </summary>
    public static WebsiterApplication Create(string[] args)
    {
        var root = FindRoot(args);
        Paths.Initialize(root);
        SecretsInitialize(root);

        WebsiterEnvironment.InitializeFromEnvironment();
        var config = WebsiterConfig.Load(root);
        var envName = config["application:environment"];
        if (!string.IsNullOrWhiteSpace(envName))
            WebsiterEnvironment.Initialize(envName);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            builder.AddProvider(new FileLoggerProvider(Paths.LogFile("app")));
        });
        Log.Initialize(loggerFactory.CreateLogger("Websiter"));

        return new WebsiterApplication(root, args);
    }

    private static void SecretsInitialize(string root)
    {
        try { Websiter.Security.Secrets.Initialize(root); } catch { }
    }

    private static string FindRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--root" or "-r") return Path.GetFullPath(args[i + 1]);
        }
        var envRoot = Environment.GetEnvironmentVariable("WEBSITER_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
            return Path.GetFullPath(envRoot);
        return WorkerMain.FindProjectRoot(Directory.GetCurrentDirectory());
    }

    // ------------------------- Routing passthrough -------------------------

    /// <inheritdoc cref="WebsiterServer.MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapGet(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.MapGet(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapGet(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.MapGet(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapGet(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.MapGet(pattern, handler, name); return this; }

    /// <inheritdoc cref="WebsiterServer.MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPost(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.MapPost(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPost(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.MapPost(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPost(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.MapPost(pattern, handler, name); return this; }

    /// <inheritdoc cref="WebsiterServer.MapPut(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPut(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.MapPut(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPut(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPut(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.MapPut(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPut(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPut(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.MapPut(pattern, handler, name); return this; }

    /// <inheritdoc cref="WebsiterServer.MapDelete(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapDelete(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.MapDelete(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapDelete(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapDelete(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.MapDelete(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapDelete(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapDelete(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.MapDelete(pattern, handler, name); return this; }

    /// <inheritdoc cref="WebsiterServer.MapPatch(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPatch(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.MapPatch(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPatch(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPatch(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.MapPatch(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.MapPatch(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication MapPatch(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.MapPatch(pattern, handler, name); return this; }

    /// <inheritdoc cref="WebsiterServer.Map(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication Map(string pattern, Func<HttpContext, Task> handler, string? name = null)
    { Server.Map(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.Map(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication Map(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
    { Server.Map(pattern, handler, name); return this; }
    /// <inheritdoc cref="WebsiterServer.Map(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterApplication Map(string pattern, Func<HttpContext, object?> handler, string? name = null)
    { Server.Map(pattern, handler, name); return this; }

    /// <summary>Maps a WebSocket endpoint.</summary>
    public WebsiterApplication MapWebSocket(string pattern, Func<System.Net.WebSockets.WebSocket, HttpContext, CancellationToken, Task> handler)
    { Server.MapWebSocket(pattern, handler); return this; }

    // ------------------------- Middleware passthrough -------------------------

    /// <summary>Adds custom middleware.</summary>
    public WebsiterApplication Use(Func<HttpContext, Func<Task>, Task> middleware)
    { Server.Use(middleware); return this; }
    /// <summary>Adds middleware that runs outermost, before all defaults.</summary>
    public WebsiterApplication UseFirst(Func<HttpContext, Func<Task>, Task> middleware)
    { Server.UseFirst(middleware); return this; }
    /// <summary>Adds security headers.</summary>
    public WebsiterApplication UseSecurityHeaders() { Server.UseSecurityHeaders(); return this; }
    /// <summary>Adds rate limiting.</summary>
    public WebsiterApplication UseRateLimiting() { Server.UseRateLimiting(); return this; }
    /// <summary>Adds static file serving.</summary>
    public WebsiterApplication UseStaticFiles() { Server.UseStaticFiles(); return this; }
    /// <summary>Adds exception handling.</summary>
    public WebsiterApplication UseExceptionHandler() { Server.UseExceptionHandler(); return this; }
    /// <summary>Adds response compression.</summary>
    public WebsiterApplication UseCompression() { Server.UseCompression(); return this; }
    /// <summary>Adds CORS.</summary>
    public WebsiterApplication UseCors() { Server.UseCors(); return this; }

    // ------------------------- Lifecycle -------------------------

    /// <summary>
    /// Runs the application: dispatches worker processes when launched with
    /// <c>--websiter-worker</c>, otherwise starts IPC, workers and the HTTP server.
    /// Blocks until shutdown; returns the exit code.
    /// </summary>
    public async Task<int> RunAsync()
    {
        // Worker process dispatch: the same assembly runs workers via flag.
        var workerArg = _args.FirstOrDefault(a => a.StartsWith("--websiter-worker=", StringComparison.Ordinal));
        if (workerArg is not null)
        {
            var workerName = workerArg["--websiter-worker=".Length..];
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return await WorkerMain.RunWorkerAsync(assembly, workerName, _args).ConfigureAwait(false);
        }

        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; _shutdown.Cancel(); });
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; _shutdown.Cancel(); });
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _shutdown.Cancel(); };

        try
        {
            // 1. IPC host endpoint so workers can call back into the host.
            await IPC.StartAsync(_shutdown.Token).ConfigureAwait(false);
            IPC.RegisterHandler("$host-status", (System.Text.Json.JsonElement? payload, System.Threading.CancellationToken ct) => Task.FromResult<object?>(new
            {
                status = "online",
                framework = "Websiter",
                environment = WebsiterEnvironment.Name,
                pid = Environment.ProcessId,
            }));

            // 2. Discover and start workers.
            await EnsureWorkersStartedAsync(_shutdown.Token).ConfigureAwait(false);

            // Host control endpoints for the CLI (`websiter workers`, `routes`, `stop`).
            IPC.RegisterHandler("$shutdown", (System.Text.Json.JsonElement? payload, System.Threading.CancellationToken ct) =>
            {
                _ = Task.Run(() => _shutdown.Cancel(), ct);
                return Task.FromResult<object?>(new { stopping = true });
            });
            IPC.RegisterHandler("$workers", (System.Text.Json.JsonElement? payload, System.Threading.CancellationToken ct) => Task.FromResult<object?>(
                Workers?.GetStatus() ?? (IReadOnlyList<WorkerStatus>)Array.Empty<WorkerStatus>()));
            IPC.RegisterEnvelopeHandler("$worker-start", async (Websiter.Ipc.IpcEnvelope env, System.Threading.CancellationToken ct) =>
            {
                var name = ExtractWorkerName(env.Payload);
                if (name is null || Workers is null) return new { ok = false, error = "no worker name or supervisor" };
                await Workers.StartWorkerAsync(name, ct).ConfigureAwait(false);
                return new { ok = true, worker = name };
            });
            IPC.RegisterEnvelopeHandler("$worker-stop", async (Websiter.Ipc.IpcEnvelope env, System.Threading.CancellationToken ct) =>
            {
                var name = ExtractWorkerName(env.Payload);
                if (name is null || Workers is null) return new { ok = false, error = "no worker name or supervisor" };
                await Workers.StopWorkerAsync(name, ct).ConfigureAwait(false);
                return new { ok = true, worker = name };
            });
            IPC.RegisterEnvelopeHandler("$worker-restart", async (Websiter.Ipc.IpcEnvelope env, System.Threading.CancellationToken ct) =>
            {
                var name = ExtractWorkerName(env.Payload);
                if (name is null || Workers is null) return new { ok = false, error = "no worker name or supervisor" };
                await Workers.RestartWorkerAsync(name, ct).ConfigureAwait(false);
                return new { ok = true, worker = name };
            });
            static string? ExtractWorkerName(System.Text.Json.JsonElement? payload)
            {
                if (payload is null) return null;
                var el = payload.Value;
                if (el.ValueKind == System.Text.Json.JsonValueKind.Object
                    && el.TryGetProperty("name", out var n)) return n.GetString();
                if (el.ValueKind == System.Text.Json.JsonValueKind.String) return el.GetString();
                return null;
            }

            IPC.RegisterHandler("$routes", (System.Text.Json.JsonElement? payload, System.Threading.CancellationToken ct) => Task.FromResult<object?>(
                Server.Routes.Routes.Select(r => new { method = r.Method, pattern = r.Pattern, name = r.Name })));

            // 3. Health checks.
            Server.Health.AddCheck("ipc", async ct =>
            {
                var pong = await IPC.RequestAsync<object>("host", "$ping", timeout: TimeSpan.FromSeconds(2), ct: ct)
                    .ConfigureAwait(false);
                return pong is not null
                    ? new HealthCheckResult("ipc", HealthStatus.Healthy)
                    : new HealthCheckResult("ipc", HealthStatus.Degraded, "ping returned empty");
            });
            Server.Health.AddCheck("workers", _ =>
            {
                var workers = Workers?.GetStatus() ?? (IReadOnlyList<WorkerStatus>)Array.Empty<WorkerStatus>();
                var unhealthy = workers.Count(w => w.State is WorkerState.Crashed or WorkerState.Failed);
                return Task.FromResult(new HealthCheckResult("workers",
                    unhealthy > 0 ? HealthStatus.Degraded : HealthStatus.Healthy,
                    $"{workers.Count} worker(s), {unhealthy} unhealthy",
                    workers.Select(w => new { w.Name, state = w.State.ToString(), w.Pid })));
            });

            // 4. HTTP server (blocks until shutdown).
            await Server.RunAsync(_shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal($"Application failed: {ex}");
            return 1;
        }
        finally
        {
            await ShutdownWorkersAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Discovers workers in the entry assembly and starts the supervisor.</summary>
    public async Task EnsureWorkersStartedAsync(CancellationToken ct = default)
    {
        if (_workersStarted) return;
        _workersStarted = true;

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var definitions = WorkerDiscovery.DiscoverInAssembly(assembly)
            .Select(d =>
            {
                // Read declared dependencies by instantiating briefly.
                if (d.TypeName is not null)
                {
                    try
                    {
                        var type = assembly.GetType(d.TypeName);
                        if (type is not null && Activator.CreateInstance(type) is Worker w)
                            d.DependsOn.AddRange(w.DependsOn);
                    }
                    catch { }
                }
                return d;
            })
            .Where(d => !string.IsNullOrEmpty(d.Name))
            .ToList();

        if (definitions.Count == 0)
        {
            Log.Debug("No workers discovered in the application assembly.");
            return;
        }

        Workers = new WorkerSupervisor(Root, definitions);
        Workers.StatusChanged += status =>
            Log.Info($"Worker '{status.Name}': {status.State}" +
                     (status.LastError is not null ? $" — {status.LastError}" : ""));

        Log.Info($"Starting {definitions.Count} worker(s): {string.Join(", ", definitions.Select(d => d.Name))}");
        await Workers.StartAllAsync(ct).ConfigureAwait(false);
    }

    private async Task ShutdownWorkersAsync()
    {
        if (Workers is null) return;
        try
        {
            Log.Info("Stopping workers...");
            await Workers.StopAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Error while stopping workers: {ex.Message}");
        }
    }

    /// <summary>Stops the application and all workers.</summary>
    public async Task StopAsync()
    {
        _shutdown.Cancel();
        await Server.StopAsync().ConfigureAwait(false);
        await ShutdownWorkersAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
        if (Workers is not null) await Workers.DisposeAsync().ConfigureAwait(false);
        await Server.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
