using System.Text.Json;
using Websiter.Workers;

namespace Websiter.CLI.Commands;

/// <summary>`websiter workers ...` — list/start/stop/restart/logs.</summary>
public static class WorkersCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) throw new Program.CliUsageException(
            "Usage: websiter workers <list|start|stop|restart|logs> [name] [-f]");

        var verb = args[0].ToLowerInvariant();
        var rest = args[1..];
        var root = CliSupport.FindRoot(rest);

        return verb switch
        {
            "list" => await ListAsync(root, rest),
            "start" or "stop" or "restart" => await ControlAsync(root, verb, rest),
            "logs" => await LogsAsync(root, rest),
            _ => throw new Program.CliUsageException($"Unknown workers subcommand '{verb}'."),
        };
    }

    private static async Task<int> ListAsync(string root, string[] args)
    {
        // Prefer live status from the running app via IPC.
        var live = await TryGetLiveStatusAsync(root).ConfigureAwait(false);
        if (live is not null)
        {
            PrintTable(live);
            return 0;
        }

        // Fallback: discovered workers + last known state file.
        var discovered = WorkerDiscovery.DiscoverFromFiles(Path.Combine(root, "Workers")).ToList();
        if (discovered.Count == 0)
        {
            Terminal.Warn("No workers found (no Workers/*.cs and app not running).");
            return 0;
        }

        Terminal.Line();
        Terminal.Bold("WORKER".PadRight(24) + "STATE".PadRight(12) + "PID".PadRight(8) + "RESTARTS");
        Terminal.Line(new string('-', 60));
        foreach (var def in discovered.OrderBy(d => d.Name))
        {
            var state = ReadStateFile(root, def.Name);
            var status = state?.Status ?? "stopped";
            var pid = state?.Pid?.ToString() ?? "-";
            Terminal.Line($"{def.Name,-24}{status,-12}{pid,-8}-");
        }
        Terminal.Line();
        Terminal.Dim("App is not running — states are from the last session.");
        return 0;
    }

    private static async Task<int> ControlAsync(string root, string verb, string[] args)
    {
        var name = CliSupport.PositionalArgs(args).FirstOrDefault();
        if (name is null) throw new Program.CliUsageException($"Missing worker name. Usage: websiter workers {verb} <name>");

        await using var ipc = await CliSupport.OpenIpcAsync(root).ConfigureAwait(false);
        var action = verb switch
        {
            "start" => "$worker-start",
            "stop" => "$worker-stop",
            "restart" => "$worker-restart",
            _ => throw new Program.CliUsageException($"Unknown workers verb '{verb}'."),
        };

        try
        {
            var result = await ipc.RequestAsync<JsonElement>("host", action, new { name },
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                Terminal.Success($"Worker '{name}' {verb} requested.");
                return 0;
            }
            Terminal.Error($"Worker control failed: {result}");
            return 1;
        }
        catch (Exception ex)
        {
            Terminal.Error($"Cannot reach the application. Is it running? ({ex.Message})");
            Terminal.Dim("Start it with 'websiter start' or 'websiter dev'.");
            return 1;
        }
    }

    private static async Task<int> LogsAsync(string root, string[] args)
    {
        var name = CliSupport.PositionalArgs(args).FirstOrDefault();
        var follow = args.Contains("-f") || args.Contains("--follow");
        if (name is null) throw new Program.CliUsageException("Missing worker name. Usage: websiter workers logs <name> [-f]");

        var logFile = Path.Combine(Paths.StateDirFor(root), "logs", $"worker-{name}.log");
        if (!File.Exists(logFile))
        {
            Terminal.Error($"No log file for worker '{name}' ({logFile}).");
            return 1;
        }

        if (!follow)
        {
            var lines = File.ReadAllLines(logFile);
            var tail = lines.TakeLast(200);
            foreach (var line in tail) Terminal.Line(line);
            if (lines.Length > 200) Terminal.Dim($"... ({lines.Length - 200} earlier lines)");
            return 0;
        }

        // Follow mode: print existing then stream appends.
        using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        fs.Seek(0, SeekOrigin.End);
        var pos = fs.Position;
        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                await Task.Delay(500).ConfigureAwait(false);
                continue;
            }
            Terminal.Line(line);
        }
    }

    private static async Task<List<WorkerStatusDto>?> TryGetLiveStatusAsync(string root)
    {
        try
        {
            await using var ipc = await CliSupport.OpenIpcAsync(root).ConfigureAwait(false);
            var raw = await ipc.RequestAsync<JsonElement>("host", "$workers",
                timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (raw.ValueKind != JsonValueKind.Array) return null;
            return raw.Deserialize<List<WorkerStatusDto>>();
        }
        catch { return null; }
    }

    private static void PrintTable(List<WorkerStatusDto> workers)
    {
        Terminal.Line();
        Terminal.Bold("WORKER".PadRight(24) + "STATE".PadRight(12) + "PID".PadRight(8) + "UPTIME".PadRight(10) + "RESTARTS");
        Terminal.Line(new string('-', 68));
        foreach (var w in workers.OrderBy(w => w.Name))
        {
            var uptime = w.UptimeSeconds is double u
                ? u < 60 ? $"{u:0}s" : u < 3600 ? $"{u / 60:0}m" : $"{u / 3600:0.0}h"
                : "-";
            Terminal.Line($"{w.Name,-24}{w.State,-12}{(w.Pid?.ToString() ?? "-"),-8}{uptime,-10}{w.RestartCount}");
        }
        Terminal.Line();
    }

    private sealed class WorkerStateFile
    {
        public string? Status { get; set; }
        public int? Pid { get; set; }
    }

    private static WorkerStateFile? ReadStateFile(string root, string worker)
    {
        var file = Path.Combine(Paths.StateDirFor(root), $"worker-{worker}.json");
        try
        {
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<WorkerStateFile>(File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private sealed class WorkerStatusDto
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public int? Pid { get; set; }
        public double? UptimeSeconds { get; set; }
        public int RestartCount { get; set; }
    }
}
