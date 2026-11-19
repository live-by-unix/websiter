# Routing

Routes are registered on `WebsiterApplication` (or `WebsiterServer`) with `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch`, `MapHead`, and generic `Map`.

## Basics

```csharp
var app = WebsiterApplication.Create(args);

app.MapGet("/", ctx => ctx.Response.WriteAsync("Hello"));
app.MapGet("/api/status", ctx => ctx.WriteJsonAsync(new { ok = true }));
app.MapPost("/api/users", async ctx =>
{
    var body = await ctx.ReadJsonAsync<CreateUserRequest>(ctx.RequestAborted);
    return Created($"/api/users/{body.Name}", new { id = 1 });
});
await app.RunAsync();
```

Handler forms supported:

- `Func<HttpContext, Task>` — full control
- `Func<HttpContext, CancellationToken, Task>` — with cancellation
- `Func<HttpContext, object?>` — return value serialized as JSON (or string → text)
- `Delegate` — dependency-injected signatures (see below)

## Route parameters

```csharp
app.MapGet("/users/{id}", ctx =>
    ctx.WriteJsonAsync(new { id = ctx.Param<int>("id") }));

app.MapGet("/files/{*path}", ctx =>           // catch-all
    ctx.WriteTextAsync(ctx.Param("path") ?? ""));
```

`ctx.Param<T>(name)` / `ctx.Param(name)` read captured values; `ctx.Query<T>(name)` reads query string.

## Named routes

```csharp
app.MapGet("/users/{id}", handler, name: "user.details");
var url = app.Routes.UrlFor("user.details", new { id = 42 });   // "/users/42"
```

## Matching rules

- Segments match literally or via `{param}` / `{*catchAll}`.
- Matching is deterministic: first registered match wins for a method+path.
- HTTP method must match (`MapGet` handles GET; HEAD falls back to static/pages unless mapped).
- Explicit routes take precedence over `Pages/` and `wwwroot/` fallbacks.

## Inspecting routes

```bash
websiter routes
```

or `GET /__websiter/routes` on a running app.
