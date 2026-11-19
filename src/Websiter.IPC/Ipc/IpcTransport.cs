using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Websiter.Ipc;

/// <summary>
/// Transport abstraction for IPC. Implementations provide OS-native
/// inter-process byte channels: Unix domain sockets on Unix platforms and
/// named pipes on Windows. New transports can be added by implementing
/// this interface.
/// </summary>
internal interface IIpcTransport : IAsyncDisposable
{
    /// <summary>Binds/creates the listening endpoint for <paramref name="logicalName"/> (server side).</summary>
    Task ListenAsync(string logicalName, CancellationToken ct);

    /// <summary>Waits for and accepts the next inbound connection (server side).</summary>
    Task<IIpcByteChannel> AcceptAsync(CancellationToken ct);

    /// <summary>Opens a connection to the endpoint for <paramref name="logicalName"/>.</summary>
    Task<IIpcByteChannel> ConnectAsync(string logicalName, CancellationToken ct);

    /// <summary>Physical endpoint identifier for a logical worker name.</summary>
    string EndpointFor(string logicalName);
}

internal static class IpcTransportFactory
{
    public static IIpcTransport Create(IpcOptions options)
    {
        var kind = options.Transport?.ToLowerInvariant() ?? "auto";
        var isWindows = OperatingSystem.IsWindows();

        return kind switch
        {
            "namedpipe" => new NamedPipeTransport(options),
            "uds" or "unix" => new UnixSocketTransport(options),
            "auto" => isWindows ? new NamedPipeTransport(options) : new UnixSocketTransport(options),
            _ => throw new IpcException($"Unknown IPC transport '{options.Transport}'. Valid values: auto, namedpipe, uds."),
        };
    }
}

internal static class IpcEndpointNaming
{
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}

/// <summary>Unix domain socket transport (Linux, macOS).</summary>
internal sealed class UnixSocketTransport : IIpcTransport
{
    private readonly IpcOptions _options;
    private readonly string _dir;
    private Socket? _listener;

    public UnixSocketTransport(IpcOptions options)
    {
        _options = options;
        _dir = options.Directory ?? Path.Combine(Paths.StateDir, "ipc");
        Directory.CreateDirectory(_dir);
        RestrictDirectoryPermissions(_dir);
    }

    private static void RestrictDirectoryPermissions(string dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { /* best effort on filesystems without unix modes */ }
        }
    }

    // macOS sun_path is ~104 bytes; Linux ~108. Stay comfortably under both.
    private const int MaxUnixSocketPathLength = 100;

    public string EndpointFor(string logicalName)
    {
        var candidate = Path.Combine(_dir, IpcEndpointNaming.Sanitize(logicalName) + ".sock");
        if (candidate.Length <= MaxUnixSocketPathLength)
            return candidate;

        // Deeply nested working directories can push the socket path past the
        // OS limit. Fall back to a deterministic short path derived from a hash
        // of the full candidate, so every process resolves the same endpoint.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)))[..16].ToLowerInvariant();
        var fallbackDir = Path.Combine(Path.GetTempPath(), "websiter-ipc");
        Directory.CreateDirectory(fallbackDir);
        RestrictDirectoryPermissions(fallbackDir);
        return Path.Combine(fallbackDir, hash + ".sock");
    }

    public async Task ListenAsync(string logicalName, CancellationToken ct)
    {
        var path = EndpointFor(logicalName);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* stale socket */ }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        socket.Listen(16);
        _listener = socket;

        if (_options.RestrictPermissions && !OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best effort */ }
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<IIpcByteChannel> AcceptAsync(CancellationToken ct)
    {
        var listener = _listener ?? throw new IpcException("Unix socket transport is not listening.");
        var socket = await listener.AcceptAsync(ct).ConfigureAwait(false);
        return new StreamByteChannel(new NetworkStream(socket, ownsSocket: true), _options);
    }

    public async Task<IIpcByteChannel> ConnectAsync(string logicalName, CancellationToken ct)
    {
        var path = EndpointFor(logicalName);
        if (!File.Exists(path))
            throw new IpcException($"No IPC endpoint found for '{logicalName}' at {path}. Is the worker running?");

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ConnectTimeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new IpcException($"Failed to connect to IPC endpoint '{logicalName}': {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new IpcException($"Timed out connecting to IPC endpoint '{logicalName}' after {_options.ConnectTimeout}.");
        }
        return new StreamByteChannel(new NetworkStream(socket, ownsSocket: true), _options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is not null)
        {
            try { _listener.Dispose(); } catch { }
            _listener = null;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>Named pipe transport (Windows; also usable elsewhere for testing).</summary>
internal sealed class NamedPipeTransport : IIpcTransport
{
    private readonly IpcOptions _options;
    private string _pipeName = string.Empty;

    public NamedPipeTransport(IpcOptions options) => _options = options;

    public string EndpointFor(string logicalName)
    {
        var safe = new string(logicalName.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        return $"websiter.{_options.ApplicationName}.{safe}";
    }

    public Task ListenAsync(string logicalName, CancellationToken ct)
    {
        _pipeName = EndpointFor(logicalName);
        return Task.CompletedTask;
    }

    public async Task<IIpcByteChannel> AcceptAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_pipeName))
            throw new IpcException("Named pipe transport is not listening.");

        // A fresh server instance per client allows unlimited sequential clients.
        var server = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new StreamByteChannel(server, _options);
    }

    public async Task<IIpcByteChannel> ConnectAsync(string logicalName, CancellationToken ct)
    {
        var pipeName = EndpointFor(logicalName);
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ConnectTimeout);
            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new IpcException($"Timed out connecting to IPC endpoint '{logicalName}' after {_options.ConnectTimeout}.");
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new IpcException($"Failed to connect to IPC endpoint '{logicalName}': {ex.Message}", ex);
        }
        return new StreamByteChannel(client, _options);
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask.ConfigureAwait(false);
}
