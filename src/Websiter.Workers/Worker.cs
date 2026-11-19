using System.Text.Json;
using Websiter.Ipc;

namespace Websiter;

/// <summary>
/// Base class for Websiter workers. Each concrete worker runs as an
/// independent operating-system process and communicates with the host and
/// other workers exclusively through <see cref="IPC"/>.
///
/// Typical worker:
/// <code>
/// public class AuthWorker : Worker
/// {
///     public override async Task ExecuteAsync(CancellationToken cancellationToken)
///     {
///         RegisterHandler("Authenticate", async message =&gt;
///         {
///             return await AuthenticateAsync(message);
///         });
///         await RunAsync(cancellationToken);
///     }
/// }
/// </code>
/// </summary>
public abstract class Worker
{
    private readonly CancellationTokenSource _shutdown = new();
    private DateTimeOffset _startedUtc;
    private int _heartbeatStarted;

    /// <summary>Unique worker name. Defaults to the class name; override or set via config.</summary>
    public virtual string Name => GetType().Name;

    /// <summary>Human-readable display name (defaults to <see cref="Name"/>).</summary>
    public virtual string DisplayName => Name;

    /// <summary>Declared dependencies on other workers (startup ordering + validation).</summary>
    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    /// <summary>Binds the process IPC hub to this worker's identity. Called by the worker host before <see cref="ExecuteAsync"/>.</summary>
    internal void Prepare() => IpcHub.Initialize(Name);

    /// <summary>Called once by the worker host with a cancellation token tied to process shutdown signals.</summary>
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>Registers a request/send handler. The handler receives the message payload as <see cref="JsonElement"/>.</summary>
    protected void RegisterHandler(string action, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => IPC.RegisterHandler(action, handler);

    /// <summary>Registers a handler receiving the full <see cref="IpcEnvelope"/> (advanced scenarios).</summary>
    protected void RegisterEnvelopeHandler(string action, Func<IpcEnvelope, CancellationToken, Task<object?>> handler)
        => IPC.RegisterEnvelopeHandler(action, handler);

    /// <summary>Subscribes to an event topic.</summary>
    protected void Subscribe(string topic, Func<JsonElement?, CancellationToken, Task> handler)
        => IPC.Subscribe(topic, handler);

    /// <summary>Sends a one-way IPC message to another worker.</summary>
    protected Task SendAsync(string worker, string action, object? payload = null, CancellationToken ct = default)
        => IPC.SendAsync(worker, action, payload, ct);

    /// <summary>Sends an IPC request and deserializes the response.</summary>
    protected Task<T?> RequestAsync<T>(string worker, string action, object? payload = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => IPC.RequestAsync<T>(worker, action, payload, timeout, ct);

    /// <summary>Publishes an event to all subscribers of a topic.</summary>
    protected Task PublishAsync(string topic, object? payload = null, CancellationToken ct = default)
        => IPC.PublishAsync(topic, payload, ct);

    /// <summary>Requests graceful shutdown of this worker.</summary>
    protected void Stop() => _shutdown.Cancel();

    /// <summary>
    /// Starts the IPC endpoint, registers the worker, writes heartbeat state and
    /// blocks until cancellation. Call at the end of <see cref="ExecuteAsync"/>.
    /// </summary>
    protected async Task RunAsync(CancellationToken cancellationToken)
    {
        _startedUtc = DateTimeOffset.UtcNow;

        // Ensure the hub is bound to this worker's identity.
        var hub = IpcHub.Initialize(Name);

        // Register shutdown handler so supervisors can stop workers gracefully over IPC.
        hub.RegisterHandler("$shutdown", new IpcMessageHandler((env, ct) =>
        {
            _ = Task.Run(() => _shutdown.Cancel());
            return Task.FromResult<object?>(new { stopping = true });
        }));

        await IPC.StartAsync(cancellationToken).ConfigureAwait(false);
        IpcRegistry.Register(hub.Options, Name, IPC.Hub.Subscriptions);

        if (Interlocked.Exchange(ref _heartbeatStarted, 1) == 0)
            _ = Task.Run(() => HeartbeatLoopAsync(_shutdown.Token));

        Log.Info($"Worker '{Name}' is running (pid {Environment.ProcessId}).");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            // Block until shutdown; ExecuteAsync already returned after RunAsync,
            // so we simply await the token here.
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            WriteState("stopped");
            IpcRegistry.Unregister(hub.Options, Name);
            Log.Info($"Worker '{Name}' stopped.");
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                WriteState("running");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void WriteState(string status)
    {
        try
        {
            var state = new
            {
                worker = Name,
                pid = Environment.ProcessId,
                status,
                startedUtc = _startedUtc,
                uptimeSeconds = (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds,
                subscriptions = IPC.Hub.Subscriptions,
                updatedUtc = DateTimeOffset.UtcNow,
            };
            File.WriteAllText(Paths.WorkerStateFile(Name), JsonSerializer.Serialize(state));
        }
        catch { /* heartbeat must never crash the worker */ }
    }
}
