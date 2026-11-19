using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Websiter.Workers;

namespace Websiter.CLI;

/// <summary>Shared helpers: project root, websiter.json, app process, IPC client.</summary>
public static class CliSupport
{
    /// <summary>Finds the project root from args (--root) or cwd.</summary>
    public static string FindRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--root" or "-r") return Path.GetFullPath(args[i + 1]);
        }
        var env = Environment.GetEnvironmentVariable("WEBSITER_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return Path.GetFullPath(env);
        var root = WorkerMain.FindProjectRoot(Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "websiter.json"))
            && !Directory.EnumerateFiles(root, "*.csproj").Any()
            && root == Directory.GetCurrentDirectory())
        {
            // Still return it; commands that need a project will validate.
        }
        return root;
    }

    /// <summary>Returns args with known value-flags (and their values) removed — for positional extraction.</summary>
    public static string[] PositionalArgs(string[] args)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--root" or "-r" or "--dir" or "--template" or "--config" or "--environment") { i++; continue; }
            if (a.StartsWith("--root=", StringComparison.Ordinal)
                || a.StartsWith("-r=", StringComparison.Ordinal)
                || a.StartsWith("--dir=", StringComparison.Ordinal)
                || a.StartsWith("--template=", StringComparison.Ordinal)) continue;
            result.Add(a);
        }
        return result.ToArray();
    }

    /// <summary>Requires a valid project root or throws a usage error.</summary>
    public static string RequireProjectRoot(string[] args)
    {
        var root = FindRoot(args);
        var hasProject = File.Exists(Path.Combine(root, "websiter.json"))
                         || Directory.EnumerateFiles(root, "*.csproj").Any();
        if (!hasProject)
            throw new Program.CliUsageException($"No Websiter project found at '{root}'. Run 'websiter new <name>' first or pass --root <path>.");
        return root;
    }

    // ------------------------- websiter.json -------------------------

    public static JsonObject LoadConfigFile(string root)
    {
        var path = Path.Combine(root, "websiter.json");
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    public static void SaveConfigFile(string root, JsonObject config)
    {
        var path = Path.Combine(root, "websiter.json");
        File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static string AppName(string root)
    {
        var cfg = LoadConfigFile(root);
        return cfg["application"]?["name"]?.GetValue<string>()
               ?? Path.GetFileName(root);
    }

    // ------------------------- App process -------------------------

    /// <summary>PID file for the app process.</summary>
    public static string AppPidFile(string root) => Path.Combine(Paths.StateDirFor(root), "app.pid");

    /// <summary>Returns the running app pid, or null.</summary>
    public static int? AppPid(string root)
    {
        var file = AppPidFile(root);
        if (!File.Exists(file)) return null;
        try
        {
            var pid = int.Parse(File.ReadAllText(file).Trim());
            using var p = Process.GetProcessById(pid);
            return p.HasExited ? null : pid;
        }
        catch { return null; }
    }

    /// <summary>Starts the app process detached in the background.</summary>
    public static Process StartApp(string root, bool watch = false)
    {
        var assembly = AppLayout.FindApplicationAssembly(root)
            ?? throw new InvalidOperationException(
                "Application assembly not found. Run 'websiter build' or 'websiter dev' first.");

        var dllDir = Path.GetDirectoryName(assembly)!;
        var name = Path.GetFileNameWithoutExtension(assembly);
        var appHost = Path.Combine(dllDir, OperatingSystem.IsWindows() ? name + ".exe" : name);

        ProcessStartInfo psi;
        if (File.Exists(appHost))
            psi = new ProcessStartInfo(appHost, $"--root \"{root}\"");
        else
            psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                $"exec \"{assembly}\" --root \"{root}\"");

        psi.WorkingDirectory = root;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;
        if (watch)
        {
            psi.Environment["WEBSITER_DEV"] = "1";
            psi.Environment["WEBSITER_WATCH"] = "1";
        }
        psi.Environment["WEBSITER_ENV"] = Environment.GetEnvironmentVariable("WEBSITER_ENV")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the application process.");

        Directory.CreateDirectory(Paths.StateDirFor(root));
        File.WriteAllText(AppPidFile(root), process.Id.ToString());
        return process;
    }

    /// <summary>Stops the app process gracefully.</summary>
    public static async Task<bool> StopAppAsync(string root, int timeoutMs = 10_000)
    {
        var pid = AppPid(root);
        if (pid is null) return false;

        // Try IPC graceful shutdown first.
        try
        {
            await using var session = await IpcSession.OpenAsync(root, "cli").ConfigureAwait(false);
            await session.RequestAsync<object>("host", "$shutdown", timeout: TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch { }

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid.Value);
                if (p.HasExited) break;
            }
            catch { break; }
            await Task.Delay(200).ConfigureAwait(false);
        }

        try
        {
            using var p = Process.GetProcessById(pid.Value);
            if (!p.HasExited)
            {
                if (!OperatingSystem.IsWindows())
                {
                    using var kill = Process.Start("/bin/kill", $"-TERM {pid.Value}");
                    kill?.WaitForExit(2000);
                }
                else p.Kill(entireProcessTree: true);
            }
            p.WaitForExit(5000);
        }
        catch { }

        try { File.Delete(AppPidFile(root)); } catch { }
        return true;
    }

    // ------------------------- IPC session -------------------------

    /// <summary>Opens an IPC hub session for CLI commands.</summary>
    public static async Task<Ipc.IpcHub> OpenIpcAsync(string root)
    {
        // Point IPC at the project's state dir.
        Environment.SetEnvironmentVariable("WEBSITER_IPC_DIR",
            Path.Combine(Paths.StateDirFor(root), "ipc"));
        Environment.SetEnvironmentVariable("WEBSITER_APP", AppName(root));
        var hub = Ipc.IpcHub.Initialize("cli");
        return hub;
    }
}

/// <summary>A disposable IPC session that resets the hub on dispose (CLI use).</summary>
internal sealed class IpcSession : IAsyncDisposable
{
    private readonly Ipc.IpcHub _hub;
    private IpcSession(Ipc.IpcHub hub) => _hub = hub;

    public static async Task<IpcSession> OpenAsync(string root, string name)
    {
        Environment.SetEnvironmentVariable("WEBSITER_IPC_DIR",
            Path.Combine(Paths.StateDirFor(root), "ipc"));
        Environment.SetEnvironmentVariable("WEBSITER_APP", CliSupport.AppName(root));
        var hub = Ipc.IpcHub.Initialize(name);
        return new IpcSession(hub);
    }

    public Task<T?> RequestAsync<T>(string worker, string action, object? payload = null, TimeSpan? timeout = null)
        => _hub.RequestAsync<T>(worker, action, payload, timeout);

    public ValueTask DisposeAsync()
    {
        Ipc.IpcHub.ResetForTests();
        return ValueTask.CompletedTask;
    }
}
