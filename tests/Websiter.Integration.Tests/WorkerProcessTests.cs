using System.Diagnostics;
using System.Text.Json;
using Websiter.Ipc;
using Websiter.Workers;
using Xunit;

namespace Websiter.Integration.Tests;

/// <summary>
/// End-to-end tests that launch real independent worker OS processes and
/// exchange IPC messages. These are the acceptance tests for the worker
/// architecture: separate processes, real transports, real crashes/restarts.
/// </summary>
public class WorkerProcessTests : IAsyncLifetime
{
    private static readonly string _hostDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Websiter.WorkerHost");
    private static readonly string _hostDll = Path.Combine(_hostDir, "bin", "Debug", "net10.0", "Websiter.WorkerHost.dll");
    private static readonly SemaphoreSlim _buildLock = new(1, 1);
    private static bool _built;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "websiter-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly List<Process> _processes = new();

    public WorkerProcessTests() => Directory.CreateDirectory(_root);

    public async Task InitializeAsync()
    {
        // Ensure the worker host is built once for the whole test run.
        if (_built) return;
        await _buildLock.WaitAsync();
        try
        {
            if (_built) return;
            var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(_hostDir, "Websiter.WorkerHost.csproj")}\" -c Debug --nologo -v q")
            {
                WorkingDirectory = _hostDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var build = Process.Start(psi)!;
            await build.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
            Assert.True(File.Exists(_hostDll), $"WorkerHost build failed. Output: {await build.StandardOutput.ReadToEndAsync()}");
            _built = true;
        }
        finally { _buildLock.Release(); }
    }

    public Task DisposeAsync()
    {
        IpcHub.ResetForTests();
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { try { process.Kill(); } catch { } }
                    process.WaitForExit(8000);
                }
            }
            catch { }
            process.Dispose();
        }
        _processes.Clear();
        try { Directory.Delete(_root, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    private Process StartWorker(string name, string appName, string ipcDir)
    {
        var psi = new ProcessStartInfo("dotnet",
            $"exec \"{_hostDll}\" --websiter-worker={name} --root \"{_root}\"")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["WEBSITER_APP"] = appName;
        psi.Environment["WEBSITER_IPC_DIR"] = ipcDir;
        psi.Environment["WEBSITER_ENV"] = "Development";
        var process = Process.Start(psi)!;
        _processes.Add(process);
        return process;
    }

    private static async Task WaitForWorker(IpcHub hub, string workerName, Process process)
    {
        // The socket path may live under a hashed fallback dir, so probe via IPC.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            try
            {
                var pong = await hub.RequestAsync<JsonElement>(workerName, "$ping", timeout: TimeSpan.FromSeconds(2));
                if (pong.ValueKind == JsonValueKind.Object) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(300);
        }
        Assert.Fail($"Worker '{workerName}' did not respond to $ping. Exited={process.HasExited}");
    }

    private IpcHub NewClientHub(string appName, string ipcDir)
        => new(new IpcOptions
        {
            ApplicationName = appName,
            Directory = ipcDir,
            RequestTimeout = TimeSpan.FromSeconds(15),
        }, "test-client");

    [Fact]
    public async Task Worker_Starts_And_RespondsToIpc()
    {
        var ipcDir = Path.Combine(_root, ".websiter", "ipc");
        var process = StartWorker("TestWorker", "e2e", ipcDir);

        await using var hub = NewClientHub("e2e", ipcDir);
        await hub.StartServerAsync();
        await WaitForWorker(hub, "TestWorker", process);
        var pong = await hub.RequestAsync<JsonElement>("TestWorker", "$ping", timeout: TimeSpan.FromSeconds(10));
        Assert.True(pong.GetProperty("pong").GetBoolean());
        Assert.Equal("TestWorker", pong.GetProperty("worker").GetString());

        var result = await hub.RequestAsync<JsonElement>("TestWorker", "Add", new { a = 20, b = 22 });
        Assert.Equal(42, result.GetProperty("sum").GetInt32());
    }

    [Fact]
    public async Task Worker_GracefulShutdown_ViaIpc()
    {
        var ipcDir = Path.Combine(_root, ".websiter", "ipc-shutdown");
        var process = StartWorker("TestWorker", "e2e-shutdown", ipcDir);

        await using var hub = NewClientHub("e2e-shutdown", ipcDir);
        await hub.StartServerAsync();
        await WaitForWorker(hub, "TestWorker", process);
        await hub.RequestAsync<object>("TestWorker", "$shutdown", timeout: TimeSpan.FromSeconds(10));

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(process.HasExited);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task Worker_Crash_ExitsNonZero()
    {
        var ipcDir = Path.Combine(_root, ".websiter", "ipc-crash");
        var process = StartWorker("CrashyWorker", "e2e-crash", ipcDir);

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(process.HasExited);
        Assert.NotEqual(0, process.ExitCode);
    }

    [Fact]
    public async Task Workers_Communicate_OverIpc()
    {
        // RelayWorker forwards requests to TestWorker — proves worker→worker IPC
        // and that multiple worker processes coexist.
        var ipcDir = Path.Combine(_root, ".websiter", "ipc-relay");
        var testWorker = StartWorker("TestWorker", "e2e-relay", ipcDir);
        var relay = StartWorker("RelayWorker", "e2e-relay", ipcDir);

        await using var hub = NewClientHub("e2e-relay", ipcDir);
        await hub.StartServerAsync();
        await WaitForWorker(hub, "TestWorker", testWorker);
        await WaitForWorker(hub, "RelayWorker", relay);
        var result = await hub.RequestAsync<JsonElement>("RelayWorker", "Relay", new { msg = "x" },
            timeout: TimeSpan.FromSeconds(15));
        Assert.Equal("relayed", result.GetProperty("echo").GetString());
    }
}
