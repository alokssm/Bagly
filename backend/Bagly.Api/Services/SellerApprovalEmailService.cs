using System.Text;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public interface ISellerApprovalEmailService
{
    Task SendApprovedAsync(SellerUser seller, CancellationToken cancellationToken = default);
    Task SendRejectedAsync(SellerUser seller, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>Notifies sellers when an admin approves or rejects their marketplace account.</summary>
public class SellerApprovalEmailService(
    IEmailSender emailSender,
    ILogger<SellerApprovalEmailService> logger) : ISellerApprovalEmailService
{
    public async Task SendApprovedAsync(SellerUser seller, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seller.Email))
        {
            logger.LogWarning("Seller approval email skipped: email missing for seller {SellerId}.", seller.Id);
            return;
        }

        var business = string.IsNullOrWhiteSpace(seller.BusinessName) ? "your business" : seller.BusinessName;
        var subject = "Your Bagly seller account is approved";
        var textBody = new StringBuilder()
            .AppendLine($"Hi {seller.Name},")
            .AppendLine()
            .AppendLine($"Good news — {business} has been approved on Bagly.")
            .AppendLine("You can now sign in to your seller dashboard and start listing products.")
            .AppendLine()
            .AppendLine("Sign in: open Bagly → Sell on Bagly → Seller sign in.")
            .AppendLine()
            .AppendLine("— The Bagly team")
            .ToString();

        var htmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;color:#1B3D2F;">Your Bagly seller account is approved</h2>
              <p>Hi {Escape(seller.Name)},</p>
              <p>Good news — <strong>{Escape(business)}</strong> has been approved on Bagly.</p>
              <p>You can now sign in to your seller dashboard and start listing products.</p>
              <p style="color:#666;">Sign in via Bagly → Sell on Bagly → Seller sign in.</p>
              <p>— The Bagly team</p>
            </div>
            """;

        var sent = await emailSender.SendAsync(seller.Email, subject, textBody, htmlBody, cancellationToken);
        if (sent)
            logger.LogInformation("Seller approval email sent to {Email}.", seller.Email);
        else
            logger.LogWarning("Seller approval email failed for {Email}.", seller.Email);
    }

    public async Task SendRejectedAsync(SellerUser seller, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seller.Email))
        {
            logger.LogWarning("Seller rejection email skipped: email missing for seller {SellerId}.", seller.Id);
            return;
        }

        var business = string.IsNullOrWhiteSpace(seller.BusinessName) ? "your business" : seller.BusinessName;
        var reasonText = string.IsNullOrWhiteSpace(reason)
            ? "Please review your business details and resubmit from your seller dashboard."
            : reason.Trim();

        var subject = "Update on your Bagly seller application";
        var textBody = new StringBuilder()
            .AppendLine($"Hi {seller.Name},")
            .AppendLine()
            .AppendLine($"We were unable to approve {business} on Bagly at this time.")
            .AppendLine()
            .AppendLine($"Reason: {reasonText}")
            .AppendLine()
            .AppendLine("You can update your details in the seller dashboard and submit again for review.")
            .AppendLine()
            .AppendLine("— The Bagly team")
            .ToString();

        var htmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;color:#1B3D2F;">Update on your Bagly seller application</h2>
              <p>Hi {Escape(seller.Name)},</p>
              <p>We were unable to approve <strong>{Escape(business)}</strong> on Bagly at this time.</p>
              <p><strong>Reason:</strong> {Escape(reasonText)}</p>
              <p>You can update your details in the seller dashboard and submit again for review.</p>
              <p>— The Bagly team</p>
            </div>
            """;

        var sent = await emailSender.SendAsync(seller.Email, subject, textBody, htmlBody, cancellationToken);
        if (sent)
            logger.LogInformation("Seller rejection email sent to {Email}.", seller.Email);
        else
            logger.LogWarning("Seller rejection email failed for {Email}.", seller.Email);
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
