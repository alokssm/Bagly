using System.Collections.Concurrent;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services.Chat;

public sealed class InMemoryChatSessionStore(IOptions<ChatOptions> options) : IChatSessionStore
{
    private readonly ChatOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public IReadOnlyList<AgentMessage> GetHistory(string sessionId)
    {
        var session = GetOrCreate(sessionId);
        lock (session.Lock)
        {
            return [.. session.Messages];
        }
    }

    public void AppendUser(string sessionId, string content) =>
        Append(sessionId, AgentMessage.User(content));

    public void AppendAssistant(string sessionId, string? content, List<AgentToolCall>? toolCalls = null) =>
        Append(sessionId, AgentMessage.Assistant(content, toolCalls));

    public void AppendTool(string sessionId, string toolCallId, string name, string content) =>
        Append(sessionId, AgentMessage.Tool(toolCallId, name, content));

    private void Append(string sessionId, AgentMessage message)
    {
        var session = GetOrCreate(sessionId);
        var maxMessages = Math.Max(2, _options.SessionHistoryLimit * 2);

        lock (session.Lock)
        {
            session.LastActivityUtc = DateTime.UtcNow;
            session.Messages.Add(message);

            while (session.Messages.Count > maxMessages)
            {
                session.Messages.RemoveAt(0);
            }
        }
    }

    private Session GetOrCreate(string sessionId) =>
        _sessions.GetOrAdd(sessionId, static _ => new Session());

    private sealed class Session
    {
        public readonly object Lock = new();
        public readonly List<AgentMessage> Messages = [];
        public DateTime LastActivityUtc = DateTime.UtcNow;
    }
}
