using System.Diagnostics;
using System.Text.Json;
using Websiter.Ipc;

namespace Websiter.Workers;

/// <summary>Locates the application assembly produced by build/dev.</summary>
public static class AppLayout
{
    /// <summary>Reads <c>.websiter/app.json</c> written by <c>websiter build</c>/<c>dev</c>.</summary>
    public static string? FindApplicationAssembly(string root)
    {
        var manifest = Path.Combine(Paths.StateDirFor(root), "app.json");
        if (File.Exists(manifest))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var assembly = doc.RootElement.TryGetProperty("assembly", out var a) ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(assembly) && File.Exists(assembly))
                    return Path.GetFullPath(assembly);
            }
            catch { }
        }

        // Fallback: scan bin/{Debug|Release}/net*/ for the application dll.
        var appName = Config.Get("application:name", Path.GetFileName(root));
        var candidates = new List<(string Path, DateTime Write)>();
        foreach (var config in new[] { "Release", "Debug" })
        {
            var dir = Path.Combine(root, "bin", config);
            if (!Directory.Exists(dir)) continue;
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (name.EndsWith(".Tests") || name.StartsWith("Websiter.")) continue;
                candidates.Add((dll, File.GetLastWriteTimeUtc(dll)));
            }
        }

        // Prefer an assembly matching the app name, else newest.
        var preferred = candidates
            .Where(c => string.Equals(Path.GetFileNameWithoutExtension(c.Path), appName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pool = preferred.Count > 0 ? preferred : candidates;
        return pool.OrderByDescending(c => c.Write).FirstOrDefault().Path;
    }
}

/// <summary>Launches worker code as an independent OS process.</summary>
public static class WorkerProcessLauncher
{
    /// <summary>
    /// Builds the start info for a worker process.
    /// Workers execute inside the application assembly via
    /// <c>--websiter-worker=&lt;name&gt;</c>.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string root, string workerName, string? assemblyPath = null)
    {
        assemblyPath ??= AppLayout.FindApplicationAssembly(root)
            ?? throw new WorkerException(
                $"Cannot locate the application assembly for '{root}'. Run `websiter build` or `websiter dev` first.");

        var dllDir = Path.GetDirectoryName(assemblyPath)!;
        var appHost = Path.Combine(dllDir,
            OperatingSystem.IsWindows() ? Path.GetFileNameWithoutExtension(assemblyPath) + ".exe"
                                         : Path.GetFileNameWithoutExtension(assemblyPath));

        var args = $"--websiter-worker={workerName} --root \"{root}\"";

        ProcessStartInfo psi;
        if (File.Exists(appHost))
        {
            psi = new ProcessStartInfo(appHost, args) { WorkingDirectory = root };
        }
        else
        {
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            psi = new ProcessStartInfo(dotnet, $"exec \"{assemblyPath}\" {args}") { WorkingDirectory = root };
        }

        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = !OperatingSystem.IsWindows();

        var env = psi.Environment;
        env["WEBSITER_WORKER"] = workerName;
        env["WEBSITER_ROOT"] = root;
        env["WEBSITER_ENV"] = WebsiterEnvironment.Name;
        var appName = Config.Get("application:name", Path.GetFileName(root));
        env["WEBSITER_APP"] = appName;

        return psi;
    }

    /// <summary>Requests graceful shutdown: IPC $shutdown first, then OS signal/kill.</summary>
    public static async Task StopProcessAsync(int pid, string workerName, int shutdownTimeoutMs, CancellationToken ct = default)
    {
        // 1. Graceful: ask the worker to stop over IPC.
        try
        {
            var hub = IpcHub.Current;
            await hub.RequestAsync<object>(workerName, "$shutdown", timeout: TimeSpan.FromSeconds(3), ct: ct)
                .ConfigureAwait(false);
        }
        catch { /* worker may not be listening */ }

        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                var exited = process.WaitForExitAsync(ct);
                var timeout = Task.Delay(shutdownTimeoutMs, ct);
                if (await Task.WhenAny(exited, timeout).ConfigureAwait(false) == exited) return;
            }
        }
        catch { }

        // 2. OS-level termination.
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                using var kill = Process.Start("/bin/kill", $"-TERM {pid}");
                kill?.WaitForExit(2000);
            }
            else
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
        }
        catch { }

        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(3000))
            {
                if (!OperatingSystem.IsWindows())
                {
                    using var kill = Process.Start("/bin/kill", $"-KILL {pid}");
                    kill?.WaitForExit(2000);
                }
                else p.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }
}
