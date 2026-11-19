using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Websiter.Ipc;

namespace Websiter.Workers;

/// <summary>
/// Supervises worker OS processes: startup ordering by dependency, crash
/// detection, exponential-backoff restarts, graceful shutdown and status
/// reporting. A worker crash never terminates siblings or the host.
/// </summary>
public sealed class WorkerSupervisor : IAsyncDisposable
{
    private readonly string _root;
    private readonly List<WorkerDefinition> _definitions;
    private readonly WorkerSupervisorOptions _options;
    private readonly ConcurrentDictionary<string, WorkerRuntime> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _monitorTask;
    private int _disposed;

    /// <summary>Raised whenever a worker's status changes.</summary>
    public event Action<WorkerStatus>? StatusChanged;

    public WorkerSupervisor(string root, IEnumerable<WorkerDefinition> definitions, WorkerSupervisorOptions? options = null)
    {
        _root = Path.GetFullPath(root);
        _definitions = definitions.ToList();
        _options = options ?? WorkerSupervisorOptions.FromConfig();
        ValidateDependencies();
    }

    /// <summary>All known worker definitions.</summary>
    public IReadOnlyList<WorkerDefinition> Definitions => _definitions;

    private void ValidateDependencies()
    {
        var names = _definitions.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = _definitions
            .SelectMany(d => d.DependsOn)
            .Where(dep => !names.Contains(dep))
            .Distinct()
            .ToList();
        if (missing.Count > 0)
            throw new WorkerException($"Worker dependency validation failed: unknown workers [{string.Join(", ", missing)}].");
    }

    /// <summary>Starts all workers in dependency order and begins monitoring.</summary>
    public async Task StartAllAsync(CancellationToken ct = default)
    {
        foreach (var def in TopologicalOrder(_definitions))
        {
            ct.ThrowIfCancellationRequested();
            await StartWorkerAsync(def.Name, ct).ConfigureAwait(false);
        }
        _monitorTask = Task.Run(() => MonitorLoopAsync(_lifetime.Token));
    }

    /// <summary>Starts a single worker (or restarts it if already known).</summary>
    public async Task StartWorkerAsync(string name, CancellationToken ct = default)
    {
        var def = _definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkerException($"Unknown worker '{name}'.");

        // Ensure dependencies are running first.
        foreach (var dep in def.DependsOn)
        {
            var depRuntime = _workers.GetValueOrDefault(dep);
            if (depRuntime?.Process?.HasExited != false)
                await StartWorkerAsync(dep, ct).ConfigureAwait(false);
        }

        var existing = _workers.GetOrAdd(name, _ => new WorkerRuntime(def));
        await existing.StartAsync(_root, _options, OnExit, ct).ConfigureAwait(false);
    }

    /// <summary>Stops a single worker gracefully.</summary>
    public async Task StopWorkerAsync(string name, CancellationToken ct = default)
    {
        if (!_workers.TryGetValue(name, out var runtime)) return;
        await runtime.StopAsync(_options.ShutdownTimeoutMs, ct).ConfigureAwait(false);
    }

    /// <summary>Restarts a worker.</summary>
    public async Task RestartWorkerAsync(string name, CancellationToken ct = default)
    {
        await StopWorkerAsync(name, ct).ConfigureAwait(false);
        await StartWorkerAsync(name, ct).ConfigureAwait(false);
    }

    /// <summary>Stops all workers.</summary>
    public async Task StopAllAsync(CancellationToken ct = default)
    {
        var tasks = _workers.Values.Select(w => w.StopAsync(_options.ShutdownTimeoutMs, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>Snapshot of all worker statuses.</summary>
    public IReadOnlyList<WorkerStatus> GetStatus()
        => _workers.Values.Select(w => w.ToStatus()).OrderBy(s => s.Name).ToList();

    /// <summary>Finds a worker by name.</summary>
    public WorkerStatus? Find(string name)
        => _workers.TryGetValue(name, out var w) ? w.ToStatus() : null;

    /// <summary>Persists the status snapshot for CLI/diagnostics consumers.</summary>
    public void WriteStatusSnapshot()
    {
        try
        {
            var snapshot = new
            {
                updatedUtc = DateTimeOffset.UtcNow,
                workers = GetStatus(),
            };
            File.WriteAllText(
                Path.Combine(Paths.StateDirFor(_root), "workers-status.json"),
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void OnExit(WorkerRuntime runtime)
    {
        var status = runtime.ToStatus();
        StatusChanged?.Invoke(status);
        WriteStatusSnapshot();

        var opts = OptionsFor(runtime.Definition.Name);
        if (runtime.StoppingIntentionally || !opts.AutoRestart)
        {
            runtime.SetState(WorkerState.Stopped);
            return;
        }

        runtime.RestartCount++;
        if (runtime.RestartCount > opts.RestartLimit)
        {
            runtime.SetState(WorkerState.Crashed);
            runtime.LastError = $"Restart limit ({opts.RestartLimit}) exceeded; worker crashed. Last exit code: {runtime.LastExitCode}.";
            Log.Error($"Worker '{runtime.Definition.Name}' exceeded restart limit and was stopped.");
            return;
        }

        runtime.SetState(WorkerState.Restarting);
        var delay = ComputeBackoff(runtime.RestartCount, opts);
        Log.Warn($"Worker '{runtime.Definition.Name}' exited (code {runtime.LastExitCode}); restarting in {delay.TotalMilliseconds:0}ms (attempt {runtime.RestartCount}/{opts.RestartLimit}).");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _lifetime.Token).ConfigureAwait(false);
                if (_disposed != 0) return;
                await runtime.StartAsync(_root, _options, OnExit, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to restart worker '{runtime.Definition.Name}': {ex.Message}");
                runtime.SetState(WorkerState.Failed);
            }
        });
    }

    private WorkerOverrideOptions? OverrideFor(string name) =>
        _options.Workers.TryGetValue(name, out var o) ? o : null;

    private (bool AutoRestart, int RestartLimit, int RestartDelayMs, int MaxRestartDelayMs, int StartTimeoutMs) OptionsFor(string name)
    {
        var o = OverrideFor(name);
        return (
            o?.AutoRestart ?? _options.AutoRestart,
            o?.RestartLimit ?? _options.RestartLimit,
            _options.RestartDelayMs,
            _options.MaxRestartDelayMs,
            _options.StartTimeoutMs);
    }

    private static TimeSpan ComputeBackoff(int attempt, (bool AutoRestart, int RestartLimit, int RestartDelayMs, int MaxRestartDelayMs, int StartTimeoutMs) opts)
    {
        var delay = (double)opts.RestartDelayMs * Math.Pow(2, Math.Min(attempt, 10));
        return TimeSpan.FromMilliseconds(Math.Min(delay, opts.MaxRestartDelayMs));
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HealthCheckIntervalMs, ct).ConfigureAwait(false);
                foreach (var runtime in _workers.Values)
                    runtime.PollHealth();
                WriteStatusSnapshot();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error($"Worker monitor error: {ex.Message}"); }
        }
    }

    private static IEnumerable<WorkerDefinition> TopologicalOrder(IEnumerable<WorkerDefinition> defs)
    {
        var byName = defs.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WorkerDefinition>();

        void Visit(WorkerDefinition def, HashSet<string> stack)
        {
            if (visited.Contains(def.Name)) return;
            if (!stack.Add(def.Name))
                throw new WorkerException($"Circular worker dependency involving '{def.Name}'.");
            foreach (var dep in def.DependsOn)
                if (byName.TryGetValue(dep, out var depDef)) Visit(depDef, stack);
            stack.Remove(def.Name);
            visited.Add(def.Name);
            result.Add(def);
        }

        foreach (var def in defs) Visit(def, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        await StopAllAsync().ConfigureAwait(false);
        if (_monitorTask is not null)
        {
            try { await _monitorTask.ConfigureAwait(false); } catch { }
        }
        WriteStatusSnapshot();
        _lifetime.Dispose();
    }

    /// <summary>Per-worker runtime state: process handle, restart bookkeeping, log capture.</summary>
    private sealed class WorkerRuntime
    {
        public WorkerDefinition Definition { get; }
        public Process? Process { get; private set; }
        public WorkerState State { get; private set; } = WorkerState.Stopped;
        public int RestartCount { get; set; }
        public int? LastExitCode { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? StartedUtc { get; private set; }
        public bool StoppingIntentionally { get; private set; }
        public event Action<WorkerRuntime>? Exited;

        private readonly object _gate = new();
        private StreamWriter? _logWriter;
        private Task? _stdoutPump;
        private Task? _stderrPump;

        public WorkerRuntime(WorkerDefinition definition) => Definition = definition;

        public async Task StartAsync(string root, WorkerSupervisorOptions options, Action<WorkerRuntime> onExit, CancellationToken ct)
        {
            lock (_gate)
            {
                if (Process is { HasExited: false }) return; // already running
            }

            SetState(WorkerState.Starting);
            var psi = WorkerProcessLauncher.CreateStartInfo(root, Definition.Name);
            var process = Process.Start(psi)
                ?? throw new WorkerException($"Failed to start worker process for '{Definition.Name}'.");

            Process = process;
            StartedUtc = DateTimeOffset.UtcNow;
            StoppingIntentionally = false;
            LastError = null;

            // Capture output to the worker log file.
            var logPath = Paths.LogFile($"worker-{Definition.Name}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                _logWriter?.Dispose();
                _logWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                _stdoutPump = PumpAsync(process.StandardOutput, _logWriter);
                _stderrPump = PumpAsync(process.StandardError, _logWriter);
            }
            catch { /* logging is best-effort */ }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                var code = SafeExitCode(process);
                LastExitCode = code;
                SetState(WorkerState.Stopped);
                onExit(this);
                Exited?.Invoke(this);
            };

            // Wait for the worker to report running via its state file.
            var deadline = DateTime.UtcNow.AddMilliseconds(options.StartTimeoutMs);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                if (IsReportedRunning(root, Definition.Name))
                {
                    SetState(WorkerState.Running);
                    return;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            if (process.HasExited)
                throw new WorkerException($"Worker '{Definition.Name}' exited during startup (code {SafeExitCode(process)}).");

            SetState(WorkerState.Running); // assume running; health monitor will catch problems
        }

        private static async Task PumpAsync(StreamReader reader, TextWriter writer)
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            catch { }
        }

        private static bool IsReportedRunning(string root, string worker)
        {
            try
            {
                var file = Path.Combine(Paths.StateDirFor(root), $"worker-{worker}.json");
                if (!File.Exists(file)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                return doc.RootElement.TryGetProperty("status", out var s)
                    && s.GetString() == "running";
            }
            catch { return false; }
        }

        private static int SafeExitCode(Process process)
        {
            try { return process.ExitCode; } catch { return -1; }
        }

        public async Task StopAsync(int shutdownTimeoutMs, CancellationToken ct)
        {
            StoppingIntentionally = true;
            SetState(WorkerState.Stopping);
            var process = Process;
            if (process is null || process.HasExited) { SetState(WorkerState.Stopped); return; }

            try
            {
                await WorkerProcessLauncher.StopProcessAsync(process.Id, Definition.Name, shutdownTimeoutMs, ct)
                    .ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (!process.WaitForExit(shutdownTimeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    process.WaitForExit(3000);
                }
            }
            catch { }
            SetState(WorkerState.Stopped);
        }

        public void PollHealth()
        {
            var process = Process;
            if (process is not null && State is WorkerState.Running or WorkerState.Restarting && process.HasExited)
            {
                // Exited event handles restart; nothing else needed here.
            }
        }

        public void SetState(WorkerState state)
        {
            State = state;
        }

        public WorkerStatus ToStatus() => new()
        {
            Name = Definition.Name,
            State = State,
            Pid = Process is { HasExited: false } ? Process.Id : null,
            StartedUtc = StartedUtc,
            UptimeSeconds = State is WorkerState.Running && StartedUtc is not null
                ? (DateTimeOffset.UtcNow - StartedUtc.Value).TotalSeconds : null,
            RestartCount = RestartCount,
            LastExitCode = LastExitCode,
            LastError = LastError,
            DependsOn = Definition.DependsOn.ToList(),
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
    }
}
