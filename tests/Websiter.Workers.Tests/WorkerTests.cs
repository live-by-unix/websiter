using Websiter;
using Websiter.Workers;
using Xunit;

namespace Websiter.Workers.Tests;

public class DiscoveryTests
{
    [Fact]
    public void DiscoversWorkerClasses_FromFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "websiter-workers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "AuthWorker.cs"), """
                public class AuthWorker : Worker { }
                """);
            File.WriteAllText(Path.Combine(dir, "NotAWorker.cs"), """
                public class NotAWorker { }
                """);

            var found = WorkerDiscovery.DiscoverFromFiles(dir);
            Assert.Single(found);
            Assert.Equal("AuthWorker", found[0].Name);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void DiscoversWorkerClasses_InAssembly()
    {
        var found = WorkerDiscovery.DiscoverInAssembly(typeof(TestWorker).Assembly);
        Assert.Contains(found, w => w.Name == nameof(TestWorker));
    }

    public class TestWorker : Worker
    {
        public override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public class SupervisorOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new WorkerSupervisorOptions();
        Assert.True(options.AutoRestart);
        Assert.Equal(5, options.RestartLimit);
        Assert.True(options.RestartDelayMs > 0);
    }

    [Fact]
    public void UnknownDependency_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "websiter-sup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var defs = new[]
            {
                new WorkerDefinition { Name = "A", DependsOn = { "Missing" } },
            };
            Assert.Throws<WorkerException>(() => new WorkerSupervisor(root, defs));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
