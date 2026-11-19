using System.Reflection;
using Microsoft.AspNetCore.Http;
using Websiter;
using Websiter.Workers;

// Worker process dispatch: --websiter-worker=<name> runs a worker in this process.
var workerArg = args.FirstOrDefault(a => a.StartsWith("--websiter-worker=", StringComparison.Ordinal));
if (workerArg is not null)
{
    var name = workerArg["--websiter-worker=".Length..];
    return await WorkerMain.RunWorkerAsync(Assembly.GetEntryAssembly()!, name, args);
}

var app = WebsiterApplication.Create(args);

app.MapGet("/", ctx => ctx.Response.WriteAsync("WorkerHost"));
app.MapGet("/api/ping", ctx => ctx.WriteJsonAsync(new { ok = true, workers = "see /api/workers" }));
app.MapGet("/api/workers", async ctx =>
{
    var workers = await IPC.RequestAsync<object>("host", "$workers", timeout: TimeSpan.FromSeconds(5));
    await ctx.WriteJsonAsync(workers);
});
app.MapGet("/api/echo", async ctx =>
{
    var result = await IPC.RequestAsync<object>("TestWorker", "Echo", new { msg = "from-http" },
        timeout: TimeSpan.FromSeconds(10));
    await ctx.WriteJsonAsync(result);
});

return await app.RunAsync();
