using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Websiter.Components;
using Websiter.Diagnostics;
using Websiter.Routing;
using Websiter.Security;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace Websiter.Server;

/// <summary>
/// The Websiter HTTP server: Kestrel-backed request pipeline with routing,
/// middleware, static files, HTML pages with components, WebSockets,
/// security headers, CORS, compression and rate limiting.
/// </summary>
public sealed class WebsiterServer : IAsyncDisposable
{
    private readonly string _root;
    private readonly List<Func<HttpContext, Func<Task>, Task>> _middlewares = new();
    private readonly List<(string Pattern, Func<WebSocket, HttpContext, CancellationToken, Task> Handler)> _webSockets = new();
    private readonly RouteTable _wsRoutes = new();
    private bool _useCompression;
    private bool _useCors;
    private bool _useWebSockets;
    private WebApplication? _builtApp;
    private IHost? _host;
    private CancellationTokenSource? _runCts;

    public WebsiterServer(string root, WebsiterServerOptions? options = null)
    {
        _root = Path.GetFullPath(root);
        Options = options ?? WebsiterServerOptions.FromConfig(_root);
        Routes = new RouteTable();
        Components = new ComponentEngine(Options.ComponentsRoot ?? Path.Combine(_root, "Components"),
            new ComponentEngineOptions { EnableCache = !WebsiterEnvironment.IsDevelopment });
        RateLimiter = new RateLimiter();
        SecurityHeaders = SecurityHeadersOptions.FromConfig();
        Cors = CorsOptions.FromConfig();
        Health = new HealthHub();
        Metrics = MetricsHub.Current;
        RegisterDiagnosticsRoutes();
    }

    /// <summary>Resolved server options.</summary>
    public WebsiterServerOptions Options { get; }

    /// <summary>The route table.</summary>
    public RouteTable Routes { get; }

    /// <summary>The HTML component engine.</summary>
    public ComponentEngine Components { get; }

    /// <summary>Rate limiter instance.</summary>
    public RateLimiter RateLimiter { get; }

    /// <summary>Security headers options.</summary>
    public SecurityHeadersOptions SecurityHeaders { get; }

    /// <summary>CORS options.</summary>
    public CorsOptions Cors { get; }

    /// <summary>Health check hub.</summary>
    public HealthHub Health { get; }

    /// <summary>Request metrics hub.</summary>
    public MetricsHub Metrics { get; }

    /// <summary>Gets the effective listen URL.</summary>
    public string Url => Options.GetUrl();

    // ------------------------- Routing -------------------------

    private WebsiterServer MapCore(RouteMethod method, string pattern, Delegate handler, string? name)
    {
        Routes.Add(method, pattern, handler, name);
        return this;
    }

    /// <summary>Maps a GET route.</summary>
    public WebsiterServer MapGet(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Get, pattern, handler, name);
    /// <inheritdoc cref="MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapGet(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Get, pattern, handler, name);
    /// <inheritdoc cref="MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapGet(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Get, pattern, handler, name);
    /// <inheritdoc cref="MapGet(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapGet(string pattern, Delegate handler, string? name = null)
        => MapCore(RouteMethod.Get, pattern, handler, name);

    /// <summary>Maps a POST route.</summary>
    public WebsiterServer MapPost(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Post, pattern, handler, name);
    /// <inheritdoc cref="MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPost(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Post, pattern, handler, name);
    /// <inheritdoc cref="MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPost(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Post, pattern, handler, name);
    /// <inheritdoc cref="MapPost(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPost(string pattern, Delegate handler, string? name = null)
        => MapCore(RouteMethod.Post, pattern, handler, name);

    /// <summary>Maps a PUT route.</summary>
    public WebsiterServer MapPut(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Put, pattern, handler, name);
    /// <inheritdoc cref="MapPut(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPut(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Put, pattern, handler, name);
    /// <inheritdoc cref="MapPut(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPut(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Put, pattern, handler, name);

    /// <summary>Maps a DELETE route.</summary>
    public WebsiterServer MapDelete(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Delete, pattern, handler, name);
    /// <inheritdoc cref="MapDelete(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapDelete(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Delete, pattern, handler, name);
    /// <inheritdoc cref="MapDelete(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapDelete(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Delete, pattern, handler, name);

    /// <summary>Maps a PATCH route.</summary>
    public WebsiterServer MapPatch(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Patch, pattern, handler, name);
    /// <inheritdoc cref="MapPatch(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPatch(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Patch, pattern, handler, name);
    /// <inheritdoc cref="MapPatch(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer MapPatch(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Patch, pattern, handler, name);

    /// <summary>Maps a route for any HTTP method.</summary>
    public WebsiterServer Map(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Any, pattern, handler, name);
    /// <inheritdoc cref="Map(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer Map(string pattern, Func<HttpContext, CancellationToken, Task> handler, string? name = null)
        => MapCore(RouteMethod.Any, pattern, handler, name);
    /// <inheritdoc cref="Map(string, Func{HttpContext, Task}, string?)"/>
    public WebsiterServer Map(string pattern, Func<HttpContext, object?> handler, string? name = null)
        => MapCore(RouteMethod.Any, pattern, handler, name);

    /// <summary>Maps a HEAD route.</summary>
    public WebsiterServer MapHead(string pattern, Func<HttpContext, Task> handler, string? name = null)
        => MapCore(RouteMethod.Head, pattern, handler, name);

    /// <summary>Maps a WebSocket endpoint.</summary>
    public WebsiterServer MapWebSocket(string pattern, Func<WebSocket, HttpContext, CancellationToken, Task> handler)
    {
        _useWebSockets = true;
        _wsRoutes.Add(RouteMethod.Get, pattern, (Delegate)handler);
        return this;
    }

    // ------------------------- Middleware -------------------------

    /// <summary>Adds custom middleware to the pipeline.</summary>
    public WebsiterServer Use(Func<HttpContext, Func<Task>, Task> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    private bool _securityHeadersAdded;
    /// <summary>Adds security headers to all responses.</summary>
    public WebsiterServer UseSecurityHeaders()
    {
        if (_securityHeadersAdded) return this;
        _securityHeadersAdded = true;
        return Use(async (ctx, next) =>
        {
            if (SecurityHeaders.Enabled)
            {
                var h = ctx.Response.Headers;
                h["X-Content-Type-Options"] = SecurityHeaders.XContentTypeOptions;
                h["X-Frame-Options"] = SecurityHeaders.XFrameOptions;
                h["Referrer-Policy"] = SecurityHeaders.ReferrerPolicy;
                if (SecurityHeaders.ContentSecurityPolicy is not null) h["Content-Security-Policy"] = SecurityHeaders.ContentSecurityPolicy;
                if (SecurityHeaders.PermissionsPolicy is not null) h["Permissions-Policy"] = SecurityHeaders.PermissionsPolicy;
                if (SecurityHeaders.StrictTransportSecurity) h["Strict-Transport-Security"] = SecurityHeaders.StrictTransportSecurityValue;
            }
            await next().ConfigureAwait(false);
        });
    }

    private bool _rateLimitingAdded;
    /// <summary>Adds per-IP fixed-window rate limiting.</summary>
    public WebsiterServer UseRateLimiting()
    {
        if (_rateLimitingAdded) return this;
        _rateLimitingAdded = true;
        return Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "/";
            var opts = _rateLimitOptions ??= RateLimitOptions.FromConfig();
            var whitelisted = opts.WhitelistPaths?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true;

            if (!whitelisted)
            {
                var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (!RateLimiter.TryAcquire(key))
                {
                    ctx.Response.StatusCode = 429;
                    ctx.Response.Headers.RetryAfter = RateLimiter.RetryAfterSeconds(key).ToString();
                    await ctx.Response.WriteAsJsonAsync(new { error = "rate_limited", message = "Too many requests." }, ctx.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }
            await next().ConfigureAwait(false);
        });
    }

    private RateLimitOptions? _rateLimitOptions;

    /// <summary>Adds response compression (handled by ASP.NET Core middleware).</summary>
    public WebsiterServer UseCompression()
    {
        _useCompression = true;
        return this;
    }

    /// <summary>Adds CORS using configured origins (handled by ASP.NET Core middleware).</summary>
    public WebsiterServer UseCors()
    {
        _useCors = true;
        return this;
    }

    private bool _exceptionHandlerAdded;
    /// <summary>Adds global exception handling returning a JSON 500.</summary>
    public WebsiterServer UseExceptionHandler()
    {
        if (_exceptionHandlerAdded) return this;
        _exceptionHandlerAdded = true;
        return Use(async (ctx, next) =>
        {
            try
            {
                await next().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Unhandled request error for {ctx.Request.Method} {ctx.Request.Path}: {ex}");
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.ContentType = "application/json";
                    var body = WebsiterEnvironment.IsDevelopment
                        ? new { error = "internal_error", message = ex.Message, stack = ex.StackTrace }
                        : (object)new { error = "internal_error", message = "An unexpected error occurred." };
                    await ctx.Response.WriteAsJsonAsync(body, ctx.RequestAborted).ConfigureAwait(false);
                }
            }
        });
    }

    private bool _staticFilesAdded;
    /// <summary>
    /// Adds static file serving from the web root plus HTML page rendering.
    /// Explicitly mapped routes take precedence over static files and pages.
    /// </summary>
    public WebsiterServer UseStaticFiles()
    {
        if (_staticFilesAdded) return this;
        _staticFilesAdded = true;
        return Use(async (ctx, next) =>
        {
            // Explicit routes win over file/page fallbacks.
            if (Routes.Match(ctx.Request.Method, ctx.Request.Path.Value ?? "/") is not null)
            {
                await next().ConfigureAwait(false);
                return;
            }
            if (await TryServeStaticAsync(ctx).ConfigureAwait(false)) return;
            if (await TryServePageAsync(ctx).ConfigureAwait(false)) return;
            await next().ConfigureAwait(false);
        });
    }

    // ------------------------- Pipeline -------------------------

    private async Task TerminalPipelineAsync(HttpContext ctx)
    {
        var sw = Stopwatch.StartNew();

        Func<Task> next = () => RouteRequestAsync(ctx);
        // Compose middleware in reverse so the first registered runs outermost.
        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var mw = _middlewares[i];
            var inner = next;
            next = () => mw(ctx, inner);
        }

        try
        {
            await next().ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            var routeLabel = ctx.Items["Websiter.Route"] as string ?? ctx.Request.Path.Value ?? "/";
            Metrics.RecordRequest(routeLabel, ctx.Response.StatusCode, sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task RouteRequestAsync(HttpContext ctx)
    {
        // WebSocket upgrade?
        if (_useWebSockets && ctx.WebSockets.IsWebSocketRequest)
        {
            var wsMatch = _wsRoutes.Match(ctx.Request.Method, ctx.Request.Path.Value ?? "/");
            if (wsMatch is not null)
            {
                using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                var handler = (Func<WebSocket, HttpContext, CancellationToken, Task>)wsMatch.Route.Handler;
                await handler(socket, ctx, ctx.RequestAborted).ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 400;
            return;
        }

        var path = ctx.Request.Path.Value ?? "/";
        var match = Routes.Match(ctx.Request.Method, path);
        if (match is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { error = "not_found", path }, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        ctx.SetRouteValues(match.Values);
        ctx.Items["Websiter.Route"] = match.Route.Pattern;
        var invoker = HandlerInvoker.Compile(match.Route.Handler);
        await invoker(ctx, ctx.RequestAborted).ConfigureAwait(false);
    }

    // ------------------------- Static files & pages -------------------------

    private async Task<bool> TryServeStaticAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)) return false;
        var webRoot = Options.WebRoot ?? Path.Combine(_root, "wwwroot");
        if (!Directory.Exists(webRoot)) return false;

        var relative = (ctx.Request.Path.Value ?? "/").TrimStart('/');
        var filePath = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!filePath.StartsWith(Path.GetFullPath(webRoot), StringComparison.Ordinal)) return false;

        if (Directory.Exists(filePath))
        {
            var index = Path.Combine(filePath, "index.html");
            if (File.Exists(index)) filePath = index;
            else return false;
        }
        if (!File.Exists(filePath)) return false;

        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            // HTML files under wwwroot also get component processing.
            var html = await File.ReadAllTextAsync(filePath, ctx.RequestAborted).ConfigureAwait(false);
            var rendered = Components.Render(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(rendered, ctx.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            ctx.Response.ContentType = Websiter.Frontend.AssetPipeline.ContentTypeFor(filePath);
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (Options.StaticCacheControl is not null)
                ctx.Response.Headers.CacheControl = Options.StaticCacheControl;
            await ctx.Response.SendFileAsync(filePath, ctx.RequestAborted).ConfigureAwait(false);
        }
        return true;
    }

    private async Task<bool> TryServePageAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)) return false;
        var pagesRoot = Options.PagesRoot ?? Path.Combine(_root, "Pages");
        if (!Directory.Exists(pagesRoot)) return false;

        var path = (ctx.Request.Path.Value ?? "/").Trim('/');
        string? file = null;
        if (string.IsNullOrEmpty(path))
        {
            file = Path.Combine(pagesRoot, "index.html");
        }
        else
        {
            var candidate = Path.Combine(pagesRoot, path.Replace('/', Path.DirectorySeparatorChar) + ".html");
            var full = Path.GetFullPath(candidate);
            if (full.StartsWith(Path.GetFullPath(pagesRoot), StringComparison.Ordinal) && File.Exists(full))
                file = full;
            else
            {
                var index = Path.Combine(pagesRoot, path.Replace('/', Path.DirectorySeparatorChar), "index.html");
                var fullIndex = Path.GetFullPath(index);
                if (fullIndex.StartsWith(Path.GetFullPath(pagesRoot), StringComparison.Ordinal) && File.Exists(fullIndex))
                    file = fullIndex;
            }
        }

        if (file is null || !File.Exists(file)) return false;

        var html = await File.ReadAllTextAsync(file, ctx.RequestAborted).ConfigureAwait(false);
        var rendered = Components.Render(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(rendered, ctx.RequestAborted).ConfigureAwait(false);
        return true;
    }

    private void RegisterDiagnosticsRoutes()
    {
        MapGet("/__websiter/health", async (HttpContext ctx) =>
        {
            var report = await Health.CheckAsync(ctx.RequestAborted).ConfigureAwait(false);
            var code = report.Status == HealthStatus.Healthy ? 200 : report.Status == HealthStatus.Degraded ? 200 : 503;
            await ctx.WriteJsonAsync(new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                timestamp = report.Timestamp,
                checks = report.Checks.Select(c => new { name = c.Name, status = c.Status.ToString().ToLowerInvariant(), description = c.Description }),
            }, code, ctx.RequestAborted).ConfigureAwait(false);
        });

        MapGet("/__websiter/metrics", (HttpContext ctx) =>
            ctx.WriteJsonAsync(Metrics.Snapshot(), 200, ctx.RequestAborted));

        MapGet("/__websiter/status", (HttpContext ctx) =>
            ctx.WriteJsonAsync(new
            {
                framework = "Websiter",
                version = typeof(WebsiterServer).Assembly.GetName().Version?.ToString(),
                environment = WebsiterEnvironment.Name,
                server = new { url = Url, uptimeSeconds = (DateTimeOffset.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds },
            }, 200, ctx.RequestAborted));
    }

    // ------------------------- Run -------------------------

    /// <summary>Builds and starts the Kestrel host. Blocks until cancellation.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _builtApp = BuildApplication();
        _host = _builtApp;
        Log.Info($"Websiter server listening on {Url} ({WebsiterEnvironment.Name}).");
        await _builtApp.RunAsync(_runCts.Token).ConfigureAwait(false);
    }

    /// <summary>Starts the server without blocking; returns the bound URL.</summary>
    public async Task<string> StartAsync(CancellationToken ct = default)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _builtApp = BuildApplication();
        _host = _builtApp;
        await _builtApp.StartAsync(_runCts.Token).ConfigureAwait(false);
        Log.Info($"Websiter server listening on {Url} ({WebsiterEnvironment.Name}).");
        return Url;
    }

    private WebApplication BuildApplication()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _root,
            Args = Array.Empty<string>(),
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.Logging.SetMinimumLevel(WebsiterEnvironment.IsDevelopment ? LogLevel.Debug : LogLevel.Information);

        if (_useCompression)
        {
            builder.Services.AddResponseCompression(o =>
            {
                o.EnableForHttps = true;
                o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
                o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            });
        }

        if (Options.UseHttps)
        {
            if (string.IsNullOrWhiteSpace(Options.CertificatePath) || !File.Exists(Options.CertificatePath))
                throw new WebsiterConfigurationException(
                    "HTTPS is enabled but no certificate was configured. Set server:certificatePath (PFX) or disable server:useHttps.");
            var cert = X509CertificateLoader.LoadPkcs12FromFile(
                Options.CertificatePath, Options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            builder.WebHost.ConfigureKestrel(k =>
            {
                if (System.Net.IPAddress.TryParse(Options.Host, out var ip))
                    k.Listen(ip, Options.Port, lo => lo.UseHttps(cert));
                else
                    k.ListenAnyIP(Options.Port, lo => lo.UseHttps(cert));
                k.Limits.MaxRequestBodySize = Options.MaxRequestBodySize;
            });
        }
        else
        {
            builder.WebHost.UseUrls(Options.GetUrl());
            builder.WebHost.ConfigureKestrel(k =>
                k.Limits.MaxRequestBodySize = Options.MaxRequestBodySize);
        }

        var app = builder.Build();

        if (_useCompression)
            app.UseResponseCompression();

        if (_useCors && Cors.Enabled && Cors.AllowedOrigins.Length > 0)
        {
            app.UseCors(policy =>
            {
                policy.WithOrigins(Cors.AllowedOrigins)
                      .WithMethods(Cors.AllowedMethods)
                      .WithHeaders(Cors.AllowedHeaders);
                if (Cors.AllowCredentials) policy.AllowCredentials();
            });
        }

        if (_useWebSockets)
            app.UseWebSockets();

        app.Run(TerminalPipelineAsync);
        return app;
    }

    /// <summary>Gracefully stops the server.</summary>
    public async Task StopAsync()
    {
        _runCts?.Cancel();
        if (_builtApp is not null)
        {
            try { await _builtApp.StopAsync().ConfigureAwait(false); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        RateLimiter.Dispose();
        Components.Dispose();
        _runCts?.Dispose();
        if (_builtApp is not null)
        {
            try { await _builtApp.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }
}
