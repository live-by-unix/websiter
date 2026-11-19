using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Websiter.Server;

/// <summary>
/// Adapts user route handlers (delegates with various signatures) into a uniform
/// <c>Func&lt;HttpContext, CancellationToken, Task&gt;</c> and serializes results.
/// Supported signatures:
/// - Func&lt;HttpContext, Task&gt; / Func&lt;HttpContext, CancellationToken, Task&gt;
/// - Func&lt;HttpContext, Task&lt;T&gt;&gt; — T serialized as JSON (string → text/html)
/// - Func&lt;HttpContext, T&gt; — synchronous variants
/// - Any delegate whose first parameter is HttpContext; optional second CancellationToken.
/// </summary>
public static class HandlerInvoker
{
    private static readonly ConcurrentDictionary<Delegate, Func<HttpContext, CancellationToken, Task>> _cache = new();

    public static Func<HttpContext, CancellationToken, Task> Compile(Delegate handler)
        => _cache.GetOrAdd(handler, Build);

    private static Func<HttpContext, CancellationToken, Task> Build(Delegate handler)
    {
        var method = handler.Method;
        var parameters = method.GetParameters();

        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(HttpContext))
            throw new ArgumentException($"Route handler '{handler.Method.Name}' must take HttpContext as its first parameter.");

        var hasCt = parameters.Length > 1 && parameters[1].ParameterType == typeof(CancellationToken);
        if (parameters.Length > (hasCt ? 2 : 1))
            throw new ArgumentException($"Route handler '{handler.Method.Name}' has unsupported parameters.");

        var returnType = method.ReturnType;
        var isAsync = returnType == typeof(Task) || returnType == typeof(ValueTask)
            || (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));

        // Build: (ctx, ct) => InvokeAndWrite(handler, ctx, ct)
        var ctxParam = Expression.Parameter(typeof(HttpContext), "ctx");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var callArgs = new List<Expression> { ctxParam };
        if (hasCt) callArgs.Add(ctParam);

        var call = Expression.Invoke(Expression.Constant(handler), callArgs);
        Expression body;

        if (isAsync)
        {
            // Convert ValueTask -> Task, unwrap Task<T>/ValueTask<T>
            Expression awaited = call;
            if (returnType == typeof(ValueTask))
                awaited = Expression.Call(awaited, typeof(ValueTask).GetMethod(nameof(ValueTask.AsTask))!);
            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTask = typeof(ValueTask<>).MakeGenericType(returnType.GetGenericArguments()[0])
                    .GetMethod(nameof(ValueTask<int>.AsTask))!;
                awaited = Expression.Call(awaited, asTask);
                returnType = typeof(Task<>).MakeGenericType(returnType.GetGenericArguments()[0]);
            }

            if (returnType == typeof(Task))
            {
                body = Expression.Call(
                    typeof(HandlerInvoker).GetMethod(nameof(WriteNothingAsync), BindingFlags.NonPublic | BindingFlags.Static)!,
                    awaited, ctParam);
            }
            else
            {
                var resultType = returnType.GetGenericArguments()[0];
                var writeGeneric = typeof(HandlerInvoker).GetMethod(nameof(WriteResultAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(resultType);
                body = Expression.Call(writeGeneric, awaited, ctxParam, ctParam);
            }
        }
        else
        {
            var writeGeneric = typeof(HandlerInvoker).GetMethod(nameof(WriteSyncResult), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(returnType == typeof(void) ? typeof(object) : returnType);
            body = Expression.Call(writeGeneric, call, ctxParam);
            if (returnType == typeof(void))
            {
                // WriteSyncResult<object> would serialize null; handle void separately.
                body = Expression.Block(call, Expression.Constant(Task.CompletedTask));
            }
        }

        return Expression.Lambda<Func<HttpContext, CancellationToken, Task>>(body, ctxParam, ctParam).Compile();
    }

    private static async Task WriteNothingAsync(Task task, CancellationToken ct)
    {
        await task.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    private static async Task WriteResultAsync<T>(Task<T> task, HttpContext ctx, CancellationToken ct)
    {
        var result = await task.ConfigureAwait(false);
        await WriteResultAsync(result, ctx, ct).ConfigureAwait(false);
    }

    private static Task WriteSyncResult<T>(T result, HttpContext ctx)
        => WriteResultAsync(result, ctx, CancellationToken.None);

    private static async Task WriteResultAsync<T>(T? result, HttpContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (result is null) return;
        if (result is string s)
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(s, ct).ConfigureAwait(false);
        }
        else if (result is byte[] bytes)
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        else
        {
            await ctx.Response.WriteAsJsonAsync(result, ct).ConfigureAwait(false);
        }
    }
}
