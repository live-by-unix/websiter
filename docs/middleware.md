# Middleware

Middleware wraps the terminal route pipeline. Register with `Use`:

```csharp
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    ctx.Response.Headers["X-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
});
```

Middleware runs in registration order (first registered = outermost).

## Built-in middleware

```csharp
app.UseExceptionHandler();   // JSON 500s, never leaks stack traces
app.UseSecurityHeaders();    // X-Content-Type-Options, X-Frame-Options, etc.
app.UseCors();               // CORS from configuration
app.UseCompression();        // gzip/deflate for compressible responses
app.UseRateLimiting();       // fixed-window per-IP limiter
app.UseStaticFiles();        // wwwroot + Pages/ + Components rendering
app.UseWebSockets();         // enable WebSocket middleware (call before MapWebSocket)
```

Typical order:

```csharp
app.UseExceptionHandler();
app.UseSecurityHeaders();
app.UseCors();
app.UseCompression();
app.UseRateLimiting();
app.UseStaticFiles();
// ... your routes ...
```

`UseStaticFiles` serves (in order): explicit routes win → `wwwroot/` files → `Pages/*.html` with component expansion → 404 JSON.
