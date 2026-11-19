using System.Net.Sockets;
using System.Text;

namespace Websiter.Ipc;

/// <summary>
/// Length-prefixed framing: 4-byte big-endian length followed by UTF-8 JSON.
/// Enforces <see cref="IpcOptions.MaxMessageBytes"/>.
/// </summary>
internal static class IpcFraming
{
    public static async Task WriteAsync(Stream stream, byte[] payload, IpcOptions options, CancellationToken ct)
    {
        if (payload.Length > options.MaxMessageBytes)
            throw new IpcException($"IPC message of {payload.Length} bytes exceeds the {options.MaxMessageBytes}-byte limit.");

        var header = new byte[4];
        var len = (uint)payload.Length;
        header[0] = (byte)(len >> 24);
        header[1] = (byte)(len >> 16);
        header[2] = (byte)(len >> 8);
        header[3] = (byte)len;

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadAsync(Stream stream, IpcOptions options, CancellationToken ct)
    {
        var header = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
        if (header is null) return null;

        var len = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
        if (len > options.MaxMessageBytes)
            throw new IpcException($"IPC message of {len} bytes exceeds the {options.MaxMessageBytes}-byte limit; connection aborted.");
        if (len == 0) return Array.Empty<byte>();

        return await ReadExactAsync(stream, (int)len, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? null : throw new EndOfStreamException("IPC peer closed the connection mid-message.");
            offset += read;
        }
        return buffer;
    }
}

/// <summary>
/// Raw bidirectional byte pipe abstraction over either a Unix domain socket
/// or a named pipe stream.
/// </summary>
internal interface IIpcByteChannel : IAsyncDisposable
{
    Task WriteAsync(byte[] data, CancellationToken ct);
    Task<byte[]?> ReadAsync(CancellationToken ct);
    bool IsConnected { get; }
}

internal sealed class StreamByteChannel : IIpcByteChannel
{
    private readonly Stream _stream;
    private readonly IpcOptions _options;

    public StreamByteChannel(Stream stream, IpcOptions options)
    {
        _stream = stream;
        _options = options;
    }

    public bool IsConnected
    {
        get
        {
            try
            {
                return _stream.CanRead && !_stream.IsDisposedOrClosed();
            }
            catch
            {
                return false;
            }
        }
    }

    public Task WriteAsync(byte[] data, CancellationToken ct) => IpcFraming.WriteAsync(_stream, data, _options, ct);

    public Task<byte[]?> ReadAsync(CancellationToken ct) => IpcFraming.ReadAsync(_stream, _options, ct);

    public async ValueTask DisposeAsync()
    {
        try { await _stream.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
    }
}

internal static class StreamExtensions
{
    public static bool IsDisposedOrClosed(this Stream stream) => !stream.CanRead;
}
