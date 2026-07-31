namespace Bagly.Api.Services.Chat;

public sealed record ChatAgentResult(string Reply, bool UsedAi);

public interface IChatAgentService
{
    Task<ChatAgentResult> GetReplyAsync(string sessionId, string userMessage, CancellationToken cancellationToken);
}
