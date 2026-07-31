namespace Bagly.Api.Services.Chat;

/// <summary>Keyword/pattern based responder used when OpenAI is not configured.</summary>
public interface IRuleBasedChatResponder
{
    Task<string> RespondAsync(string message, CancellationToken cancellationToken);
}
