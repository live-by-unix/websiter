using Websiter;
using Xunit;

namespace Websiter.Core.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "websiter-core-" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Config.Reset();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void JsonConfig_LoadsValues()
    {
        File.WriteAllText(Path.Combine(_root, "websiter.json"), """
            { "application": { "name": "TestApp" }, "server": { "port": 9999 } }
            """);
        var config = WebsiterConfig.Load(_root);
        Assert.Equal("TestApp", Config.Get("application:name"));
        Assert.Equal("9999", Config.Get("server:port"));
    }

    [Fact]
    public void EnvironmentSpecific_Overrides()
    {
        File.WriteAllText(Path.Combine(_root, "websiter.json"), """{ "server": { "port": 1111 } }""");
        File.WriteAllText(Path.Combine(_root, "websiter.Production.json"), """{ "server": { "port": 2222 } }""");
        WebsiterEnvironment.Initialize("Production");
        WebsiterConfig.Load(_root);
        Assert.Equal("2222", Config.Get("server:port"));
    }

    [Fact]
    public void EnvVar_OverridesJson()
    {
        File.WriteAllText(Path.Combine(_root, "websiter.json"), """{ "server": { "port": 1111 } }""");
        Environment.SetEnvironmentVariable("WEBSITER_SERVER__PORT", "3333");
        try
        {
            WebsiterConfig.Load(_root);
            Assert.Equal("3333", Config.Get("server:port"));
        }
        finally { Environment.SetEnvironmentVariable("WEBSITER_SERVER__PORT", null); }
    }

    [Fact]
    public void InMemory_CreatesConfig()
    {
        WebsiterConfig.CreateInMemory(new Dictionary<string, string?> { ["a:b"] = "c" });
        Assert.Equal("c", Config.Get("a:b"));
    }
}

public class EnvironmentTests
{
    [Fact]
    public void Defaults_Production()
    {
        WebsiterEnvironment.Initialize("Production");
        Assert.True(WebsiterEnvironment.IsProduction);
        Assert.False(WebsiterEnvironment.IsDevelopment);
    }

    [Fact]
    public void Development_Flags()
    {
        WebsiterEnvironment.Initialize("Development");
        Assert.True(WebsiterEnvironment.IsDevelopment);
        Assert.False(WebsiterEnvironment.IsProduction);
    }
}

public class PathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "websiter-paths-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void StateDirFor_DoesNotMutate()
    {
        var dir = Paths.StateDirFor(_root);
        Assert.EndsWith(".websiter", dir);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), ".websiter"), dir);
    }
}
