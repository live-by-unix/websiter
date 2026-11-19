using Microsoft.AspNetCore.Http;
using Websiter;

var app = WebsiterApplication.Create(args);

// REST API surface.
app.MapGet("/api/health", ctx => ctx.WriteJsonAsync(new { status = "ok" }));

app.MapGet("/api/items", ctx => ctx.WriteJsonAsync(new[]
{
    new { id = 1, name = "Alpha" },
    new { id = 2, name = "Beta" },
}));

app.MapGet("/api/items/{id}", ctx =>
{
    var id = ctx.Param<int>("id");
    if (id <= 0) return ctx.WriteJsonAsync(new { error = "invalid id" }, 400, ctx.RequestAborted);
    return ctx.WriteJsonAsync(new { id, name = $"Item {id}" });
});

app.MapPost("/api/items", async ctx =>
{
    var body = await ctx.ReadJsonAsync<ItemCreate>(ctx.RequestAborted);
    if (body is null || string.IsNullOrWhiteSpace(body.Name))
        return await ctx.WriteJsonAsync(new { error = "name is required" }, 422, ctx.RequestAborted);
    return await ctx.WriteJsonAsync(new { id = 3, name = body.Name }, 201, ctx.RequestAborted);
});

app.MapDelete("/api/items/{id}", ctx =>
    ctx.WriteJsonAsync(new { deleted = ctx.Param<int>("id") }));

// Secured by default: security headers + rate limiting are one call away.
app.UseSecurityHeaders();
app.UseRateLimiting();
app.UseExceptionHandler();

return await app.RunAsync();

public sealed class ItemCreate
{
    public string? Name { get; set; }
}
