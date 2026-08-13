namespace Bagly.Api.Services;

/// <summary>In-memory Shiprocket JWT cache (valid ~10 days; we refresh early / on 401).</summary>
public sealed class ShiprocketTokenStore
{
    private readonly object _lock = new();
    private string? _token;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    /// <summary>Tokens are treated as valid for 9 days after login unless invalidated earlier.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(9);

    public string? GetValidToken()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_token) || DateTimeOffset.UtcNow >= _expiresAtUtc)
            {
                return null;
            }

            return _token;
        }
    }

    public void SetToken(string token, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        lock (_lock)
        {
            _token = token.Trim();
            _expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultLifetime);
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _token = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
        }
    }
}
