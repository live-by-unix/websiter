using System.Collections.Concurrent;
using System.Text.Json;

namespace Websiter.Diagnostics;

/// <summary>In-memory request metrics with sliding counters.</summary>
public sealed class MetricsHub
{
    private static MetricsHub? _current;
    private static readonly object _gate = new();

    private long _requestsTotal;
    private long _requestsFailed;
    private readonly ConcurrentDictionary<string, long> _requestsByRoute = new();
    private readonly ConcurrentDictionary<int, long> _statusCounts = new();
    private readonly ConcurrentDictionary<string, double> _routeTotalMs = new();
    private readonly ConcurrentDictionary<string, long> _routeCount = new();
    private readonly object _timingGate = new();
    private DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public static MetricsHub Current
    {
        get
        {
            lock (_gate) return _current ??= new MetricsHub();
        }
    }

    /// <summary>Records a completed HTTP request.</summary>
    public void RecordRequest(string route, int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _requestsTotal);
        _requestsByRoute.AddOrUpdate(route, 1, (_, c) => c + 1);
        _statusCounts.AddOrUpdate(statusCode, 1, (_, c) => c + 1);
        if (statusCode >= 500) Interlocked.Increment(ref _requestsFailed);

        lock (_timingGate)
        {
            _routeTotalMs.AddOrUpdate(route, elapsedMs, (_, t) => t + elapsedMs);
            _routeCount.AddOrUpdate(route, 1, (_, c) => c + 1);
        }
    }

    /// <summary>Snapshot of metrics as an anonymous object (JSON-serializable).</summary>
    public object Snapshot()
    {
        var routes = _requestsByRoute.Select(kvp =>
        {
            double avgMs = 0;
            lock (_timingGate)
            {
                if (_routeTotalMs.TryGetValue(kvp.Key, out var total) && _routeCount.TryGetValue(kvp.Key, out var count) && count > 0)
                    avgMs = total / count;
            }
            return new
            {
                route = kvp.Key,
                requests = kvp.Value,
                avgDurationMs = Math.Round(avgMs, 3),
            };
        }).OrderByDescending(r => r.requests).ToList();

        return new
        {
            startedUtc = _startedUtc,
            uptimeSeconds = (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds,
            requestsTotal = Interlocked.Read(ref _requestsTotal),
            requestsFailed = Interlocked.Read(ref _requestsFailed),
            byRoute = routes,
            byStatus = _statusCounts.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value),
        };
    }
}

/// <summary>Aggregates health checks from app, workers and IPC.</summary>
public sealed class HealthHub
{
    private readonly List<Func<CancellationToken, Task<HealthCheckResult>>> _checks = new();

    public void AddCheck(string name, Func<CancellationToken, Task<HealthCheckResult>> check)
    {
        lock (_checks) _checks.Add(check);
    }

    /// <summary>Runs all checks and returns an overall status.</summary>
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var results = new List<HealthCheckResult>();
        Func<CancellationToken, Task<HealthCheckResult>>[] checks;
        lock (_checks) checks = _checks.ToArray();

        foreach (var check in checks)
        {
            try { results.Add(await check(ct).ConfigureAwait(false)); }
            catch (Exception ex)
            {
                results.Add(new HealthCheckResult("unknown", HealthStatus.Unhealthy, ex.Message));
            }
        }

        var overall = results.Any(r => r.Status == HealthStatus.Unhealthy) ? HealthStatus.Unhealthy
            : results.Any(r => r.Status == HealthStatus.Degraded) ? HealthStatus.Degraded
            : HealthStatus.Healthy;

        return new HealthReport
        {
            Status = overall,
            Timestamp = DateTimeOffset.UtcNow,
            Checks = results,
        };
    }
}

public enum HealthStatus { Healthy, Degraded, Unhealthy }

public sealed class HealthCheckResult
{
    public HealthCheckResult(string name, HealthStatus status, string? description = null, object? data = null)
    {
        Name = name; Status = status; Description = description; Data = data;
    }
    public string Name { get; }
    public HealthStatus Status { get; }
    public string? Description { get; }
    public object? Data { get; }
}

public sealed class HealthReport
{
    public required HealthStatus Status { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required IReadOnlyList<HealthCheckResult> Checks { get; init; }
}

/// <summary>Reads worker state files written by workers/supervisor for health reporting.</summary>
public static class WorkerStateReader
{
    /// <summary>Reads all worker state files under a project root's .websiter directory.</summary>
    public static IReadOnlyList<JsonElement> ReadAll(string root)
    {
        var results = new List<JsonElement>();
        var stateDir = Websiter.Paths.StateDirFor(root);
        if (!Directory.Exists(stateDir)) return results;

        foreach (var file in Directory.EnumerateFiles(stateDir, "worker-*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                results.Add(doc.RootElement.Clone());
            }
            catch { }
        }
        return results;
    }
}
