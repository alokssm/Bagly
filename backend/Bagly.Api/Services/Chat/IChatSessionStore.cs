namespace Bagly.Api.Services.Chat;

/// <summary>Keeps a bounded conversation transcript per chat session id (in memory, per-process).</summary>
public interface IChatSessionStore
{
    IReadOnlyList<AgentMessage> GetHistory(string sessionId);

    void AppendUser(string sessionId, string content);

    void AppendAssistant(string sessionId, string? content, List<AgentToolCall>? toolCalls = null);

    void AppendTool(string sessionId, string toolCallId, string name, string content);
}
