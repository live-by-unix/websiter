using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Websiter.Frontend;

/// <summary>An entry in the frontend asset manifest.</summary>
public sealed class AssetEntry
{
    public required string Source { get; init; }   // path relative to web root
    public required string Target { get; init; }   // path relative to output
    public required string ContentType { get; init; }
    public required string Hash { get; init; }     // short content hash for cache busting
    public long SizeBytes { get; init; }
}

/// <summary>
/// Frontend asset pipeline: collects static web assets (HTML, CSS, JS, images,
/// fonts, videos), computes content hashes for cache busting, and produces a
/// manifest plus a deployable asset directory.
/// </summary>
public sealed class AssetPipeline
{
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".txt"] = "text/plain; charset=utf-8",
        [".xml"] = "application/xml",
        [".map"] = "application/json",
        [".webmanifest"] = "application/manifest+json",
    };

    /// <summary>Gets the content type for a file extension.</summary>
    public static string ContentTypeFor(string path)
        => _mimeTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";

    /// <summary>Collects all assets under the web root.</summary>
    public IReadOnlyList<AssetEntry> Collect(string webRoot)
    {
        var entries = new List<AssetEntry>();
        if (!Directory.Exists(webRoot)) return entries;

        foreach (var file in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(webRoot, file).Replace('\\', '/');
            entries.Add(new AssetEntry
            {
                Source = file,
                Target = relative,
                ContentType = ContentTypeFor(file),
                Hash = ComputeHash(file),
                SizeBytes = new FileInfo(file).Length,
            });
        }
        return entries;
    }

    /// <summary>Short content hash (first 12 hex chars of SHA-256) for cache busting.</summary>
    public static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>Appends the content hash as a query string: <c>app.css?v=abc123</c>.</summary>
    public static string VersionedUrl(string url, string hash) => $"{url}?v={hash}";

    /// <summary>
    /// Copies all assets to <paramref name="outputDir"/> and writes
    /// <c>manifest.json</c> describing each asset.
    /// </summary>
    public async Task<IReadOnlyList<AssetEntry>> PublishAsync(string webRoot, string outputDir, CancellationToken ct = default)
    {
        var assets = Collect(webRoot);
        Directory.CreateDirectory(outputDir);

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(outputDir, asset.Target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(asset.Source, target, overwrite: true);
        }

        var manifest = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            webRoot,
            assets = assets.Select(a => new
            {
                path = a.Target,
                contentType = a.ContentType,
                hash = a.Hash,
                sizeBytes = a.SizeBytes,
                versionedUrl = VersionedUrl("/" + a.Target, a.Hash),
            }),
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct)
            .ConfigureAwait(false);

        return assets;
    }
}
