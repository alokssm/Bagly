using System.Collections.Concurrent;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services.Chat;

/// <summary>Simple fixed-window rate limiter keyed by SignalR connection id.</summary>
public sealed class ChatRateLimiter(IOptions<ChatOptions> options) : IChatRateLimiter
{
    private readonly ChatOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Window> _windows = new();

    public bool TryConsume(string connectionId)
    {
        var limit = Math.Max(1, _options.MaxMessagesPerMinute);
        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(connectionId, static _ => new Window());

        lock (window.Lock)
        {
            if (now - window.WindowStartUtc >= TimeSpan.FromMinutes(1))
            {
                window.WindowStartUtc = now;
                window.Count = 0;
            }

            if (window.Count >= limit)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    public void Release(string connectionId) => _windows.TryRemove(connectionId, out _);

    private sealed class Window
    {
        public readonly object Lock = new();
        public DateTime WindowStartUtc = DateTime.UtcNow;
        public int Count;
    }
}
