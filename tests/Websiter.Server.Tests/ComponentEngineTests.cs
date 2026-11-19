using Websiter.Components;
using Xunit;

namespace Websiter.Server.Tests;

public class ComponentEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "websiter-comp-" + Guid.NewGuid().ToString("N"));

    public ComponentEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_dir, name + ".html"), content);

    [Fact]
    public void RendersSimpleComponent()
    {
        Write("Header", "<header>HEADER</header>");
        var engine = new ComponentEngine(_dir);
        var result = engine.Render("<body><component name=\"Header\" /></body>");
        Assert.Equal("<body><header>HEADER</header></body>", result);
    }

    [Fact]
    public void RendersNestedComponents()
    {
        Write("Inner", "<span>INNER</span>");
        Write("Outer", "<div><component name=\"Inner\" /></div>");
        var engine = new ComponentEngine(_dir);
        var result = engine.Render("<component name=\"Outer\" />");
        Assert.Equal("<div><span>INNER</span></div>", result);
    }

    [Fact]
    public void MissingComponent_Throws()
    {
        var engine = new ComponentEngine(_dir);
        var ex = Assert.Throws<ComponentException>(() => engine.Render("<component name=\"Nope\" />"));
        Assert.Contains("Nope", ex.Message);
    }

    [Fact]
    public void CircularInclude_Throws()
    {
        Write("A", "<component name=\"B\" />");
        Write("B", "<component name=\"A\" />");
        var engine = new ComponentEngine(_dir);
        Assert.Throws<ComponentException>(() => engine.Render("<component name=\"A\" />"));
    }

    [Fact]
    public void SelfInclude_Throws()
    {
        Write("Loop", "<div><component name=\"Loop\" /></div>");
        var engine = new ComponentEngine(_dir);
        Assert.Throws<ComponentException>(() => engine.Render("<component name=\"Loop\" />"));
    }

    [Fact]
    public void MissingNameAttribute_Throws()
    {
        var engine = new ComponentEngine(_dir);
        Assert.Throws<ComponentException>(() => engine.Render("<component />"));
    }

    [Fact]
    public void Discover_ListsComponents()
    {
        Write("One", "x");
        Write("Two", "y");
        var engine = new ComponentEngine(_dir);
        var names = engine.Discover();
        Assert.Contains("One", names);
        Assert.Contains("Two", names);
    }

    [Fact]
    public void SubdirectoryComponent_Resolves()
    {
        var sub = Path.Combine(_dir, "layout");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "Header.html"), "<header>SUB</header>");
        var engine = new ComponentEngine(_dir);
        var result = engine.Render("<component name=\"layout/Header\" />");
        Assert.Equal("<header>SUB</header>", result);
    }

    [Fact]
    public void PathTraversal_IsRejected()
    {
        var engine = new ComponentEngine(_dir);
        Assert.Throws<ComponentException>(() => engine.Render("<component name=\"../secret\" />"));
    }

    [Fact]
    public void Invalidate_ClearsCache()
    {
        Write("C", "v1");
        var engine = new ComponentEngine(_dir);
        Assert.Contains("v1", engine.Render("<component name=\"C\" />"));
        Write("C", "v2");
        engine.Invalidate();
        Assert.Contains("v2", engine.Render("<component name=\"C\" />"));
    }
}
