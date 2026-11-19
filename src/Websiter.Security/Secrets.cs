using System.Text.Json;
using System.Text.RegularExpressions;

namespace Websiter.Security;

/// <summary>
/// Resolves secrets without hardcoding them. Resolution order:
/// 1. Environment variable <c>WEBSITER_SECRET_&lt;NAME&gt;</c> (NAME uppercased, '-'→'_')
/// 2. <c>.websiter/secrets.json</c> in the project root (gitignored; never commit)
/// 3. The literal value if it doesn't look like a secret reference
///
/// Usage: <c>var conn = Secrets.Get("Database:Password")</c> or config value
/// <c>"secretref:DbPassword"</c> resolved via <see cref="ResolveReference"/>.
/// </summary>
public static partial class Secrets
{
    private static readonly object _gate = new();
    private static Dictionary<string, string>? _fileSecrets;
    private static string? _root;

    /// <summary>Sets the project root used to locate <c>.websiter/secrets.json</c>.</summary>
    public static void Initialize(string root) => _root = root;

    /// <summary>Gets a secret by name. Throws <see cref="InvalidOperationException"/> when not found.</summary>
    public static string Get(string name)
        => TryGet(name, out var value)
            ? value!
            : throw new InvalidOperationException($"Secret '{name}' not found. Set WEBSITER_SECRET_{EnvName(name)} or add it to .websiter/secrets.json.");

    /// <summary>Attempts to resolve a secret; returns false when absent.</summary>
    public static bool TryGet(string name, out string? value)
    {
        var envName = "WEBSITER_SECRET_" + EnvName(name);
        value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(value)) return true;

        value = FileSecrets().GetValueOrDefault(name)
                ?? FileSecrets().GetValueOrDefault(name.Replace(':', '/'));
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Resolves a configuration value that may be a secret reference
    /// (<c>secretref:Name</c> or <c>env:NAME</c>). Plain values pass through.
    /// </summary>
    public static string ResolveReference(string? value, string fallback = "")
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        if (value.StartsWith("secretref:", StringComparison.OrdinalIgnoreCase))
            return Get(value["secretref:".Length..]);
        if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(value[4..]) ?? fallback;
        return value;
    }

    private static string EnvName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        return builder.ToString();
    }

    private static Dictionary<string, string> FileSecrets()
    {
        lock (_gate)
        {
            if (_fileSecrets is not null) return _fileSecrets;
            _fileSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = _root ?? Paths.Root;
            var path = Path.Combine(Paths.StateDirFor(root), "secrets.json");
            if (!File.Exists(path)) return _fileSecrets;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        _fileSecrets[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
            catch { /* malformed secrets file: ignore, env vars still work */ }
            return _fileSecrets;
        }
    }

    /// <summary>Writes a secret to the local secrets file (mode 0600 on Unix).</summary>
    public static void Set(string name, string value)
    {
        var root = _root ?? Paths.Root;
        var dir = Paths.StateDirFor(root);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "secrets.json");
        var secrets = FileSecrets();
        secrets[name] = value;
        File.WriteAllText(path, JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }
    }
}
