using System.Text;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IContactEmailService
{
    /// <summary>Sends the contact-form submission to the admin mailbox. Returns whether it was
    /// (or should be considered) delivered — same best-effort contract as <see cref="IEmailSender"/>.</summary>
    Task<bool> SendAdminNotificationAsync(ContactMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Notifies the admin mailbox of contact-form submissions. Reuses the same
/// <c>Email__AdminOrderNotify</c> / <c>Admin__OrderNotifyEmail</c> recipient resolution as order
/// notifications, so a single admin mailbox setting covers both.</summary>
public class ContactEmailService(
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IOptions<AdminOptions> adminOptions,
    ILogger<ContactEmailService> logger) : IContactEmailService
{
    public async Task<bool> SendAdminNotificationAsync(ContactMessage message, CancellationToken cancellationToken = default)
    {
        var adminEmail = emailOptions.Value.ResolveAdminOrderNotifyEmail(adminOptions.Value.OrderNotifyEmail);
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning("Contact form notification skipped for {Email}: no admin recipient configured.", message.Email);
            return false;
        }

        var (subject, textBody, htmlBody) = BuildMessage(message);
        var sent = await emailSender.SendAsync(adminEmail, subject, textBody, htmlBody, cancellationToken);
        if (sent)
        {
            logger.LogInformation(
                "Contact form notification sent to {AdminEmail} for submission from {Email}.",
                adminEmail,
                message.Email);
        }
        else
        {
            logger.LogWarning(
                "Contact form notification failed to send to {AdminEmail} for submission from {Email}.",
                adminEmail,
                message.Email);
        }

        return sent;
    }

    private static (string Subject, string TextBody, string HtmlBody) BuildMessage(ContactMessage message)
    {
        var fullName = $"{message.FirstName} {message.LastName}".Trim();
        var subject = $"Bagly contact form — {fullName}";
        var company = string.IsNullOrWhiteSpace(message.CompanyName) ? "—" : message.CompanyName;

        var sb = new StringBuilder();
        sb.AppendLine("A new contact form submission was received on Bagly.");
        sb.AppendLine();
        sb.AppendLine($"Name: {fullName}");
        sb.AppendLine($"Email: {message.Email}");
        sb.AppendLine($"Phone: {message.Phone}");
        sb.AppendLine($"Company: {company}");
        sb.AppendLine();
        sb.AppendLine("Message:");
        sb.AppendLine(message.Message);
        sb.AppendLine();
        sb.AppendLine($"Submitted: {message.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine("— Bagly contact form");

        var textBody = sb.ToString();

        var htmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;">New contact form submission</h2>
              <p>
                <strong>Name:</strong> {Escape(fullName)}<br>
                <strong>Email:</strong> {Escape(message.Email)}<br>
                <strong>Phone:</strong> {Escape(message.Phone)}<br>
                <strong>Company:</strong> {Escape(company)}
              </p>
              <p><strong>Message</strong><br>{Escape(message.Message).Replace("\n", "<br>", StringComparison.Ordinal)}</p>
              <p style="color:#666;">Submitted: {message.CreatedAt:yyyy-MM-dd HH:mm} UTC</p>
              <p>— Bagly contact form</p>
            </div>
            """;

        return (subject, textBody, htmlBody);
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
