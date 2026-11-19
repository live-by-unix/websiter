using Microsoft.Extensions.Configuration;

namespace Websiter;

/// <summary>
/// Central configuration facade for Websiter applications.
/// Backed by <see cref="IConfigurationRoot"/> loaded from
/// <c>websiter.json</c>, <c>websiter.{Environment}.json</c>, environment
/// variables and defaults. Initialize via <see cref="WebsiterConfig.Initialize"/>.
/// </summary>
public static class Config
{
    private static IConfigurationRoot? _root;
    private static readonly object _gate = new();

    /// <summary>The underlying configuration root, or null before initialization.</summary>
    public static IConfiguration? Root => _root;

    /// <summary>Gets a configuration value by key, or null when absent.</summary>
    public static string? Get(string key) => _root?[key];

    /// <summary>Gets a configuration value or a fallback.</summary>
    public static string Get(string key, string fallback) => _root?[key] ?? fallback;

    /// <summary>Gets a strongly-typed configuration section.</summary>
    public static T? GetSection<T>(string key) where T : class
        => _root?.GetSection(key).Get<T>();

    /// <summary>Binds a configuration section to a new instance of <typeparamref name="T"/>.</summary>
    public static T Get<T>(string key, T fallback) where T : class, new()
    {
        if (_root is null) return fallback;
        var value = _root.GetSection(key).Get<T>();
        return value ?? fallback;
    }

    /// <summary>Determines whether a configuration key exists.</summary>
    public static bool Has(string key) => _root?.GetSection(key).Exists() == true || _root?[key] is not null;

    internal static void Initialize(IConfigurationRoot root)
    {
        lock (_gate)
        {
            _root = root;
        }
    }

    internal static void Reset()
    {
        lock (_gate)
        {
            _root = null;
        }
    }
}

/// <summary>Builds the Websiter configuration root from disk and environment.</summary>
public static class WebsiterConfig
{
    /// <summary>
    /// Loads configuration from (in increasing precedence):
    /// built-in defaults, <c>websiter.json</c>, <c>websiter.{Environment}.json</c>,
    /// environment variables prefixed <c>WEBSITER_</c> and <c>WEBSITER__</c> nested keys.
    /// </summary>
    public static IConfigurationRoot Load(string contentRoot, string? environment = null)
    {
        environment ??= WebsiterEnvironment.Name;

        var builder = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("websiter.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"websiter.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("WEBSITER_")
            .AddEnvironmentVariables(); // unrestricted env vars still apply to sections like Server:Port

        var root = builder.Build();
        Config.Initialize(root);
        return root;
    }

    /// <summary>Creates an in-memory configuration root (useful for tests).</summary>
    public static IConfigurationRoot CreateInMemory(IDictionary<string, string?> values)
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(values);
        var root = builder.Build();
        Config.Initialize(root);
        return root;
    }
}
