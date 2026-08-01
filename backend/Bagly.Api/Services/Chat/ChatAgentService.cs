using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services.Chat;

/// <summary>
/// Orchestrates one chat turn: OpenAI tool-calling agent when OpenAi__ApiKey is configured,
/// otherwise the rule-based fallback. Both paths only answer using the shared chat tools.
/// </summary>
public sealed class ChatAgentService(
    IOpenAiChatClient openAiClient,
    IChatToolExecutor toolExecutor,
    IChatSessionStore sessionStore,
    IRuleBasedChatResponder ruleBasedResponder,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<ChatOptions> chatOptions,
    ILogger<ChatAgentService> logger) : IChatAgentService
{
    private const string SystemPrompt =
        "You are the Bagly bags store assistant. You can check product stock, set up email alerts for " +
        "out-of-stock products, and look up order status when given an order number and the email used on " +
        "the order. Order numbers look like BG-yyyyMMdd-xxxx (or BG-DEMO-xxxx for demo orders). Be concise " +
        "and friendly. Never invent stock levels or order details — always use the provided tools to look " +
        "them up, and if a tool reports something wasn't found, say so honestly rather than guessing.";

    private readonly OpenAiOptions _openAiOptions = openAiOptions.Value;
    private readonly ChatOptions _chatOptions = chatOptions.Value;

    public async Task<ChatAgentResult> GetReplyAsync(string sessionId, string userMessage, CancellationToken cancellationToken)
    {
        sessionStore.AppendUser(sessionId, userMessage);

        if (!_openAiOptions.IsConfigured)
        {
            var reply = await ruleBasedResponder.RespondAsync(sessionId, userMessage, cancellationToken);
            sessionStore.AppendAssistant(sessionId, reply);
            return new ChatAgentResult(reply, UsedAi: false);
        }

        try
        {
            var reply = await RunAgentLoopAsync(sessionId, cancellationToken);
            return new ChatAgentResult(reply, UsedAi: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI chat agent failed for session {SessionId}; using rule-based fallback.", sessionId);
            var reply = await ruleBasedResponder.RespondAsync(sessionId, userMessage, cancellationToken);
            sessionStore.AppendAssistant(sessionId, reply);
            return new ChatAgentResult(reply, UsedAi: false);
        }
    }

    private async Task<string> RunAgentLoopAsync(string sessionId, CancellationToken cancellationToken)
    {
        var maxIterations = Math.Max(1, _chatOptions.MaxToolIterations);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var history = sessionStore.GetHistory(sessionId);
            var messages = new List<AgentMessage>(history.Count + 1) { AgentMessage.System(SystemPrompt) };
            messages.AddRange(history);

            var completion = await openAiClient.CompleteAsync(messages, cancellationToken);

            if (!completion.HasToolCalls)
            {
                var reply = string.IsNullOrWhiteSpace(completion.Content)
                    ? "Sorry, I didn't quite catch that — could you rephrase?"
                    : completion.Content!.Trim();
                sessionStore.AppendAssistant(sessionId, reply);
                return reply;
            }

            sessionStore.AppendAssistant(sessionId, completion.Content, completion.ToolCalls);

            foreach (var toolCall in completion.ToolCalls)
            {
                var result = await toolExecutor.ExecuteAsync(toolCall.Name, toolCall.ArgumentsJson, cancellationToken);
                sessionStore.AppendTool(sessionId, toolCall.Id, toolCall.Name, result);
            }
        }

        const string exhaustedReply = "I'm having trouble completing that request right now — could you try again in a moment?";
        sessionStore.AppendAssistant(sessionId, exhaustedReply);
        return exhaustedReply;
    }
}
