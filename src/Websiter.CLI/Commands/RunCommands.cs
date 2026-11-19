using Websiter.Workers;
namespace Websiter.CLI.Commands;

/// <summary>`websiter start|stop|restart` — app process lifecycle.</summary>
public static class RunCommands
{
    public static async Task<int> RunAsync(string[] args, string verb)
    {
        var root = CliSupport.RequireProjectRoot(args);

        switch (verb)
        {
            case "start":
            {
                if (CliSupport.AppPid(root) is not null)
                {
                    Terminal.Warn("Application is already running.");
                    return 0;
                }
                if (AppLayout.FindApplicationAssembly(root) is null)
                {
                    Terminal.Error("No built application found. Run 'websiter build' or 'websiter dev' first.");
                    return 1;
                }

                var process = CliSupport.StartApp(root);
                Terminal.Info($"Starting application (pid {process.Id})...");

                // Wait for health endpoint or process exit.
                var url = ReadServerUrl(root);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                var healthy = false;
                while (DateTime.UtcNow < deadline)
                {
                    if (process.HasExited)
                    {
                        Terminal.Error($"Application exited during startup (code {process.ExitCode}). Check .websiter/logs/app.log");
                        return 1;
                    }
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                        var response = await http.GetAsync($"{url}/__websiter/health").ConfigureAwait(false);
                        if (response.IsSuccessStatusCode) { healthy = true; break; }
                    }
                    catch { }
                    await Task.Delay(400).ConfigureAwait(false);
                }

                if (healthy)
                    Terminal.Success($"Application running at {url} (pid {process.Id})");
                else
                    Terminal.Warn($"Application started (pid {process.Id}) but health check did not respond yet. URL: {url}");
                Terminal.Dim($"Logs: .websiter/logs/app.log — stop with 'websiter stop'");
                return 0;
            }

            case "stop":
            {
                var stopped = await CliSupport.StopAppAsync(root).ConfigureAwait(false);
                if (stopped) Terminal.Success("Application stopped.");
                else Terminal.Warn("Application is not running.");
                return 0;
            }

            case "restart":
            {
                await CliSupport.StopAppAsync(root).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                return await RunAsync(args, "start").ConfigureAwait(false);
            }

            default:
                throw new Program.CliUsageException($"Unknown command '{verb}'.");
        }
    }

    /// <summary>Resolves the configured server URL.</summary>
    public static string ReadServerUrl(string root)
    {
        var cfg = CliSupport.LoadConfigFile(root);
        var port = cfg["server"]?["port"]?.GetValue<int>() ?? 8080;
        var host = cfg["server"]?["host"]?.GetValue<string>() ?? "localhost";
        var https = cfg["server"]?["useHttps"]?.GetValue<bool>() ?? false;
        if (host is "0.0.0.0" or "*" or "::") host = "localhost";
        return $"{(https ? "https" : "http")}://{host}:{port}";
    }
}

public static class StartCommand
{
    public static Task<int> RunAsync(string[] args) => RunCommands.RunAsync(args, "start");
}

public static class StopCommand
{
    public static Task<int> RunAsync(string[] args) => RunCommands.RunAsync(args, "stop");
}

public static class RestartCommand
{
    public static Task<int> RunAsync(string[] args) => RunCommands.RunAsync(args, "restart");
}
