using Microsoft.AspNetCore.Http;
using Websiter;

var app = WebsiterApplication.Create(args);

app.MapGet("/", ctx => ctx.Response.WriteAsync("Hello Websiter!"));

app.MapGet("/api/status", ctx => ctx.WriteJsonAsync(new
{
    status = "online",
    framework = "Websiter",
    environment = WebsiterEnvironment.Name,
}));

app.MapGet("/api/hello/{name}", ctx => ctx.WriteJsonAsync(new
{
    message = $"Hello, {ctx.Param<string>("name") ?? "world"}!",
}));

return await app.RunAsync();
