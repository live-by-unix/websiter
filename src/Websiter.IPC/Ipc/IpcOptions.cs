namespace Websiter.Ipc;

/// <summary>Options controlling IPC behavior. Bound from the <c>Ipc</c> configuration section.</summary>
public sealed class IpcOptions
{
    /// <summary>Default request timeout.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum size of a single IPC message in bytes.</summary>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Bounded send-queue depth per connection (backpressure).</summary>
    public int SendQueueCapacity { get; set; } = 256;

    /// <summary>Time to wait for a worker endpoint to accept connections.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Transport override: "auto", "namedpipe", "uds".</summary>
    public string Transport { get; set; } = "auto";

    /// <summary>Directory used for Unix domain socket endpoints and the IPC registry.</summary>
    public string? Directory { get; set; }

    /// <summary>Application name used to namespace endpoints.</summary>
    public string ApplicationName { get; set; } = "websiter-app";

    /// <summary>Whether IPC file/socket permissions are restricted to the current user (always enforced where supported).</summary>
    public bool RestrictPermissions { get; set; } = true;
}
