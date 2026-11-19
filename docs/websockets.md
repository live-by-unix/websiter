# WebSockets

Map WebSocket endpoints with `MapWebSocket`:

```csharp
app.UseWebSockets();

app.MapWebSocket("/ws/live", async (socket, ctx, ct) =>
{
    var buffer = new byte[4096];
    while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        var result = await socket.ReceiveAsync(buffer, ct);
        if (result.MessageType == WebSocketMessageType.Close) break;
        await socket.SendAsync(buffer.AsMemory(0, result.Count),
            WebSocketMessageType.Text, true, ct);
    }
});
```

Notes:

- `UseWebSockets()` must run before routes are mapped.
- Upgrade requests that don't match a `MapWebSocket` pattern get `400`.
- For worker-backed sockets, bridge inside the handler via `IPC` (e.g. subscribe to events and forward to the socket).
- Keep handlers cancellable; sockets close on client disconnect or shutdown.
