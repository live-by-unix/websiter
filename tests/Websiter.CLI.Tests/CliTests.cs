using System.Text.Json;
using System.Text.Json.Nodes;
using Websiter.CLI;
using Websiter.CLI.Commands;
using Xunit;

namespace Websiter.CLI.Tests;

public class NewCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "websiter-cli-" + Guid.NewGuid().ToString("N"));

    public NewCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task New_Basic_CreatesProject()
    {
        var templatesRoot = NewCommand.FindTemplatesRoot();
        Assert.NotNull(templatesRoot);

        var code = await NewCommand.RunAsync(new[] { "TestSite", "--template", "basic", "--dir", _dir, "--non-interactive" });
        Assert.Equal(0, code);

        var project = Path.Combine(_dir, "TestSite");
        Assert.True(File.Exists(Path.Combine(project, "websiter.json")));
        Assert.True(File.Exists(Path.Combine(project, "Program.cs")));
        Assert.True(Directory.Exists(Path.Combine(project, "wwwroot")));

        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "websiter.json")));
        Assert.Equal("TestSite", json.RootElement.GetProperty("application").GetProperty("name").GetString());
    }

    [Fact]
    public async Task New_FullStack_CreatesWorkersAndComponents()
    {
        var code = await NewCommand.RunAsync(new[] { "FullApp", "--template", "fullstack", "--dir", _dir, "--non-interactive" });
        Assert.Equal(0, code);
        var project = Path.Combine(_dir, "FullApp");
        Assert.True(File.Exists(Path.Combine(project, "Workers", "AnalyticsWorker.cs")));
        Assert.True(File.Exists(Path.Combine(project, "Components", "Navbar.html")));
        Assert.True(File.Exists(Path.Combine(project, "Pages", "index.html")));
    }

    [Fact]
    public async Task New_Distributed_CreatesMultipleWorkers()
    {
        var code = await NewCommand.RunAsync(new[] { "DistApp", "--template", "distributed", "--dir", _dir, "--non-interactive" });
        Assert.Equal(0, code);
        var workers = Directory.GetFiles(Path.Combine(_dir, "DistApp", "Workers"), "*.cs");
        Assert.True(workers.Length >= 3);
    }

    [Fact]
    public async Task New_InvalidTemplate_Fails()
    {
        var code = await Assert.ThrowsAsync<Program.CliUsageException>(() =>
            NewCommand.RunAsync(new[] { "X", "--template", "nonexistent", "--dir", _dir, "--non-interactive" }));
        Assert.NotNull(code);
    }

    [Fact]
    public async Task New_ExistingDir_Fails()
    {
        var project = Path.Combine(_dir, "Taken");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "file.txt"), "x");
        await Assert.ThrowsAsync<Program.CliUsageException>(() =>
            NewCommand.RunAsync(new[] { "Taken", "--dir", _dir, "--non-interactive" }));
    }
}

public class ConfigFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "websiter-config-" + Guid.NewGuid().ToString("N"));

    public ConfigFileTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void LoadSave_RoundTrips()
    {
        var cfg = CliSupport.LoadConfigFile(_root);
        cfg["application"] = new JsonObject { ["name"] = "RoundTrip" };
        CliSupport.SaveConfigFile(_root, cfg);

        var loaded = CliSupport.LoadConfigFile(_root);
        Assert.Equal("RoundTrip", loaded["application"]?["name"]?.GetValue<string>());
        Assert.Equal("RoundTrip", CliSupport.AppName(_root));
    }
}
