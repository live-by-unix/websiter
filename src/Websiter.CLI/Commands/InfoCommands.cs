using System.Text.Json;
using System.Text.Json.Nodes;
using Websiter.Native;
using Websiter.Workers;

namespace Websiter.CLI.Commands;

/// <summary>`websiter routes` — lists registered HTTP routes via the running app.</summary>
public static class RoutesCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        try
        {
            await using var ipc = await CliSupport.OpenIpcAsync(root).ConfigureAwait(false);
            var routes = await ipc.RequestAsync<JsonElement>("host", "$routes",
                timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (routes.ValueKind != JsonValueKind.Array) { Terminal.Error("Unexpected response from host."); return 1; }

            Terminal.Line();
            Terminal.Bold("METHOD".PadRight(8) + "PATTERN".PadRight(40) + "NAME");
            Terminal.Line(new string('-', 70));
            foreach (var r in routes.EnumerateArray())
            {
                Terminal.Line($"{r.GetProperty("method").GetString(),-8}" +
                              $"{r.GetProperty("pattern").GetString(),-40}" +
                              $"{(r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")}");
            }
            Terminal.Line();
            return 0;
        }
        catch (Exception ex)
        {
            Terminal.Error($"Cannot reach the application: {ex.Message}");
            Terminal.Dim("Routes are registered at runtime — start the app with 'websiter start' or 'websiter dev'.");
            return 1;
        }
    }
}

/// <summary>`websiter logs` — tails application and worker logs.</summary>
public static class LogsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var follow = args.Contains("-f") || args.Contains("--follow");
        var target = CliSupport.PositionalArgs(args).FirstOrDefault();

        var logDir = Path.Combine(Paths.StateDirFor(root), "logs");
        if (!Directory.Exists(logDir))
        {
            Terminal.Error("No logs directory found (.websiter/logs). Has the app run?");
            return 1;
        }

        var files = target is null
            ? Directory.GetFiles(logDir, "*.log").OrderBy(f => f).ToList()
            : new List<string> { Path.Combine(logDir, target.EndsWith(".log") ? target : target + ".log") };

        files = files.Where(File.Exists).ToList();
        if (files.Count == 0) { Terminal.Warn("No matching log files."); return 0; }

        foreach (var file in files)
        {
            Terminal.Bold($"--- {Path.GetFileName(file)} ---");
            var lines = File.ReadAllLines(file);
            foreach (var line in lines.TakeLast(100)) Terminal.Line(line);
            if (lines.Length > 100) Terminal.Dim($"... ({lines.Length - 100} earlier lines)");
        }

        if (follow)
        {
            Terminal.Dim("Following (Ctrl+C to stop)...");
            var positions = files.ToDictionary(f => f, f => new FileInfo(f).Length);
            while (true)
            {
                foreach (var file in files)
                {
                    try
                    {
                        var len = new FileInfo(file).Length;
                        if (len > positions[file])
                        {
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            fs.Seek(positions[file], SeekOrigin.Begin);
                            using var reader = new StreamReader(fs);
                            string? line;
                            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
                                Terminal.Line($"{Path.GetFileName(file)}: {line}");
                            positions[file] = len;
                        }
                    }
                    catch { }
                }
                await Task.Delay(700).ConfigureAwait(false);
            }
        }
        return 0;
    }
}

/// <summary>`websiter doctor` — environment and project diagnostics.</summary>
public static class DoctorCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.FindRoot(args);
        var failures = 0;

        void Check(bool ok, string label, string? detail = null, bool required = true)
        {
            if (ok) Terminal.Success(label + (detail is null ? "" : $" — {detail}"));
            else { Terminal.Error(label + (detail is null ? "" : $" — {detail}")); if (required) failures++; }
        }

        Terminal.Bold("Websiter doctor");
        Terminal.Line();

        // .NET SDK
        var dotnet = Run("dotnet", "--version");
        Check(dotnet.ExitCode == 0 && Version.TryParse(dotnet.Output.Trim(), out var v) && v >= new Version(10, 0, 0),
            ".NET SDK 10+", dotnet.Output.Trim());

        // Project
        var isProject = File.Exists(Path.Combine(root, "websiter.json")) || Directory.EnumerateFiles(root, "*.csproj").Any();
        Check(isProject, "Project root", root, required: false);

        if (isProject)
        {
            var csproj = Directory.EnumerateFiles(root, "*.csproj").FirstOrDefault();
            Check(csproj is not null, "Project file", csproj is null ? null : Path.GetFileName(csproj));

            var workersDir = Path.Combine(root, "Workers");
            var workerCount = Directory.Exists(workersDir) ? Directory.GetFiles(workersDir, "*.cs", SearchOption.AllDirectories).Length : 0;
            Terminal.Info($"Workers: {workerCount} source file(s)");

            var componentsDir = Path.Combine(root, "Components");
            var componentCount = Directory.Exists(componentsDir) ? Directory.GetFiles(componentsDir, "*.html", SearchOption.AllDirectories).Length : 0;
            Terminal.Info($"Components: {componentCount}");

            var pagesDir = Path.Combine(root, "Pages");
            var pageCount = Directory.Exists(pagesDir) ? Directory.GetFiles(pagesDir, "*.html", SearchOption.AllDirectories).Length : 0;
            Terminal.Info($"Pages: {pageCount}");

            // Config parse
            var cfgPath = Path.Combine(root, "websiter.json");
            if (File.Exists(cfgPath))
            {
                try { JsonDocument.Parse(File.ReadAllText(cfgPath)); Check(true, "websiter.json valid"); }
                catch (Exception ex) { Check(false, "websiter.json valid", ex.Message); }
            }
            else Terminal.Warn("websiter.json not found (defaults will be used)");

            // Port availability
            var cfg = CliSupport.LoadConfigFile(root);
            var port = cfg["server"]?["port"]?.GetValue<int>() ?? 8080;
            Check(await IsPortFreeAsync(port).ConfigureAwait(false), $"Port {port} available", required: false);

            // IPC dir writable
            var ipcDir = Path.Combine(Paths.StateDirFor(root), "ipc");
            try
            {
                Directory.CreateDirectory(ipcDir);
                var probe = Path.Combine(ipcDir, ".probe");
                File.WriteAllText(probe, "x"); File.Delete(probe);
                Check(true, "IPC directory writable", ipcDir);
            }
            catch (Exception ex) { Check(false, "IPC directory writable", ex.Message); }
        }

        // Git
        Check(Directory.Exists(Path.Combine(root, ".git")), "Git repository", required: false);

        Terminal.Line();
        if (failures == 0) { Terminal.Success("All checks passed."); return 0; }
        Terminal.Error($"{failures} check(s) failed.");
        return 1;
    }

    private static (int ExitCode, string Output) Run(string file, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, outp.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    private static async Task<bool> IsPortFreeAsync(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch { return false; }
    }
}

/// <summary>`websiter config` — shows effective configuration (secrets masked).</summary>
public static class ConfigCommand
{
    private static readonly string[] SecretHints = { "password", "secret", "key", "token", "connectionstring", "credential" };

    public static Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var file = Path.Combine(root, "websiter.json");
        if (!File.Exists(file))
        {
            Terminal.Warn("websiter.json not found — showing defaults.");
        }
        else
        {
            Terminal.Bold("websiter.json:");
            var text = File.ReadAllText(file);
            // Mask likely secrets crudely for display.
            try
            {
                var node = JsonNode.Parse(text);
                MaskSecrets(node);
                Terminal.Line(node!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { Terminal.Line(text); }
            Terminal.Line();
        }

        Terminal.Bold("Effective settings:");
        Terminal.Line($"  Root:          {root}");
        Terminal.Line($"  App name:      {CliSupport.AppName(root)}");
        Terminal.Line($"  Server URL:    {RunCommands.ReadServerUrl(root)}");
        Terminal.Line($"  Environment:   {Environment.GetEnvironmentVariable("WEBSITER_ENV") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}");
        var appPid = CliSupport.AppPid(root);
        Terminal.Line($"  App status:    {(appPid is null ? "stopped" : $"running (pid {appPid})")}");
        Terminal.Line($"  Workers dir:   {(Directory.Exists(Path.Combine(root, "Workers")) ? "present" : "absent")}");
        Terminal.Line($"  IPC dir:       {Path.Combine(Paths.StateDirFor(root), "ipc")}");
        return Task.FromResult(0);
    }

    private static void MaskSecrets(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is JsonValue v && v.TryGetValue<string>(out var s)
                    && SecretHints.Any(h => kvp.Key.Contains(h, StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(s))
                {
                    obj[kvp.Key] = "***";
                }
                else MaskSecrets(kvp.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr) MaskSecrets(item);
        }
    }
}
