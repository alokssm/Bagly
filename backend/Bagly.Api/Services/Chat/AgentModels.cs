namespace Bagly.Api.Services.Chat;

/// <summary>A single turn in the OpenAI-compatible chat-completion transcript.</summary>
public sealed class AgentMessage
{
    /// <summary>system, user, assistant, or tool.</summary>
    public required string Role { get; init; }
    public string? Content { get; init; }

    /// <summary>Only set on Role="tool" messages — the name of the tool that produced Content.</summary>
    public string? Name { get; init; }

    /// <summary>Only set on Role="tool" messages — links the result back to the originating tool call.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Only set on Role="assistant" messages that requested tool execution.</summary>
    public List<AgentToolCall>? ToolCalls { get; init; }

    public static AgentMessage System(string content) => new() { Role = "system", Content = content };

    public static AgentMessage User(string content) => new() { Role = "user", Content = content };

    public static AgentMessage Assistant(string? content, List<AgentToolCall>? toolCalls = null) =>
        new() { Role = "assistant", Content = content, ToolCalls = toolCalls };

    public static AgentMessage Tool(string toolCallId, string name, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Name = name, Content = content };
}

/// <summary>A function/tool invocation requested by the model.</summary>
public sealed class AgentToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ArgumentsJson { get; init; } = "{}";
}

/// <summary>Result of one chat-completion call: either final text, or tool calls to execute next.</summary>
public sealed class AgentCompletion
{
    public string? Content { get; init; }
    public List<AgentToolCall> ToolCalls { get; init; } = [];

    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>JSON-schema description of a tool the model is allowed to call.</summary>
public sealed class AgentToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Raw JSON Schema object (as a JSON string) describing the tool's parameters.</summary>
    public required string ParametersSchemaJson { get; init; }
}
