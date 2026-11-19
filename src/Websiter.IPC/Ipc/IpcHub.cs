using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Websiter.Ipc;

/// <summary>
/// Per-process IPC hub: owns the listening endpoint for the local worker/host,
/// caches outbound connections to peer workers, routes requests, sends and
/// events. Accessed through the public <see cref="IPC"/> facade.
/// </summary>
public sealed class IpcHub : IAsyncDisposable
{
    private static IpcHub? _current;
    private static readonly object _initGate = new();

    private readonly IpcOptions _options;
    private readonly string _localName;
    private readonly IIpcTransport _transport;
    private readonly ConcurrentDictionary<string, IpcMessageHandler> _handlers = new();
    private readonly ConcurrentDictionary<string, List<Func<IpcEnvelope, CancellationToken, Task>>> _eventHandlers = new();
    private readonly ConcurrentDictionary<string, IpcConnection> _connections = new();
    private readonly object _connGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    private Task? _acceptLoop;
    private int _disposed;

    // Diagnostics counters.
    private long _messagesReceived;
    private long _messagesSent;
    private long _requestsSent;
    private long _errors;

    internal IpcHub(IpcOptions options, string localName)
    {
        _options = options;
        _localName = localName;
        _transport = IpcTransportFactory.Create(options);
    }

    /// <summary>The process-wide hub instance (lazily initialized).</summary>
    public static IpcHub Current
    {
        get
        {
            if (_current is null)
            {
                lock (_initGate)
                {
                    _current ??= CreateFromConfig("host");
                }
            }
            return _current;
        }
    }

    /// <summary>Local endpoint identity (worker name or "host").</summary>
    public string LocalName => _localName;

    /// <summary>Resolved IPC options.</summary>
    public IpcOptions Options => _options;

    /// <summary>Number of live outbound connections.</summary>
    public int ConnectionCount => _connections.Count;

    internal static IpcHub CreateFromConfig(string localName)
    {
        var options = new IpcOptions();
        var section = Config.Root?.GetSection("Ipc");
        if (section is not null)
            section.Bind(options);

        if (string.IsNullOrWhiteSpace(options.ApplicationName) || options.ApplicationName == "websiter-app")
        {
            options.ApplicationName = Config.Get("application:name", "websiter-app");
        }

        var appName = Environment.GetEnvironmentVariable("WEBSITER_APP");
        if (!string.IsNullOrWhiteSpace(appName))
            options.ApplicationName = appName;

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            var envDir = Environment.GetEnvironmentVariable("WEBSITER_IPC_DIR");
            if (!string.IsNullOrWhiteSpace(envDir)) options.Directory = envDir;
        }

        return new IpcHub(options, localName);
    }

    /// <summary>Initializes (or replaces) the process-wide hub for a worker.</summary>
    public static IpcHub Initialize(string localName)
    {
        lock (_initGate)
        {
            if (_current is not null)
            {
                if (_current._localName == localName) return _current;

                // If the previous hub was never started (e.g. handlers were
                // registered via IPC.* before the worker identity was known),
                // carry its registrations over to the new hub.
                IpcHub? replacement = null;
                if (_current._acceptLoop is null)
                {
                    replacement = CreateFromConfig(localName);
                    foreach (var (action, handler) in _current._handlers)
                        replacement._handlers[action] = handler;
                    foreach (var (topic, handlers) in _current._eventHandlers)
                        replacement._eventHandlers[topic] = handlers;
                }

                _current.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _current = replacement ?? CreateFromConfig(localName);
                return _current;
            }
            _current = CreateFromConfig(localName);
            return _current;
        }
    }

    /// <summary>Returns the current hub, initializing it for the given name if needed (test hook).</summary>
    internal static IpcHub EnsureInitialized(string localName)
    {
        if (_current is not null) return _current;
        lock (_initGate)
        {
            _current ??= CreateFromConfig(localName);
            return _current;
        }
    }

    /// <summary>Disposes and clears the current hub (used by CLI sessions and tests).</summary>
    public static void ResetForTests()
    {
        lock (_initGate)
        {
            _current?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _current = null;
        }
    }

    /// <summary>Endpoint logical name for a worker in this application.</summary>
    public string EndpointName(string worker) => $"{_options.ApplicationName}.{worker}";

    /// <summary>Starts listening for inbound connections.</summary>
    public async Task StartServerAsync(CancellationToken ct = default)
    {
        var endpoint = EndpointName(_localName);
        await _transport.ListenAsync(endpoint, ct).ConfigureAwait(false);

        RegisterBuiltInHandlers();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_lifetime.Token));
        Log.Info($"IPC listening as '{_localName}' ({DescribeTransport()}).");
    }

    private string DescribeTransport() =>
        _transport is UnixSocketTransport ? $"uds:{_transport.EndpointFor(EndpointName(_localName))}" : $"pipe:{_transport.EndpointFor(EndpointName(_localName))}";

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IIpcByteChannel channel;
            try
            {
                channel = await _transport.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Log.Error($"IPC accept failed: {ex.Message}");
                continue;
            }

            IpcConnection? connection = null;
            connection = new IpcConnection(channel, _options, _localName,
                (env, ct) => DispatchAsync(env, connection!, ct));
            connection.Start();
        }
    }

    private async Task DispatchAsync(IpcEnvelope envelope, IpcConnection inbound, CancellationToken ct)
    {
        Interlocked.Increment(ref _messagesReceived);

        if (envelope.Kind == IpcMessageKind.Event)
        {
            await DispatchEventAsync(envelope, ct).ConfigureAwait(false);
            return;
        }

        // Request or Send: find handler by action name.
        if (_handlers.TryGetValue(envelope.Action, out var handler))
        {
            object? result = null;
            string? error = null;
            var ok = true;
            try
            {
                result = await handler(envelope, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ok = false;
                error = $"{ex.GetType().Name}: {ex.Message}";
                Interlocked.Increment(ref _errors);
                Log.Error($"IPC handler '{envelope.Action}' failed: {ex.Message}");
            }

            if (envelope.Kind == IpcMessageKind.Request)
            {
                var response = new IpcEnvelope
                {
                    Kind = IpcMessageKind.Response,
                    To = envelope.From,
                    Action = envelope.Action,
                    CorrelationId = envelope.CorrelationId ?? envelope.Id,
                    Ok = ok,
                    Error = error,
                    From = _localName,
                    Payload = ok ? ToElement(result) : null,
                };
                // Reply on the same connection the request arrived on so the
                // sender's pending-request correlation resolves.
                try { await inbound.SendAsync(response, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.Error($"Failed to send IPC response for '{envelope.Action}': {ex.Message}"); }
            }
            return;
        }

        if (envelope.Kind == IpcMessageKind.Request)
        {
            var response = new IpcEnvelope
            {
                Kind = IpcMessageKind.Response,
                To = envelope.From,
                Action = envelope.Action,
                CorrelationId = envelope.CorrelationId ?? envelope.Id,
                Ok = false,
                Error = $"No IPC handler registered for action '{envelope.Action}'.",
                From = _localName,
            };
            Interlocked.Increment(ref _errors);
            try { await inbound.SendAsync(response, ct).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error($"Failed to send IPC error response for '{envelope.Action}': {ex.Message}"); }
        }
        else
        {
            Log.Warn($"No IPC handler for action '{envelope.Action}' (send ignored).");
        }
    }

    private async Task DispatchEventAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        if (_eventHandlers.TryGetValue(envelope.Action, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                try { await handler(envelope, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.Error($"IPC event handler for '{envelope.Action}' failed: {ex.Message}"); }
            }
        }
    }

    private static JsonElement? ToElement(object? value) => value switch
    {
        null => null,
        JsonElement e => e,
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>Registers a handler for request/send actions.</summary>
    public void RegisterHandler(string action, IpcMessageHandler handler)
        => _handlers[action] = handler;

    /// <summary>Removes a previously registered handler.</summary>
    public bool UnregisterHandler(string action) => _handlers.TryRemove(action, out _);

    /// <summary>Subscribes to events published to <paramref name="topic"/> (or "*" for all).</summary>
    public void Subscribe(string topic, Func<IpcEnvelope, CancellationToken, Task> handler)
    {
        var list = _eventHandlers.GetOrAdd(topic, _ => new List<Func<IpcEnvelope, CancellationToken, Task>>());
        lock (list) list.Add(handler);
    }

    /// <summary>Lists subscribed topics (for registry publication).</summary>
    public IReadOnlyList<string> Subscriptions => _eventHandlers.Keys.ToList();

    /// <summary>Opens (or reuses) a connection to a worker.</summary>
    internal async Task<IpcConnection> GetConnectionAsync(string worker, CancellationToken ct = default)
    {
        var key = EndpointName(worker);
        if (_connections.TryGetValue(key, out var existing) && existing.IsConnected)
            return existing;

        lock (_connGate)
        {
            if (_connections.TryGetValue(key, out existing) && existing.IsConnected)
                return existing;
        }

        var channel = await _transport.ConnectAsync(key, ct).ConfigureAwait(false);
        IpcConnection? connection = null;
        connection = new IpcConnection(channel, _options, worker,
            (env, innerCt) => DispatchAsync(env, connection!, innerCt));
        connection.Start();

        lock (_connGate)
        {
            if (_connections.TryGetValue(key, out var raced) && raced.IsConnected)
            {
                _ = connection.DisposeAsync();
                return raced;
            }
            _connections[key] = connection;
        }
        return connection;
    }

    /// <summary>Drops a cached connection (e.g. after a failure).</summary>
    public void DropConnection(string worker)
    {
        var key = EndpointName(worker);
        if (_connections.TryRemove(key, out var connection))
            _ = connection.DisposeAsync();
    }

    /// <summary>Sends a one-way message to a worker.</summary>
    public async Task SendAsync(string worker, string action, object? payload = null, CancellationToken ct = default)
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Send, worker, action, payload);
        envelope.From = _localName;
        var connection = await GetConnectionWithRetryAsync(worker, ct).ConfigureAwait(false);
        await connection.SendAsync(envelope, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _messagesSent);
    }

    /// <summary>Sends a request and awaits the response envelope.</summary>
    public async Task<IpcEnvelope> RequestAsync(string worker, string action, object? payload = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Request, worker, action, payload);
        envelope.From = _localName;

        var connection = await GetConnectionWithRetryAsync(worker, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _requestsSent);
        var response = await connection.RequestAsync(envelope, timeout ?? _options.RequestTimeout, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _messagesSent);
        return response;
    }

    /// <summary>Sends a request and deserializes the response payload to <typeparamref name="T"/>.</summary>
    public async Task<T?> RequestAsync<T>(string worker, string action, object? payload = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var response = await RequestAsync(worker, action, payload, timeout, ct).ConfigureAwait(false);
        if (!response.Ok)
            throw new IpcException($"IPC request '{action}' to '{worker}' failed: {response.Error ?? "unknown error"}");
        return response.GetPayload<T>();
    }

    /// <summary>Sends a directed event to a single worker.</summary>
    public async Task SendEventAsync(string worker, string topic, object? payload = null, CancellationToken ct = default)
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Event, worker, topic, payload);
        envelope.From = _localName;
        var connection = await GetConnectionWithRetryAsync(worker, ct).ConfigureAwait(false);
        await connection.SendAsync(envelope, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _messagesSent);
    }

    /// <summary>
    /// Publishes an event to all subscribers of <paramref name="topic"/> across
    /// the application (local handlers invoked directly; remote workers via IPC).
    /// Delivery is at-least-once; subscribers must tolerate duplicates.
    /// </summary>
    public async Task PublishAsync(string topic, object? payload = null, CancellationToken ct = default)
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Event, "*", topic, payload);
        envelope.From = _localName;
        await DispatchEventAsync(envelope, ct).ConfigureAwait(false);

        foreach (var registration in IpcRegistry.ReadAll(_options))
        {
            if (string.Equals(registration.Worker, _localName, StringComparison.Ordinal)) continue;
            if (!registration.Subscriptions.Contains("*") && !registration.Subscriptions.Contains(topic)) continue;

            try
            {
                var directed = IpcEnvelope.Create(IpcMessageKind.Event, registration.Worker, topic, payload);
                directed.From = _localName;
                var connection = await GetConnectionAsync(registration.Worker, ct).ConfigureAwait(false);
                await connection.SendAsync(directed, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _messagesSent);
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to publish event '{topic}' to '{registration.Worker}': {ex.Message}");
            }
        }
    }

    private async Task<IpcConnection> GetConnectionWithRetryAsync(string worker, CancellationToken ct)
    {
        try
        {
            return await GetConnectionAsync(worker, ct).ConfigureAwait(false);
        }
        catch (IpcException)
        {
            DropConnection(worker);
            // Single retry for transient accept-loop races.
            await Task.Delay(50, ct).ConfigureAwait(false);
            return await GetConnectionAsync(worker, ct).ConfigureAwait(false);
        }
    }

    private void RegisterBuiltInHandlers()
    {
        _handlers["$ping"] = (_, _) => Task.FromResult<object?>(new { pong = true, worker = _localName, utc = DateTimeOffset.UtcNow });
        _handlers["$status"] = (_, _) => Task.FromResult<object?>(new
        {
            worker = _localName,
            pid = Environment.ProcessId,
            startedUtc = _startedUtc,
            uptimeSeconds = (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds,
            subscriptions = Subscriptions,
            connections = _connections.Count,
            messagesReceived = Interlocked.Read(ref _messagesReceived),
            messagesSent = Interlocked.Read(ref _messagesSent),
            requestsSent = Interlocked.Read(ref _requestsSent),
            errors = Interlocked.Read(ref _errors),
        });
    }

    /// <summary>Snapshot of hub diagnostics.</summary>
    public object GetDiagnostics() => new
    {
        localName = _localName,
        application = _options.ApplicationName,
        transport = DescribeTransport(),
        connections = _connections.Count,
        handlers = _handlers.Count,
        subscriptions = Subscriptions,
        messagesReceived = Interlocked.Read(ref _messagesReceived),
        messagesSent = Interlocked.Read(ref _messagesSent),
        requestsSent = Interlocked.Read(ref _requestsSent),
        errors = Interlocked.Read(ref _errors),
        uptimeSeconds = (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds,
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        IpcRegistry.Unregister(_options, _localName);

        foreach (var connection in _connections.Values)
            await connection.DisposeAsync().ConfigureAwait(false);
        _connections.Clear();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
