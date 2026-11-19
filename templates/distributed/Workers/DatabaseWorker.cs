using Websiter;

/// <summary>
/// Independent OS process owning data access. The host and other workers
/// reach it exclusively through IPC — no shared memory, no HTTP server.
/// </summary>
public class DatabaseWorker : Worker
{
    // Simulated datastore; replace with a real provider.
    private static readonly string[] _users = { "alice", "bob", "carol" };

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("GetUsers", (message, ct) =>
            Task.FromResult<object?>(new
            {
                users = _users.Select(u => new { username = u }),
                count = _users.Length,
            }));

        RegisterHandler("GetUser", (message, ct) =>
        {
            var name = message?.GetProperty("username").GetString();
            return Task.FromResult<object?>(name is not null && _users.Contains(name)
                ? new { found = true, username = name }
                : new { found = false });
        });

        await RunAsync(cancellationToken);
    }
}
