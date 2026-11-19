using System.Text.Json;
using Websiter.Ipc;
using Xunit;

namespace Websiter.IPC.Tests;

public class EnvelopeTests
{
    [Fact]
    public void RoundTrip_PreservesFields()
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Request, "workerB", "GetUsers",
            new { filter = "active" });
        envelope.From = "workerA";
        envelope.CorrelationId = "corr-123";

        var bytes = envelope.ToBytes();
        var restored = IpcEnvelope.FromBytes(bytes);

        Assert.Equal(IpcProtocol.Version, restored.Version);
        Assert.Equal("workerB", restored.To);
        Assert.Equal("GetUsers", restored.Action);
        Assert.Equal("workerA", restored.From);
        Assert.Equal("corr-123", restored.CorrelationId);
        Assert.Equal(IpcMessageKind.Request, restored.Kind);
        Assert.Equal("active", restored.GetPayload<JsonElement>().GetProperty("filter").GetString());
    }

    [Fact]
    public void RejectsUnsupportedVersion()
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Send, "x", "y");
        envelope.Version = 999;
        var bytes = envelope.ToBytes();
        Assert.Throws<IpcException>(() => IpcEnvelope.FromBytes(bytes));
    }

    [Fact]
    public void Payload_DeserializesToTypedObject()
    {
        var envelope = IpcEnvelope.Create(IpcMessageKind.Response, "a", "b",
            new UserList { Users = new[] { "alice", "bob" } });
        var restored = IpcEnvelope.FromBytes(envelope.ToBytes());
        var result = restored.GetPayload<UserList>();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Users.Length);
    }

    private sealed class UserList
    {
        public string[] Users { get; set; } = Array.Empty<string>();
    }
}

public class FramingTests
{
    [Fact]
    public async Task WriteRead_RoundTripsPayload()
    {
        var options = new IpcOptions { MaxMessageBytes = 1024 };
        var (client, server) = await CreatePipe();

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        await IpcFraming.WriteAsync(client, payload, options, CancellationToken.None);
        var read = await IpcFraming.ReadAsync(server, options, CancellationToken.None);

        Assert.Equal(payload, read);
        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Read_ReturnsNullOnCleanClose()
    {
        var options = new IpcOptions();
        var (client, server) = await CreatePipe();
        await client.DisposeAsync();
        var read = await IpcFraming.ReadAsync(server, options, CancellationToken.None);
        Assert.Null(read);
        await server.DisposeAsync();
    }

    private static async Task<(System.IO.Stream, System.IO.Stream)> CreatePipe()
    {
        // Loopback TCP pair gives a real duplex byte stream for framing tests.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var clientSocket = new System.Net.Sockets.TcpClient();
        var connectTask = clientSocket.ConnectAsync("127.0.0.1", port);
        var serverSocket = await listener.AcceptTcpClientAsync();
        await connectTask;
        listener.Stop();
        return (clientSocket.GetStream(), serverSocket.GetStream());
    }
}
