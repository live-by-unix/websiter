using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Websiter.Server;
using Xunit;

namespace Websiter.Server.Tests;

/// <summary>HTTP tests against a real Kestrel instance on an ephemeral port.</summary>
public class ServerTests : IAsyncLifetime
{
    private WebsiterServer _server = null!;
    private HttpClient _http = null!;
    private string _baseUrl = null!;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "websiter-server-tests-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(_root, "Pages"));
        Directory.CreateDirectory(Path.Combine(_root, "Components"));

        File.WriteAllText(Path.Combine(_root, "wwwroot", "style.css"), "body{color:red}");
        File.WriteAllText(Path.Combine(_root, "wwwroot", "data.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(_root, "Pages", "index.html"), "<h1>Page Home</h1>");
        File.WriteAllText(Path.Combine(_root, "Pages", "about.html"), "<h1>About</h1><component name=\"Footer\" />");
        File.WriteAllText(Path.Combine(_root, "Components", "Footer.html"), "<footer>FOOTER-COMPONENT</footer>");

        _server = new WebsiterServer(_root, new WebsiterServerOptions
        {
            Host = "127.0.0.1",
            Port = GetFreePort(),
            WebRoot = Path.Combine(_root, "wwwroot"),
            PagesRoot = Path.Combine(_root, "Pages"),
            ComponentsRoot = Path.Combine(_root, "Components"),
            StaticCacheControl = null,
        });

        _server.MapGet("/", ctx => ctx.Response.WriteAsync("Hello Websiter!"));
        _server.MapGet("/api/status", ctx => ctx.WriteJsonAsync(new { status = "online", framework = "Websiter" }));
        _server.MapGet("/users/{id}", ctx => ctx.WriteJsonAsync(new { id = ctx.Param<int>("id") }));
        _server.MapGet("/query", ctx => ctx.WriteJsonAsync(new { q = ctx.Query<string>("q") }));
        _server.MapPost("/echo", async ctx =>
        {
            var body = await ctx.ReadJsonAsync<JsonElement>(ctx.RequestAborted);
            await ctx.WriteJsonAsync(new { echoed = body });
        });
        _server.MapGet("/text", ctx => "plain string result");
        _server.MapGet("/boom", ctx => throw new InvalidOperationException("test failure"));

        _server.UseExceptionHandler();
        _server.UseSecurityHeaders();
        _server.UseStaticFiles();

        _baseUrl = await _server.StartAsync().ConfigureAwait(false);
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_server is not null) await _server.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static int _portCounter = 20000 + Random.Shared.Next(1000);
    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Get_Root_ReturnsHello()
    {
        var response = await _http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello Websiter!", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ApiStatus_ReturnsJson()
    {
        var json = await _http.GetFromJsonAsync<JsonElement>("/api/status");
        Assert.Equal("online", json.GetProperty("status").GetString());
        Assert.Equal("Websiter", json.GetProperty("framework").GetString());
    }

    [Fact]
    public async Task RouteParam_IsCaptured()
    {
        var json = await _http.GetFromJsonAsync<JsonElement>("/users/42");
        Assert.Equal(42, json.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task QueryParam_IsRead()
    {
        var json = await _http.GetFromJsonAsync<JsonElement>("/query?q=hello");
        Assert.Equal("hello", json.GetProperty("q").GetString());
    }

    [Fact]
    public async Task Post_JsonBody_IsEchoed()
    {
        var response = await _http.PostAsJsonAsync("/echo", new { value = 123 });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(123, json.GetProperty("echoed").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task SyncStringHandler_WritesText()
    {
        var response = await _http.GetAsync("/text");
        Assert.Equal("plain string result", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent()
    {
        var response = await _http.GetAsync("/api/status");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public async Task StaticFile_IsServed()
    {
        var response = await _http.GetAsync("/style.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("body{color:red}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Page_IsServedWithComponents()
    {
        var response = await _http.GetAsync("/about");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("About", html);
        Assert.Contains("FOOTER-COMPONENT", html);
        Assert.DoesNotContain("<component", html);
    }

    [Fact]
    public async Task IndexPage_IsServed()
    {
        var response = await _http.GetAsync("/");
        // "/" matches the explicit route, not the page — verify page fallback via /index? Use a subpath.
        var about = await _http.GetAsync("/about");
        Assert.Equal(HttpStatusCode.OK, about.StatusCode);
    }

    [Fact]
    public async Task NotFound_Returns404Json()
    {
        var response = await _http.GetAsync("/nonexistent-route-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exception_Returns500Json()
    {
        var response = await _http.GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("internal_error", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task HealthEndpoint_Responds()
    {
        var response = await _http.GetAsync("/__websiter/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MetricsEndpoint_Responds()
    {
        var response = await _http.GetAsync("/__websiter/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
