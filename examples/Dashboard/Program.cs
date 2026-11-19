using Microsoft.AspNetCore.Http;
using Websiter;

var app = WebsiterApplication.Create(args);

app.MapGet("/api/status", ctx => ctx.WriteJsonAsync(new
{
    status = "online",
    framework = "Websiter",
    environment = WebsiterEnvironment.Name,
}));

// Example of calling a worker over IPC from an HTTP route.
app.MapGet("/api/workers", async ctx =>
{
    var workers = await IPC.RequestAsync<object>("host", "$workers", timeout: TimeSpan.FromSeconds(5));
    await ctx.WriteJsonAsync(workers);
});

return await app.RunAsync();
