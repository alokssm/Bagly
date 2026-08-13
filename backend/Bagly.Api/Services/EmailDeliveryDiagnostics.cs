namespace Bagly.Api.Services;

/// <summary>
/// In-memory last outbound email failure for /api/health (no secrets, no message bodies).
/// Helps diagnose Resend sandbox / domain-verification rejections on Render without log diving.
/// </summary>
public sealed class EmailDeliveryDiagnostics
{
    private readonly object _gate = new();
    private EmailDeliveryFailureSnapshot? _lastFailure;

    public void RecordFailure(
        string provider,
        string to,
        string subject,
        int? statusCode,
        string? responseBody)
    {
        lock (_gate)
        {
            _lastFailure = new EmailDeliveryFailureSnapshot(
                DateTime.UtcNow,
                provider,
                MaskEmail(to),
                Truncate(subject, 120),
                statusCode,
                Truncate(responseBody, 400));
        }
    }

    public EmailDeliveryFailureSnapshot? GetLastFailure()
    {
        lock (_gate)
        {
            return _lastFailure;
        }
    }

    private static string MaskEmail(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        return trimmed[0] + "***" + trimmed[at..];
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max
                ? value
                : value[..max] + "…";
}

public sealed record EmailDeliveryFailureSnapshot(
    DateTime AtUtc,
    string Provider,
    string ToMasked,
    string? Subject,
    int? StatusCode,
    string? ResponseBody);
