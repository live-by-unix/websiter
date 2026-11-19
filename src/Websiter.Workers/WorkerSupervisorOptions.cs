using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Websiter.Workers;

/// <summary>Supervisor behavior configuration, bound from the <c>Workers</c> section.</summary>
public sealed class WorkerSupervisorOptions
{
    /// <summary>Automatically restart crashed workers.</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>Maximum restart attempts before a worker is marked crashed.</summary>
    public int RestartLimit { get; set; } = 5;

    /// <summary>Initial restart delay; doubles each attempt (exponential backoff).</summary>
    public int RestartDelayMs { get; set; } = 500;

    /// <summary>Maximum restart delay cap.</summary>
    public int MaxRestartDelayMs { get; set; } = 30_000;

    /// <summary>Time to wait for a worker to report running after spawn.</summary>
    public int StartTimeoutMs { get; set; } = 15_000;

    /// <summary>Health check interval for process monitoring.</summary>
    public int HealthCheckIntervalMs { get; set; } = 2_000;

    /// <summary>Grace period after requesting shutdown before force-kill.</summary>
    public int ShutdownTimeoutMs { get; set; } = 10_000;

    /// <summary>Worker-specific overrides keyed by worker name.</summary>
    public Dictionary<string, WorkerOverrideOptions> Workers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static WorkerSupervisorOptions FromConfig()
    {
        var options = new WorkerSupervisorOptions();
        var section = Config.Root?.GetSection("Workers");
        if (section is not null) section.Bind(options);
        return options;
    }
}

/// <summary>Per-worker overrides.</summary>
public sealed class WorkerOverrideOptions
{
    public bool? AutoRestart { get; set; }
    public int? RestartLimit { get; set; }
    public string[]? DependsOn { get; set; }
}

/// <summary>Runtime state of a managed worker.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkerState
{
    Stopped,
    Starting,
    Running,
    Restarting,
    Stopping,
    Crashed,
    Failed,
}

/// <summary>Snapshot of a worker's status for CLI/diagnostics.</summary>
public sealed class WorkerStatus
{
    public required string Name { get; init; }
    public required WorkerState State { get; set; }
    public int? Pid { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public double? UptimeSeconds { get; set; }
    public int RestartCount { get; set; }
    public int? LastExitCode { get; set; }
    public string? LastError { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
