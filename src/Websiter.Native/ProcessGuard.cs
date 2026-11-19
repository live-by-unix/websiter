using System.Diagnostics;

namespace Websiter.Native;

/// <summary>Cross-platform process inspection helpers.</summary>
public static class ProcessGuard
{
    /// <summary>Returns true when a process with the given pid exists and has not exited.</summary>
    public static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Working set size in bytes for a process, or null when unavailable.</summary>
    public static long? WorkingSetBytes(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();
            return process.WorkingSet64;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>CPU time consumed by a process, or null when unavailable.</summary>
    public static TimeSpan? TotalProcessorTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();
            return process.TotalProcessorTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sends SIGTERM on Unix; force-kills on Windows. Returns true when the process exited.</summary>
    public static async Task<bool> TryTerminateAsync(int pid, int timeoutMs, CancellationToken ct = default)
    {
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
                p.Kill(entireProcessTree: false);
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!IsAlive(pid)) return true;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return !IsAlive(pid);
        }
        catch
        {
            return false;
        }
    }
}
