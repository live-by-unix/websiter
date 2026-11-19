using System.Diagnostics;
using Websiter.Deployment;
using Websiter.Workers;

namespace Websiter.CLI.Commands;

/// <summary>`websiter dev` — build, run and watch for changes.</summary>
public static class DevCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var noWatch = args.Contains("--no-watch");

        if (!await BuildDebugAsync(root).ConfigureAwait(false)) return 1;

        var url = RunCommands.ReadServerUrl(root);
        Terminal.Success($"Dev server starting at {url}");
        if (!noWatch) Terminal.Dim("Watching for changes... (Ctrl+C to stop)");

        var session = new DevSession(root);
        session.StartApp();

        if (noWatch)
        {
            await session.WaitCurrentAsync().ConfigureAwait(false);
            return session.ExitCode;
        }

        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        foreach (var filter in new[] { "*.cs", "*.html", "*.css", "*.js", "*.json" })
            watcher.Filters.Add(filter);
        watcher.EnableRaisingEvents = true;

        string? pendingPath = null;
        var debounce = new Timer(_ =>
        {
            lock (session.Gate)
            {
                if (pendingPath is null) return;
                var changed = pendingPath;
                pendingPath = null;
                _ = session.RebuildAndRestartAsync(changed);
            }
        }, null, Timeout.Infinite, Timeout.Infinite);

        void OnChanged(object? s, FileSystemEventArgs e)
        {
            // Ignore build output and state dirs.
            var full = e.FullPath;
            if (full.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || full.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || full.Contains($"{Path.DirectorySeparatorChar}.websiter{Path.DirectorySeparatorChar}"))
                return;
            lock (session.Gate) pendingPath = full;
            debounce.Change(800, Timeout.Infinite);
        }
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; session.Stop(); };
        await session.WaitCurrentAsync().ConfigureAwait(false);
        session.Dispose();
        return session.ExitCode == 0 ? 0 : 1;
    }

    private static async Task<bool> BuildDebugAsync(string root)
    {
        var csproj = Directory.EnumerateFiles(root, "*.csproj").FirstOrDefault();
        if (csproj is null) { Terminal.Error("No .csproj in project root."); return false; }

        var psi = new ProcessStartInfo("dotnet", $"build \"{csproj}\" -c Debug --nologo -v q")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)
                   + await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            Terminal.Error("Build failed:");
            Terminal.Line(output.Trim());
            return false;
        }

        var dll = Directory.EnumerateFiles(Path.Combine(root, "bin", "Debug"), "*.dll", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("Websiter."))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (dll is not null) AppManifest.Write(root, dll, "Debug");
        return true;
    }

    /// <summary>Holds the current app process and serializes rebuild/restart cycles.</summary>
    private sealed class DevSession : IDisposable
    {
        private readonly string _root;
        private Process? _app;
        private readonly SemaphoreSlim _rebuildLock = new(1, 1);
        private volatile bool _stopped;

        public object Gate { get; } = new();
        public int ExitCode { get; private set; }

        public DevSession(string root) => _root = root;

        public void StartApp()
        {
            var assembly = AppLayout.FindApplicationAssembly(_root)
                ?? throw new InvalidOperationException("Application assembly not found after build.");
            var dllDir = Path.GetDirectoryName(assembly)!;
            var name = Path.GetFileNameWithoutExtension(assembly);
            var appHost = Path.Combine(dllDir, OperatingSystem.IsWindows() ? name + ".exe" : name);

            ProcessStartInfo psi;
            if (File.Exists(appHost))
                psi = new ProcessStartInfo(appHost, $"--root \"{_root}\"");
            else
                psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                    $"exec \"{assembly}\" --root \"{_root}\"");
            psi.WorkingDirectory = _root;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.Environment["WEBSITER_ENV"] = "Development";
            psi.Environment["WEBSITER_DEV"] = "1";
            psi.Environment["WEBSITER_WATCH"] = "1";
            psi.Environment["DOTNET_ENVIRONMENT"] = "Development";

            _app = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start app.");
            PumpOutput(_app, "app ");
            PumpOutput(_app, "app!", error: true);
        }

        public async Task RebuildAndRestartAsync(string changedPath)
        {
            if (_stopped) return;
            await _rebuildLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stopped) return;
                Terminal.Info($"Change detected: {Path.GetFileName(changedPath)} — rebuilding...");
                if (!await BuildDebugAsync(_root).ConfigureAwait(false))
                {
                    Terminal.Warn("Build failed; keeping previous process running.");
                    return;
                }

                var app = _app;
                if (app is not null && !app.HasExited)
                {
                    try { app.Kill(entireProcessTree: true); } catch { }
                    try { await app.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }
                }
                _app?.Dispose();
                StartApp();
                Terminal.Success("Restarted.");
            }
            finally
            {
                _rebuildLock.Release();
            }
        }

        public async Task WaitCurrentAsync()
        {
            while (!_stopped)
            {
                var app = _app;
                if (app is null) return;
                try { await app.WaitForExitAsync().ConfigureAwait(false); }
                catch { return; }
                ExitCode = app.ExitCode;
                if (_stopped) return;

                // App exited — restart it, unless a rebuild already swapped in a new process.
                await _rebuildLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_stopped || !ReferenceEquals(_app, app)) continue;
                    Terminal.Warn($"App exited (code {app.ExitCode}); restarting...");
                    try { app.Dispose(); } catch { }
                    try { StartApp(); }
                    catch (Exception ex) { Terminal.Error($"Restart failed: {ex.Message}"); return; }
                }
                finally { _rebuildLock.Release(); }
            }
        }

        public void Stop()
        {
            _stopped = true;
            try { _app?.Kill(entireProcessTree: true); } catch { }
        }

        private static void PumpOutput(Process process, string tag, bool error = false)
        {
            var stream = error ? process.StandardError : process.StandardOutput;
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await stream.ReadLineAsync().ConfigureAwait(false)) is not null)
                        Console.WriteLine($"{tag}| {line}");
                }
                catch { }
            });
        }

        public void Dispose()
        {
            _stopped = true;
            try { _app?.Kill(entireProcessTree: true); } catch { }
            _app?.Dispose();
            _rebuildLock.Dispose();
        }
    }
}
