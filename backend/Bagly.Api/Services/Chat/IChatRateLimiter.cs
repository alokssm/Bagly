namespace Bagly.Api.Services.Chat;

/// <summary>Limits how many chat messages a single SignalR connection can send per rolling minute.</summary>
public interface IChatRateLimiter
{
    bool TryConsume(string connectionId);

    void Release(string connectionId);
}
