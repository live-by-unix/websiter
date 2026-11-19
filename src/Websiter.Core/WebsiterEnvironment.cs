namespace Websiter;

/// <summary>
/// Environment detection for Websiter applications.
/// Initialize via <see cref="WebsiterEnvironment.Initialize"/>; the runtime
/// performs this automatically from configuration.
/// </summary>
public static class WebsiterEnvironment
{
    private static string _name = "Production";
    private static bool _initialized;

    /// <summary>Current environment name (e.g. "Development", "Production").</summary>
    public static string Name => _name;

    /// <summary>True when running in a production environment.</summary>
    public static bool IsProduction =>
        string.Equals(_name, "Production", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_name, "Prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when running in a development environment.</summary>
    public static bool IsDevelopment =>
        string.Equals(_name, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_name, "Dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when running in a staging environment.</summary>
    public static bool IsStaging => string.Equals(_name, "Staging", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when running under a debugger or the `websiter dev` watcher.</summary>
    public static bool IsWatchMode =>
        string.Equals(Environment.GetEnvironmentVariable("WEBSITER_WATCH"), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable("WEBSITER_DEV"), "1", StringComparison.Ordinal);

    internal static void Initialize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Production";
        _name = char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
        _initialized = true;
    }

    /// <summary>Reads the environment name from the <c>WEBSITER_ENV</c> / <c>ASPNETCORE_ENVIRONMENT</c> variables if not already set.</summary>
    internal static void InitializeFromEnvironment()
    {
        if (_initialized) return;
        var name = Environment.GetEnvironmentVariable("WEBSITER_ENV")
                   ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                   ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        Initialize(name ?? "Production");
    }
}
