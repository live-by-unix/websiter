using System.Text.Json;
using System.Threading.Channels;

namespace Websiter.Ipc;

/// <summary>Handler delegate for incoming request/send/event envelopes.</summary>
public delegate Task<object?> IpcMessageHandler(IpcEnvelope envelope, CancellationToken ct);

/// <summary>
/// A single bidirectional IPC connection. Owns the write pump (bounded channel
/// for backpressure), the read pump, and pending request correlation.
/// </summary>
internal sealed class IpcConnection : IAsyncDisposable
{
    private readonly IIpcByteChannel _channel;
    private readonly IpcOptions _options;
    private readonly Channel<IpcEnvelope> _outbound;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, TaskCompletionSource<IpcEnvelope>> _pending = new();
    private readonly object _pendingGate = new();
    private readonly Func<IpcEnvelope, CancellationToken, Task> _dispatch;
    private readonly string _peer;

    private Task? _writerTask;
    private Task? _readerTask;
    private int _disposed;

    public IpcConnection(IIpcByteChannel channel, IpcOptions options, string peer,
        Func<IpcEnvelope, CancellationToken, Task> dispatch)
    {
        _channel = channel;
        _options = options;
        _peer = peer;
        _dispatch = dispatch;
        _outbound = Channel.CreateBounded<IpcEnvelope>(new BoundedChannelOptions(options.SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public string Peer => _peer;
    public bool IsConnected => _channel.IsConnected && Volatile.Read(ref _disposed) == 0;

    /// <summary>Starts the read/write pumps. Call once after construction.</summary>
    public void Start()
    {
        _writerTask = Task.Run(WriteLoopAsync);
        _readerTask = Task.Run(ReadLoopAsync);
    }

    /// <summary>Sends an envelope, applying backpressure via the bounded queue.</summary>
    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _outbound.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
    }

    /// <summary>Sends a request and awaits the correlated response.</summary>
    public async Task<IpcEnvelope> RequestAsync(IpcEnvelope request, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var corr = Guid.NewGuid().ToString("N");
        request.CorrelationId = corr;
        request.Kind = IpcMessageKind.Request;

        lock (_pendingGate)
        {
            _pending[corr] = tcs;
        }

        try
        {
            await SendAsync(request, ct).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await using var reg = timeoutCts.Token.Register(() =>
                tcs.TrySetCanceled(timeoutCts.Token));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_pendingGate)
            {
                _pending.Remove(corr);
            }
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var envelope in _outbound.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                var bytes = envelope.ToBytes();
                await _channel.WriteAsync(bytes, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error($"IPC write loop failed on '{_peer}': {ex.Message}");
        }
        finally
        {
            FailAllPending(new IpcException($"IPC connection to '{_peer}' closed."));
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                byte[]? payload;
                try
                {
                    payload = await _channel.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error($"IPC read failed on '{_peer}': {ex.Message}");
                    break;
                }

                if (payload is null) break; // peer closed

                IpcEnvelope envelope;
                try
                {
                    envelope = IpcEnvelope.FromBytes(payload);
                }
                catch (Exception ex)
                {
                    Log.Error($"IPC envelope rejected from '{_peer}': {ex.Message}");
                    continue;
                }

                if (envelope.Kind == IpcMessageKind.Response && envelope.CorrelationId is not null)
                {
                    TaskCompletionSource<IpcEnvelope>? tcs;
                    lock (_pendingGate)
                    {
                        if (_pending.Remove(envelope.CorrelationId, out tcs))
                            tcs.TrySetResult(envelope);
                    }
                    continue;
                }

                // Dispatch inbound request/send/event without blocking the read loop.
                _ = Task.Run(async () =>
                {
                    try { await _dispatch(envelope, _lifetime.Token).ConfigureAwait(false); }
                    catch (Exception ex) { Log.Error($"IPC handler failed for '{envelope.Action}' from '{_peer}': {ex.Message}"); }
                });
            }
        }
        finally
        {
            FailAllPending(new IpcException($"IPC connection to '{_peer}' closed."));
            _outbound.Writer.TryComplete();
        }
    }

    private void FailAllPending(Exception exception)
    {
        List<TaskCompletionSource<IpcEnvelope>> pending;
        lock (_pendingGate)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }
        foreach (var tcs in pending)
            tcs.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _outbound.Writer.TryComplete();
        FailAllPending(new IpcException($"IPC connection to '{_peer}' disposed."));

        try { if (_writerTask is not null) await _writerTask.ConfigureAwait(false); } catch { }
        try { if (_readerTask is not null) await _readerTask.ConfigureAwait(false); } catch { }
        try { await _channel.DisposeAsync().ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
    }
}
