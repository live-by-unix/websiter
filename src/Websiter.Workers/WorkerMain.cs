using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Websiter.Workers;

/// <summary>
/// Entry point for worker processes. Usage from a worker's Program.cs:
/// <code>return await Websiter.Workers.WorkerMain.RunAsync&lt;AuthWorker&gt;(args);</code>
/// </summary>
public static class WorkerMain
{
    /// <summary>
    /// Runs a worker in its own process: initializes paths, configuration,
    /// environment and logging, then executes the worker until shutdown.
    /// Returns the process exit code.
    /// </summary>
    public static async Task<int> RunAsync<TWorker>(string[] args) where TWorker : Worker, new()
    {
        var workerName = typeof(TWorker).Name;
        string? root = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--root" or "-r") root = args[i + 1];
        }

        try
        {
            root ??= FindProjectRoot(Directory.GetCurrentDirectory());
            Paths.Initialize(root);

            WebsiterEnvironment.InitializeFromEnvironment();
            var config = WebsiterConfig.Load(root);
            WebsiterEnvironment.Initialize(config["application:environment"] ?? WebsiterEnvironment.Name);

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                });
                builder.AddProvider(new FileLoggerProvider(Paths.LogFile($"worker-{workerName}")));
            });
            Log.Initialize(loggerFactory.CreateLogger("Websiter.Worker"));

            using var cts = new CancellationTokenSource();
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; cts.Cancel(); });
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); });

            var worker = new TWorker();
            worker.Prepare(); // bind IPC hub to this worker before handlers register
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                await worker.ExecuteAsync(cts.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal($"Worker '{workerName}' failed: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Dispatches a worker process launched with <c>--websiter-worker=&lt;name&gt;</c>.
    /// Locates the <see cref="Worker"/> subclass in <paramref name="assembly"/>
    /// whose <see cref="Worker.Name"/> matches and runs it.
    /// </summary>
    public static async Task<int> RunWorkerAsync(System.Reflection.Assembly assembly, string workerName, string[] args)
    {
        var type = FindWorkerType(assembly, workerName)
            ?? throw new WorkerException($"No worker named '{workerName}' found in {assembly.GetName().Name}.");

        var method = typeof(WorkerMain).GetMethod(nameof(RunAsync),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var generic = method.MakeGenericMethod(type);
        var task = (Task<int>)generic.Invoke(null, new object[] { args })!;
        return await task.ConfigureAwait(false);
    }

    /// <summary>Finds a concrete <see cref="Worker"/> type by name in an assembly.</summary>
    public static Type? FindWorkerType(System.Reflection.Assembly assembly, string workerName)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(Worker).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;
            if (string.Equals(type.Name, workerName, StringComparison.OrdinalIgnoreCase)) return type;
        }

        // Fall back: instantiate-free name check failed; try matching via a temporary instance is unsafe,
        // so report not found.
        return null;
    }

    /// <summary>Finds the project root by walking up looking for websiter.json or a .csproj.</summary>
    public static string FindProjectRoot(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "websiter.json"))) return dir.FullName;
            if (Directory.EnumerateFiles(dir.FullName, "*.csproj").Any()) return dir.FullName;
            dir = dir.Parent;
        }
        return startDir;
    }
}
