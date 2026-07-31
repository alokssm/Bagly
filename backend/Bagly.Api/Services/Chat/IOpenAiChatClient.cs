namespace Bagly.Api.Services.Chat;

/// <summary>Thin client for an OpenAI-compatible chat-completions API with function/tool calling.</summary>
public interface IOpenAiChatClient
{
    Task<AgentCompletion> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken);
}
