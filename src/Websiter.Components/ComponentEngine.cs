using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Websiter.Components;

/// <summary>Options for the component engine.</summary>
public sealed class ComponentEngineOptions
{
    /// <summary>Cache rendered component output (disable in watch/dev mode).</summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>Maximum include depth to bound pathological nesting.</summary>
    public int MaxDepth { get; set; } = 32;
}

/// <summary>
/// Reusable HTML component engine. Components are plain HTML fragments in the
/// <c>Components/</c> directory, included via
/// <c>&lt;component name="Navbar" /&gt;</c> tags. Components may themselves
/// contain component tags (nested includes). There is no server-side code
/// execution inside components — content is inserted verbatim and must be
/// trusted markup authored by the developer.
/// </summary>
public sealed class ComponentEngine : IDisposable
{
    private static readonly Regex _tagRegex = new(
        @"<component\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _nameAttrRegex = new(
        @"name\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s/>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _componentsDir;
    private readonly ComponentEngineOptions _options;
    private readonly ConcurrentDictionary<string, string> _sourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _renderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _fileStamps = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    /// <summary>Raised when a component file changes (dev reload).</summary>
    public event Action<string>? ComponentChanged;

    public ComponentEngine(string componentsDir, ComponentEngineOptions? options = null)
    {
        _componentsDir = Path.GetFullPath(componentsDir);
        _options = options ?? new ComponentEngineOptions();
        Directory.CreateDirectory(_componentsDir);
    }

    /// <summary>Directory scanned for components.</summary>
    public string ComponentsDirectory => _componentsDir;

    /// <summary>Discovers all component names (file names without extension).</summary>
    public IReadOnlyList<string> Discover()
    {
        if (!Directory.Exists(_componentsDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_componentsDir, "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Resolves a component's source file path.</summary>
    public string? ResolvePath(string name)
    {
        // Subdirectory references like "layout/Header" — checked first since
        // the name sanitizer rejects path separators.
        if (name.Contains('/') || name.Contains('\\'))
        {
            if (name.Contains("..", StringComparison.Ordinal)) return null;
            var rel = Path.Combine(_componentsDir, name.Replace('/', Path.DirectorySeparatorChar) + ".html");
            var full = Path.GetFullPath(rel);
            if (full.StartsWith(_componentsDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(full, _componentsDir, StringComparison.Ordinal))
            {
                if (File.Exists(full)) return full;
            }
            return null;
        }

        var safe = SanitizeName(name);
        if (safe is null) return null;
        var direct = Path.Combine(_componentsDir, safe + ".html");
        if (File.Exists(direct)) return direct;
        return null;
    }

    private static string? SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return null;
        foreach (var c in name)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_')) return null;
        return name;
    }

    /// <summary>
    /// Renders <paramref name="html"/> by expanding all <c>&lt;component&gt;</c>
    /// tags recursively. Throws <see cref="ComponentException"/> on missing
    /// components or circular includes.
    /// </summary>
    public string Render(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return RenderInternal(html, new Stack<string>(), 0);
    }

    /// <summary>Async wrapper around <see cref="Render"/>.</summary>
    public Task<string> RenderAsync(string html, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Render(html));
    }

    /// <summary>Renders a component by name.</summary>
    public string RenderComponent(string name)
        => Render($"<component name=\"{name}\" />");

    /// <summary>Clears all caches (call on file changes in dev mode).</summary>
    public void Invalidate()
    {
        _sourceCache.Clear();
        _renderCache.Clear();
        _fileStamps.Clear();
    }

    /// <summary>Enables file watching; component edits invalidate caches and raise <see cref="ComponentChanged"/>.</summary>
    public void EnableWatching()
    {
        if (_watcher is not null) return;
        _watcher = new FileSystemWatcher(_componentsDir, "*.html")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += (_, e) => OnChanged(null, new RenamedEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(e.FullPath) ?? "", e.OldFullPath, e.FullPath));
    }

    private void OnChanged(object? sender, FileSystemEventArgs e)
    {
        Invalidate();
        ComponentChanged?.Invoke(e.Name);
    }

    public void Dispose() => _watcher?.Dispose();

    private string RenderInternal(string html, Stack<string> includeStack, int depth)
    {
        if (depth > _options.MaxDepth)
            throw new ComponentException($"Component include depth exceeded { _options.MaxDepth } (circular include?).");

        return _tagRegex.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var name = ExtractName(attrs);
            if (name is null)
                throw new ComponentException($"<component> tag is missing a 'name' attribute: {match.Value}");

            if (includeStack.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                throw new ComponentException($"Circular component include detected: {string.Join(" -> ", includeStack.Reverse().Concat(new[] { name }))}");

            var source = LoadSource(name);
            includeStack.Push(name);
            try
            {
                // Render the component source recursively (nested components).
                var rendered = RenderInternal(source, includeStack, depth + 1);
                return rendered;
            }
            finally
            {
                includeStack.Pop();
            }
        });
    }

    private string LoadSource(string name)
    {
        if (_options.EnableCache && _renderCache.TryGetValue(name, out var cached))
            return cached;

        var path = ResolvePath(name)
            ?? throw new ComponentException($"Component '{name}' not found in '{_componentsDir}'.");

        // Cache keyed by file stamp so edits invalidate naturally.
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_options.EnableCache
            && _sourceCache.TryGetValue(name, out var src)
            && _fileStamps.TryGetValue(name, out var oldStamp)
            && oldStamp == stamp)
        {
            return src;
        }

        var text = File.ReadAllText(path);
        _sourceCache[name] = text;
        _fileStamps[name] = stamp;
        if (_options.EnableCache)
            _renderCache[name] = text;
        return text;
    }

    private static string? ExtractName(string attrs)
    {
        var m = _nameAttrRegex.Match(attrs);
        if (!m.Success) return null;
        return m.Groups["dq"].Success ? m.Groups["dq"].Value
             : m.Groups["sq"].Success ? m.Groups["sq"].Value
             : m.Groups["bare"].Value;
    }
}
