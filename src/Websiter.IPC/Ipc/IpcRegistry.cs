using System.Text.Json;

namespace Websiter.Ipc;

/// <summary>A worker's IPC registration entry stored in the shared registry directory.</summary>
public sealed class IpcRegistration
{
    [System.Text.Json.Serialization.JsonPropertyName("worker")] public string Worker { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("pid")] public int Pid { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("subscriptions")] public List<string> Subscriptions { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("startedUtc")] public DateTimeOffset StartedUtc { get; set; }
}

/// <summary>
/// File-based registry that lets workers discover each other's IPC
/// subscriptions for event publishing. Entries live under
/// <c>{ipcDir}/registry/{worker}.json</c>.
/// </summary>
internal static class IpcRegistry
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private static string RegistryDir(IpcOptions options)
    {
        var dir = options.Directory ?? Path.Combine(Paths.StateDir, "ipc");
        var registry = Path.Combine(dir, "registry");
        Directory.CreateDirectory(registry);
        return registry;
    }

    public static void Register(IpcOptions options, string worker, IEnumerable<string> subscriptions)
    {
        try
        {
            var entry = new IpcRegistration
            {
                Worker = worker,
                Pid = Environment.ProcessId,
                Subscriptions = subscriptions.Distinct().ToList(),
                StartedUtc = DateTimeOffset.UtcNow,
            };
            var path = Path.Combine(RegistryDir(options), IpcEndpointNaming.Sanitize(worker) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(entry, _json));
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to write IPC registry entry for '{worker}': {ex.Message}");
        }
    }

    public static void Unregister(IpcOptions options, string worker)
    {
        try
        {
            var path = Path.Combine(RegistryDir(options), IpcEndpointNaming.Sanitize(worker) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>Returns registrations whose process is still alive.</summary>
    public static IReadOnlyList<IpcRegistration> ReadAll(IpcOptions options)
    {
        var results = new List<IpcRegistration>();
        string dir;
        try { dir = RegistryDir(options); }
        catch { return results; }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<IpcRegistration>(File.ReadAllText(file));
                if (entry is null) continue;
                if (!PidAlive(entry.Pid))
                {
                    try { File.Delete(file); } catch { }
                    continue;
                }
                results.Add(entry);
            }
            catch { }
        }
        return results;
    }

    private static bool PidAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
