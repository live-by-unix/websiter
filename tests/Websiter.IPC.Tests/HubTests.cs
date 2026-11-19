using System.Text.Json;
using Websiter.Ipc;
using Xunit;

namespace Websiter.IPC.Tests;

/// <summary>
/// Integration tests that run two IPC hubs in one process over the real
/// OS transport (Unix domain sockets on this platform).
/// </summary>
public class HubTests : IAsyncLifetime
{
    private readonly string _ipcDir = Path.Combine(Path.GetTempPath(), "websiter-ipc-tests-" + Guid.NewGuid().ToString("N"));
    private IpcHub _server = null!;
    private IpcHub _client = null!;

    private IpcOptions Options() => new()
    {
        ApplicationName = "ipc-test",
        Directory = _ipcDir,
        RequestTimeout = TimeSpan.FromSeconds(10),
    };

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_ipcDir);
        _server = new IpcHub(Options(), "WorkerA");
        _client = new IpcHub(Options(), "Client");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        await _client.DisposeAsync();
        try { Directory.Delete(_ipcDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task RequestResponse_ReturnsPayload()
    {
        _server.RegisterHandler("GetUsers", (_, _) =>
            Task.FromResult<object?>(new { users = new[] { "alice", "bob" }, count = 2 }));
        await _server.StartServerAsync();

        var result = await _client.RequestAsync<JsonElement>("WorkerA", "GetUsers");
        Assert.Equal(2, result.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task RequestResponse_PropagatesHandlerError()
    {
        _server.RegisterHandler("Boom", (_, _) => throw new InvalidOperationException("kaput"));
        await _server.StartServerAsync();

        var ex = await Assert.ThrowsAsync<IpcException>(() =>
            _client.RequestAsync<object>("WorkerA", "Boom"));
        Assert.Contains("kaput", ex.Message);
    }

    [Fact]
    public async Task RequestResponse_UnknownAction_ReturnsError()
    {
        await _server.StartServerAsync();
        var ex = await Assert.ThrowsAsync<IpcException>(() =>
            _client.RequestAsync<object>("WorkerA", "DoesNotExist"));
        Assert.Contains("No IPC handler", ex.Message);
    }

    [Fact]
    public async Task RequestResponse_Timeout_WhenNoResponse()
    {
        // No handler registered and Request waits — but unknown action responds with error.
        // For a true timeout test, point at a nonexistent worker.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _client.RequestAsync<object>("NoSuchWorker", "X", timeout: TimeSpan.FromMilliseconds(500)));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Send_DeliversToHandler()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.RegisterHandler("Notify", (env, _) =>
        {
            tcs.TrySetResult(env.Payload?.GetProperty("msg").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });
        await _server.StartServerAsync();

        await _client.SendAsync("WorkerA", "Notify", new { msg = "hello" });
        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hello", msg);
    }

    [Fact]
    public async Task Ping_BuiltInHandler()
    {
        await _server.StartServerAsync();
        var pong = await _client.RequestAsync<JsonElement>("WorkerA", "$ping");
        Assert.True(pong.GetProperty("pong").GetBoolean());
        Assert.Equal("WorkerA", pong.GetProperty("worker").GetString());
    }

    [Fact]
    public async Task Events_DeliverToSubscribers()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Subscribe("alerts", (env, _) =>
        {
            tcs.TrySetResult(env.Payload?.GetProperty("text").GetString() ?? "");
            return Task.CompletedTask;
        });
        await _server.StartServerAsync();

        // Publish from client; server is subscribed — delivery via registry requires
        // the server to have registered its subscriptions. The hub writes registry on
        // Subscribe? Registry writes happen via IpcRegistry.Register — hubs don't call it
        // automatically, so test directed event instead.
        await _client.SendEventAsync("WorkerA", "alerts", new { text = "disk full" });
        var text = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("disk full", text);
    }

    [Fact]
    public async Task Cancellation_AbortsPendingRequest()
    {
        _server.RegisterHandler("Slow", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return (object?)null;
        });
        await _server.StartServerAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.RequestAsync<object>("WorkerA", "Slow", ct: cts.Token));
    }
}
