using System.Text.Json;
using Websiter.Ipc;

namespace Websiter;

/// <summary>
/// Native inter-process communication facade.
///
/// Workers run as independent OS processes; this API lets the host and workers
/// exchange messages over OS-native transports (Unix domain sockets on Linux/
/// macOS, named pipes on Windows) without configuring HTTP endpoints.
///
/// Delivery semantics:
/// - Request/response: correlated, at-most-once per attempt; errors and
///   timeouts surface as <see cref="IpcException"/>.
/// - Send: at-least-once within a connection; a disconnected peer loses the
///   message (it is not queued for redelivery).
/// - Events (<see cref="PublishAsync"/>): at-least-once broadcast to all
///   subscribers; subscribers must be idempotent.
/// </summary>
public static class IPC
{
    /// <summary>The underlying hub for the current process.</summary>
    public static IpcHub Hub => IpcHub.Current;

    /// <summary>Sends a one-way message to a worker's action handler.</summary>
    public static Task SendAsync(string worker, string action, object? payload = null, CancellationToken ct = default)
        => Hub.SendAsync(worker, action, payload, ct);

    /// <summary>Sends a request and returns the raw response envelope.</summary>
    public static Task<IpcEnvelope> RequestAsync(string worker, string action, object? payload = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Hub.RequestAsync(worker, action, payload, timeout, ct);

    /// <summary>Sends a request and deserializes the response payload to <typeparamref name="T"/>.</summary>
    public static Task<T?> RequestAsync<T>(string worker, string action, object? payload = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Hub.RequestAsync<T>(worker, action, payload, timeout, ct);

    /// <summary>Sends a typed convenience request using a payload object.</summary>
    public static Task<TResponse?> CallAsync<TRequest, TResponse>(string worker, string action, TRequest payload,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Hub.RequestAsync<TResponse>(worker, action, payload, timeout, ct);

    /// <summary>Publishes an event to every subscriber of <paramref name="topic"/>.</summary>
    public static Task PublishAsync(string topic, object? payload = null, CancellationToken ct = default)
        => Hub.PublishAsync(topic, payload, ct);

    /// <summary>Sends a directed event to a single worker.</summary>
    public static Task SendEventAsync(string worker, string topic, object? payload = null, CancellationToken ct = default)
        => Hub.SendEventAsync(worker, topic, payload, ct);

    /// <summary>Registers a handler for request/send actions (worker side).</summary>
    public static void RegisterHandler(string action, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => Hub.RegisterHandler(action, new IpcMessageHandler((envelope, ct) => handler(envelope.Payload, ct)));

    /// <summary>Registers a handler receiving the full envelope (for advanced scenarios like correlation IDs).</summary>
    public static void RegisterEnvelopeHandler(string action, Func<IpcEnvelope, CancellationToken, Task<object?>> handler)
        => Hub.RegisterHandler(action, new IpcMessageHandler(handler));

    /// <summary>Subscribes to events on a topic ("*" for all topics).</summary>
    public static void Subscribe(string topic, Func<JsonElement?, CancellationToken, Task> handler)
        => Hub.Subscribe(topic, (envelope, ct) => handler(envelope.Payload, ct));

    /// <summary>Subscribes to events receiving the full envelope.</summary>
    public static void SubscribeEnvelope(string topic, Func<IpcEnvelope, CancellationToken, Task> handler)
        => Hub.Subscribe(topic, handler);

    /// <summary>Removes a handler previously registered with <see cref="RegisterHandler"/>.</summary>
    public static bool UnregisterHandler(string action) => Hub.UnregisterHandler(action);

    /// <summary>Starts the IPC listener for this process (called automatically by worker/host startup).</summary>
    public static Task StartAsync(CancellationToken ct = default) => Hub.StartServerAsync(ct);

    /// <summary>Diagnostics snapshot for this process's IPC hub.</summary>
    public static object Diagnostics => Hub.GetDiagnostics();
}
