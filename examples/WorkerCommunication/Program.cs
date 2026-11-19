using Microsoft.AspNetCore.Http;
using Websiter;

var app = WebsiterApplication.Create(args);

app.MapGet("/", ctx => ctx.Response.WriteAsync("Distributed Websiter app"));

// HTTP route → IPC → worker → response.
app.MapGet("/api/users", async ctx =>
{
    var users = await IPC.RequestAsync<object>("DatabaseWorker", "GetUsers", timeout: TimeSpan.FromSeconds(10));
    await ctx.WriteJsonAsync(users);
});

app.MapPost("/api/auth/login", async ctx =>
{
    var body = await ctx.ReadJsonAsync<LoginRequest>(ctx.RequestAborted);
    if (body is null) return await ctx.WriteJsonAsync(new { error = "invalid body" }, 400, ctx.RequestAborted);

    var session = await IPC.RequestAsync<object>("AuthWorker", "Authenticate", new
    {
        username = body.Username,
        password = body.Password,
    }, timeout: TimeSpan.FromSeconds(10));

    await ctx.WriteJsonAsync(session);
});

// Worker status dashboard data.
app.MapGet("/api/workers", async ctx =>
{
    var workers = await IPC.RequestAsync<object>("host", "$workers", timeout: TimeSpan.FromSeconds(5));
    await ctx.WriteJsonAsync(workers);
});

return await app.RunAsync();

public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}
