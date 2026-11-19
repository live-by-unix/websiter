using Microsoft.Extensions.Configuration;

namespace Websiter.Server;

/// <summary>Configuration for the Websiter HTTP server.</summary>
public sealed class WebsiterServerOptions
{
    /// <summary>Bind address (default 0.0.0.0 / ::).</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Listen port (default 8080).</summary>
    public int Port { get; set; } = 8080;

    /// <summary>Enable HTTPS with the configured certificate.</summary>
    public bool UseHttps { get; set; }

    /// <summary>PFX certificate path for HTTPS.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Certificate password.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>Explicit URL override (e.g. "http://localhost:5000"). Takes precedence over Host/Port.</summary>
    public string? Url { get; set; }

    /// <summary>Maximum request body size in bytes (0 = Kestrel default 30MB).</summary>
    public long MaxRequestBodySize { get; set; } = 30L * 1024 * 1024;

    /// <summary>Directory for static assets (default: {root}/wwwroot).</summary>
    public string? WebRoot { get; set; }

    /// <summary>Directory for HTML pages (default: {root}/Pages).</summary>
    public string? PagesRoot { get; set; }

    /// <summary>Directory for components (default: {root}/Components).</summary>
    public string? ComponentsRoot { get; set; }

    /// <summary>Cache-Control value for static assets (null = no header).</summary>
    public string? StaticCacheControl { get; set; } = "public, max-age=3600";

    /// <summary>Whether to serve a directory index/fallback for unknown paths.</summary>
    public bool SpaFallback { get; set; }

    public static WebsiterServerOptions FromConfig(string root)
    {
        var options = new WebsiterServerOptions
        {
            WebRoot = Path.Combine(root, "wwwroot"),
            PagesRoot = Path.Combine(root, "Pages"),
            ComponentsRoot = Path.Combine(root, "Components"),
        };
        Config.Root?.GetSection("Server").Bind(options);
        return options;
    }

    /// <summary>Effective listen URL.</summary>
    public string GetUrl()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return Url;
        var scheme = UseHttps ? "https" : "http";
        var host = Host is "0.0.0.0" or "::" or "*" ? "localhost" : Host;
        return $"{scheme}://{host}:{Port}";
    }
}
