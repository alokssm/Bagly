using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services.Chat;

public class OpenAiChatClient(HttpClient httpClient, IOptions<OpenAiOptions> options, ILogger<OpenAiChatClient> logger)
    : IOpenAiChatClient
{
    private readonly OpenAiOptions _options = options.Value;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<AgentCompletion> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("OpenAI is not configured. Set OpenAi__ApiKey.");
        }

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var payload = new
        {
            model = _options.Model,
            messages = messages.Select(BuildMessagePayload).ToList(),
            tools = ChatToolDefinitions.All.Select(BuildToolPayload).ToList(),
            tool_choice = "auto",
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, RequestJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("OpenAI chat completion timed out after {TimeoutSeconds}s.", _options.TimeoutSeconds);
            throw new InvalidOperationException("The AI service timed out.");
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "OpenAI chat completion failed ({StatusCode}): {Body}",
                (int)response.StatusCode,
                Truncate(raw, 500));
            throw new InvalidOperationException($"AI service returned HTTP {(int)response.StatusCode}.");
        }

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(raw)
            ?? throw new InvalidOperationException("The AI service returned an unreadable response.");

        var message = parsed.Choices?.FirstOrDefault()?.Message
            ?? throw new InvalidOperationException("The AI service response had no message.");

        var toolCalls = (message.ToolCalls ?? [])
            .Where(tc => tc.Function is not null && !string.IsNullOrWhiteSpace(tc.Id))
            .Select(tc => new AgentToolCall
            {
                Id = tc.Id!,
                Name = tc.Function!.Name ?? string.Empty,
                ArgumentsJson = string.IsNullOrWhiteSpace(tc.Function!.Arguments) ? "{}" : tc.Function!.Arguments!,
            })
            .ToList();

        return new AgentCompletion
        {
            Content = message.Content,
            ToolCalls = toolCalls,
        };
    }

    private static object BuildMessagePayload(AgentMessage message)
    {
        if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
        {
            return new
            {
                role = "tool",
                tool_call_id = message.ToolCallId,
                name = message.Name,
                content = message.Content ?? string.Empty,
            };
        }

        if (string.Equals(message.Role, "assistant", StringComparison.Ordinal) && message.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = message.Content,
                tool_calls = message.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson },
                }).ToList(),
            };
        }

        return new { role = message.Role, content = message.Content ?? string.Empty };
    }

    private static object BuildToolPayload(AgentToolDefinition tool) =>
        new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = JsonSerializer.Deserialize<JsonElement>(tool.ParametersSchemaJson),
            },
        };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatResponseMessage? Message { get; set; }
    }

    private sealed class ChatResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<ChatResponseToolCall>? ToolCalls { get; set; }
    }

    private sealed class ChatResponseToolCall
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("function")]
        public ChatResponseFunctionCall? Function { get; set; }
    }

    private sealed class ChatResponseFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }
}
