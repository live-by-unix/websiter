using Websiter;

/// <summary>
/// Independent OS process handling authentication. Demonstrates
/// worker-to-worker IPC: AuthWorker calls DatabaseWorker over IPC.
/// </summary>
public class AuthWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Authenticate", async (message, ct) =>
        {
            var username = message?.GetProperty("username").GetString();
            var password = message?.GetProperty("password").GetString();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return new { authenticated = false, error = "missing credentials" };

            // Ask DatabaseWorker whether the user exists (worker → worker IPC).
            var user = await IPC.RequestAsync<object>("DatabaseWorker", "GetUser",
                new { username }, timeout: TimeSpan.FromSeconds(5), ct: ct);

            // Demo-only check; use a real credential store in production.
            var ok = password == "demo";
            return new
            {
                authenticated = ok,
                username,
                token = ok ? Guid.NewGuid().ToString("N") : null,
            };
        });

        await RunAsync(cancellationToken);
    }
}
