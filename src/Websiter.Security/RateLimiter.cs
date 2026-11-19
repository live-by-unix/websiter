using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Websiter.Security;

/// <summary>
/// Fixed-window per-key rate limiter. Thread-safe, in-memory, suitable for
/// single-instance deployments; swap for a distributed limiter at scale.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Timer _eviction;
    private int _disposed;

    private sealed class Window
    {
        public long ResetTicks;
        public int Count;
    }

    public RateLimiter(RateLimitOptions? options = null)
    {
        _options = options ?? RateLimitOptions.FromConfig();
        _eviction = new Timer(_ => Evict(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Attempts to acquire a permit for <paramref name="key"/>.</summary>
    /// <returns>True when the request is allowed; false when limited.</returns>
    public bool TryAcquire(string key)
    {
        if (!_options.Enabled) return true;

        var now = DateTime.UtcNow.Ticks;
        var windowTicks = TimeSpan.FromSeconds(_options.WindowSeconds).Ticks;
        var w = _windows.GetOrAdd(key, _ => new Window());

        lock (w)
        {
            if (now >= w.ResetTicks)
            {
                w.ResetTicks = now + windowTicks;
                w.Count = 0;
            }
            if (w.Count >= _options.RequestsPerWindow) return false;
            w.Count++;
            return true;
        }
    }

    /// <summary>Seconds until the current window resets for <paramref name="key"/>.</summary>
    public int RetryAfterSeconds(string key)
    {
        if (!_windows.TryGetValue(key, out var w)) return _options.WindowSeconds;
        lock (w)
        {
            var remaining = new DateTime(w.ResetTicks, DateTimeKind.Utc) - DateTime.UtcNow;
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        }
    }

    private void Evict()
    {
        var cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(_options.WindowSeconds * 2).Ticks;
        foreach (var kvp in _windows)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.ResetTicks < cutoff)
                    _windows.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _eviction.Dispose();
    }
}
