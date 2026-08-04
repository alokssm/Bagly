using System.Collections.Concurrent;

namespace Bagly.Api.Services;

public interface ISiteHitRateLimiter
{
    /// <summary>Returns true if the caller identified by <paramref name="key"/> (typically their
    /// IP address) is still within the allowed hit-beacon rate.</summary>
    bool TryConsume(string key);
}

/// <summary>Simple fixed-window limiter keyed by client IP, so the public "record a page view"
/// beacon can't be used to flood the database. Generous limit since it fires once per storefront
/// navigation. In-memory only — fine for a single-instance API and resets on restart/deploy.</summary>
public sealed class SiteHitRateLimiter : ISiteHitRateLimiter
{
    private const int MaxRequestsPerWindow = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, WindowState> _windows = new();

    public bool TryConsume(string key)
    {
        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(key, static _ => new WindowState());

        lock (window.Lock)
        {
            if (now - window.StartUtc >= Window)
            {
                window.StartUtc = now;
                window.Count = 0;
            }

            if (window.Count >= MaxRequestsPerWindow)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    private sealed class WindowState
    {
        public readonly object Lock = new();
        public DateTime StartUtc = DateTime.UtcNow;
        public int Count;
    }
}
