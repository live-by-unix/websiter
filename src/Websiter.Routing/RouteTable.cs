namespace Websiter.Routing;

/// <summary>HTTP method constraint for a route.</summary>
public enum RouteMethod
{
    Any,
    Get,
    Post,
    Put,
    Delete,
    Patch,
    Head,
    Options,
}

/// <summary>A compiled route: method + path pattern + handler.</summary>
public sealed class RouteDefinition
{
    public required string Method { get; init; }
    public required string Pattern { get; init; }
    public required string[] Segments { get; init; }
    public required Delegate Handler { get; init; }
    public string? Name { get; set; }
}

/// <summary>Result of matching a request path against a route pattern.</summary>
public sealed class RouteMatch
{
    public required RouteDefinition Route { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}

/// <summary>
/// Ordered route table with segment-wise pattern matching.
/// Patterns like <c>/users/{id}/posts/{slug}</c> capture path parameters;
/// a trailing <c>{*rest}</c> or <c>*</c> segment acts as a catch-all.
/// </summary>
public sealed class RouteTable
{
    private readonly List<RouteDefinition> _routes = new();

    public IReadOnlyList<RouteDefinition> Routes => _routes;

    public void Add(RouteMethod method, string pattern, Delegate handler, string? name = null)
    {
        var normalized = NormalizePattern(pattern);
        _routes.Add(new RouteDefinition
        {
            Method = method == RouteMethod.Any ? "ANY" : method.ToString().ToUpperInvariant(),
            Pattern = normalized,
            Segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries),
            Handler = handler,
            Name = name,
        });
    }

    /// <summary>Matches method + path. Returns the first matching route in registration order.</summary>
    public RouteMatch? Match(string method, string path)
    {
        var segments = path.Split('?', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        // Re-split properly: path may contain '?' — strip query first.
        var rawPath = path.Split('?', 2)[0];
        segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var route in _routes)
        {
            if (route.Method != "ANY" && !string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
                continue;

            var values = MatchSegments(route.Segments, segments);
            if (values is not null)
                return new RouteMatch { Route = route, Values = values };
        }
        return null;
    }

    private static Dictionary<string, string>? MatchSegments(string[] pattern, string[] path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        for (; i < pattern.Length; i++)
        {
            var seg = pattern[i];
            if (seg == "*")
            {
                // Catch-all: consume the rest.
                values["*"] = string.Join('/', path.Skip(i));
                return values;
            }
            if (i >= path.Length) return null;

            if (seg.StartsWith('{') && seg.EndsWith('}'))
            {
                var name = seg[1..^1];
                var optional = name.EndsWith('?');
                if (optional) name = name[..^1];
                values[name] = Uri.UnescapeDataString(path[i]);
            }
            else if (seg.StartsWith('{') && seg.EndsWith("*}"))
            {
                var name = seg[1..^2];
                values[name] = Uri.UnescapeDataString(string.Join('/', path.Skip(i)));
                return values;
            }
            else if (!string.Equals(seg, path[i], StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        return i == path.Length ? values : null;
    }

    private static string NormalizePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "/") return "/";
        var p = pattern.Trim();
        if (!p.StartsWith('/')) p = "/" + p;
        return p.TrimEnd('/');
    }
}
