using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Websiter;

/// <summary>Request/response helpers for Websiter route handlers.</summary>
public static class WebsiterHttp
{
    private const string RouteValuesKey = "Websiter.RouteValues";

    internal static void SetRouteValues(this HttpContext context, IReadOnlyDictionary<string, string> values)
        => context.Items[RouteValuesKey] = values;

    /// <summary>Reads a route parameter (e.g. {id} in /users/{id}).</summary>
    public static string? Param(this HttpContext context, string name)
    {
        if (context.Items[RouteValuesKey] is IReadOnlyDictionary<string, string> values
            && values.TryGetValue(name, out var v))
            return v;
        return context.Request.RouteValues.TryGetValue(name, out var rv) ? rv?.ToString() : null;
    }

    /// <summary>Reads a route parameter converted to <typeparamref name="T"/>.</summary>
    public static T? Param<T>(this HttpContext context, string name)
    {
        var raw = context.Param(name);
        if (raw is null) return default;
        if (typeof(T) == typeof(string)) return (T)(object)raw;
        try { return JsonSerializer.Deserialize<T>(raw); }
        catch { /* raw may be a plain string, not a JSON literal */ }
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(raw)); }
        catch { /* fall through to ChangeType */ }
        try
        {
            if (typeof(T) != typeof(object) && raw is not null)
                return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch { }
        return default;
    }

    /// <summary>Reads a query string parameter.</summary>
    public static string? Query(this HttpContext context, string name)
        => context.Request.Query.TryGetValue(name, out var v) ? v.ToString() : null;

    /// <summary>Reads a query string parameter converted to <typeparamref name="T"/>.</summary>
    public static T? Query<T>(this HttpContext context, string name)
    {
        var raw = context.Query(name);
        if (raw is null) return default;
        if (typeof(T) == typeof(string)) return (T)(object)raw;
        try { return JsonSerializer.Deserialize<T>(raw); }
        catch { }
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(raw)); }
        catch { }
        try
        {
            if (typeof(T) != typeof(object))
                return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch { }
        return default;
    }

    /// <summary>Deserializes the request body as JSON.</summary>
    public static async Task<T?> ReadJsonAsync<T>(this HttpContext context, CancellationToken ct = default)
    {
        if (context.Request.ContentLength is 0) return default;
        return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Writes a JSON response with the given status code.</summary>
    public static async Task WriteJsonAsync<T>(this HttpContext context, T value, int statusCode = 200, CancellationToken ct = default)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(value, ct).ConfigureAwait(false);
    }

    /// <summary>Writes a plain-text response.</summary>
    public static async Task WriteTextAsync(this HttpContext context, string text, string contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
    {
        context.Response.ContentType = contentType;
        await context.Response.WriteAsync(text, ct).ConfigureAwait(false);
    }

    /// <summary>Writes an empty response with a status code.</summary>
    public static Task StatusAsync(this HttpContext context, int statusCode, CancellationToken ct = default)
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    /// <summary>Redirects to a URL.</summary>
    public static Task RedirectAsync(this HttpContext context, string url, int statusCode = 302, CancellationToken ct = default)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.Location = url;
        return Task.CompletedTask;
    }

    /// <summary>Reads a cookie value.</summary>
    public static string? Cookie(this HttpContext context, string name)
        => context.Request.Cookies.TryGetValue(name, out var v) ? v : null;

    /// <summary>Sets a cookie with secure defaults.</summary>
    public static void SetCookie(this HttpContext context, string name, string value,
        bool httpOnly = true, bool secure = true, TimeSpan? maxAge = null, string path = "/", string? sameSite = "Lax")
    {
        var options = new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = secure,
            Path = path,
            SameSite = sameSite is null ? SameSiteMode.Unspecified : Enum.Parse<SameSiteMode>(sameSite),
        };
        if (maxAge is not null) options.MaxAge = maxAge;
        context.Response.Cookies.Append(name, value, options);
    }
}
