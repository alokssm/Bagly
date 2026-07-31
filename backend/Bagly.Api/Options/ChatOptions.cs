namespace Bagly.Api.Options;

public class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Max user messages a single SignalR connection may send per rolling minute.</summary>
    public int MaxMessagesPerMinute { get; set; } = 12;

    /// <summary>User messages longer than this are truncated before reaching the agent.</summary>
    public int MaxMessageLength { get; set; } = 1000;

    /// <summary>How many prior turns are kept per session to build agent context.</summary>
    public int SessionHistoryLimit { get; set; } = 20;

    /// <summary>Safety cap on tool-call round-trips per user message when using the OpenAI agent.</summary>
    public int MaxToolIterations { get; set; } = 4;

    /// <summary>How long an idle in-memory session is retained before it can be evicted.</summary>
    public int SessionIdleMinutes { get; set; } = 60;
}
