using System.Security.Claims;
using Bagly.Api.DTOs;
using Bagly.Api.Options;
using Bagly.Api.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Hubs;

/// <summary>
/// Real-time storefront chat hub. Customers chat with the AI/rule-based agent by default; once an
/// admin joins their conversation, human replies take over (AI is skipped) until the admin leaves.
/// Requires an authenticated JWT with role Customer or Admin; anonymous connections are rejected.
/// </summary>
[Authorize(Roles = "Customer,Admin")]
public sealed class ChatHub(
    IChatAgentService agentService,
    IChatRateLimiter rateLimiter,
    IChatConversationRegistry conversations,
    IOptions<ChatOptions> options,
    ILogger<ChatHub> logger) : Hub<IChatClient>
{
    private const string AdminsGroup = "admins";

    private readonly ChatOptions _options = options.Value;

    public override async Task OnConnectedAsync()
    {
        if (IsAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }
        else if (TryGetCustomerId(out var customerId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CustomerGroup(customerId));

            var name = Context.User?.Identity?.Name ?? "Customer";
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var snapshot = conversations.CustomerConnected(customerId, name, email, Context.ConnectionId);
            await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        rateLimiter.Release(Context.ConnectionId);

        if (IsAdmin)
        {
            var affected = conversations.AdminDisconnected(Context.ConnectionId);
            foreach (var customerId in affected)
            {
                var snapshot = conversations.GetSnapshot(customerId);
                if (snapshot is not null)
                {
                    await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);
                }

                await Clients.Group(CustomerGroup(customerId))
                    .ReceiveMessage(new ChatMessageDto("system", "The agent has left the chat. Our assistant is back to help.", DateTime.UtcNow));
            }
        }
        else if (TryGetCustomerId(out var customerId))
        {
            var snapshot = conversations.CustomerDisconnected(customerId, Context.ConnectionId);
            if (snapshot is not null)
            {
                await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins the caller's own AI-session group (per-browser session id), used to route assistant replies.</summary>
    public async Task Join(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 100)
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    /// <summary>Customer sends a message. Relayed to any watching admins; AI replies unless an admin has joined.</summary>
    public async Task SendMessage(string sessionId, string message)
    {
        if (!TryGetCustomerId(out var customerId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 100 || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!rateLimiter.TryConsume(Context.ConnectionId))
        {
            await Clients.Caller.ReceiveError("You're sending messages a bit too fast — please wait a few seconds and try again.");
            return;
        }

        var maxLength = Math.Max(1, _options.MaxMessageLength);
        var trimmedMessage = message.Trim();
        if (trimmedMessage.Length > maxLength)
        {
            trimmedMessage = trimmedMessage[..maxLength];
        }

        var snapshot = conversations.RecordMessage(customerId, "user", trimmedMessage);
        await Clients.Group(AdminsGroup).ReceiveConversationMessage(
            new ConversationMessageDto(customerId, "user", trimmedMessage, DateTime.UtcNow));
        await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);

        if (conversations.IsJoinedByAdmin(customerId))
        {
            // A human agent has joined this conversation — skip the AI, message is already relayed above.
            return;
        }

        await Clients.Group(sessionId).ReceiveTyping(true);

        try
        {
            var result = await agentService.GetReplyAsync(sessionId, trimmedMessage, Context.ConnectionAborted);
            await Clients.Group(sessionId).ReceiveMessage(new ChatMessageDto("assistant", result.Reply, DateTime.UtcNow));

            var updated = conversations.RecordMessage(customerId, "assistant", result.Reply);
            await Clients.Group(AdminsGroup).ReceiveConversationMessage(
                new ConversationMessageDto(customerId, "assistant", result.Reply, DateTime.UtcNow));
            await Clients.Group(AdminsGroup).ReceiveConversationUpdated(updated);
        }
        catch (OperationCanceledException)
        {
            // Connection closed mid-request — nothing to send back.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat agent failed to reply for session {SessionId}.", sessionId);
            await Clients.Caller.ReceiveError("Sorry, something went wrong on our end. Please try again.");
        }
        finally
        {
            await Clients.Group(sessionId).ReceiveTyping(false);
        }
    }

    /// <summary>Admin joins a customer's conversation: AI is skipped for that customer until an admin leaves.</summary>
    public async Task<IReadOnlyList<ConversationMessageDto>> JoinConversation(Guid customerId)
    {
        if (!IsAdmin)
        {
            return [];
        }

        var adminName = Context.User?.Identity?.Name ?? "Admin";
        var snapshot = conversations.AdminJoin(customerId, Context.ConnectionId, adminName);
        if (snapshot is null)
        {
            return [];
        }

        await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);
        await Clients.Group(CustomerGroup(customerId))
            .ReceiveMessage(new ChatMessageDto("system", $"{adminName} joined the chat.", DateTime.UtcNow));

        return conversations.GetHistory(customerId);
    }

    /// <summary>Admin leaves a customer's conversation: the AI resumes answering that customer.</summary>
    public async Task LeaveConversation(Guid customerId)
    {
        if (!IsAdmin)
        {
            return;
        }

        var snapshot = conversations.AdminLeave(customerId, Context.ConnectionId);
        if (snapshot is null)
        {
            return;
        }

        await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);

        if (!snapshot.IsJoined)
        {
            await Clients.Group(CustomerGroup(customerId))
                .ReceiveMessage(new ChatMessageDto("system", "The agent has left the chat. Our assistant is back to help.", DateTime.UtcNow));
        }
    }

    /// <summary>Admin sends a message directly to a customer; auto-joins the conversation if needed.</summary>
    public async Task SendAdminMessage(Guid customerId, string message)
    {
        if (!IsAdmin || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!rateLimiter.TryConsume(Context.ConnectionId))
        {
            await Clients.Caller.ReceiveError("You're sending messages a bit too fast — please wait a few seconds and try again.");
            return;
        }

        var maxLength = Math.Max(1, _options.MaxMessageLength);
        var trimmedMessage = message.Trim();
        if (trimmedMessage.Length > maxLength)
        {
            trimmedMessage = trimmedMessage[..maxLength];
        }

        var adminName = Context.User?.Identity?.Name ?? "Admin";

        if (!conversations.IsJoinedByAdmin(customerId))
        {
            conversations.AdminJoin(customerId, Context.ConnectionId, adminName);
        }

        var snapshot = conversations.RecordMessage(customerId, "admin", trimmedMessage);

        await Clients.Group(CustomerGroup(customerId))
            .ReceiveMessage(new ChatMessageDto("admin", trimmedMessage, DateTime.UtcNow));
        await Clients.Group(AdminsGroup).ReceiveConversationMessage(
            new ConversationMessageDto(customerId, "admin", trimmedMessage, DateTime.UtcNow));
        await Clients.Group(AdminsGroup).ReceiveConversationUpdated(snapshot);
    }

    /// <summary>Returns every known conversation (online or not) for the admin conversation list.</summary>
    public Task<IReadOnlyList<ConversationDto>> GetActiveConversations() =>
        Task.FromResult(IsAdmin ? conversations.GetSnapshots() : []);

    /// <summary>Returns the recent transcript for a conversation, for an admin opening it without joining yet.</summary>
    public Task<IReadOnlyList<ConversationMessageDto>> GetConversationHistory(Guid customerId) =>
        Task.FromResult(IsAdmin ? conversations.GetHistory(customerId) : []);

    private bool IsAdmin => Context.User?.IsInRole("Admin") == true;

    private bool TryGetCustomerId(out Guid customerId)
    {
        customerId = Guid.Empty;
        if (Context.User?.IsInRole("Customer") != true)
        {
            return false;
        }

        var value = Context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out customerId);
    }

    private static string CustomerGroup(Guid customerId) => $"customer:{customerId}";
}
