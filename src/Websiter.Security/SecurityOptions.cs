using Microsoft.Extensions.Configuration;

namespace Websiter.Security;

/// <summary>Configurable security headers applied to responses.</summary>
public sealed class SecurityHeadersOptions
{
    public bool Enabled { get; set; } = true;
    public string XContentTypeOptions { get; set; } = "nosniff";
    public string XFrameOptions { get; set; } = "DENY";
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";
    public string? ContentSecurityPolicy { get; set; }
    public string? PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=()";
    public bool StrictTransportSecurity { get; set; } = true;
    public string StrictTransportSecurityValue { get; set; } = "max-age=31536000; includeSubDomains";

    public static SecurityHeadersOptions FromConfig()
    {
        var options = new SecurityHeadersOptions();
        Config.Root?.GetSection("Security:Headers").Bind(options);
        return options;
    }
}

/// <summary>Fixed-window rate limiting settings.</summary>
public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int RequestsPerWindow { get; set; } = 300;
    public int WindowSeconds { get; set; } = 60;
    public string? WhitelistPaths { get; set; } = "/__websiter/health";

    public static RateLimitOptions FromConfig()
    {
        var options = new RateLimitOptions();
        Config.Root?.GetSection("Security:RateLimit").Bind(options);
        return options;
    }
}

/// <summary>CORS settings.</summary>
public sealed class CorsOptions
{
    public bool Enabled { get; set; }
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] AllowedMethods { get; set; } = { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" };
    public string[] AllowedHeaders { get; set; } = { "Content-Type", "Authorization", "X-Requested-With" };
    public bool AllowCredentials { get; set; }

    public static CorsOptions FromConfig()
    {
        var options = new CorsOptions();
        Config.Root?.GetSection("Security:Cors").Bind(options);
        return options;
    }
}
