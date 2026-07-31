using Bagly.Api.DTOs;
using Bagly.Api.Services.Chat;

namespace Bagly.Api.Hubs;

/// <summary>Strongly-typed callback contract the hub uses to push events to connected clients.</summary>
public interface IChatClient
{
    /// <summary>Sent to a customer's own connections: assistant/admin/system replies for their conversation.</summary>
    Task ReceiveMessage(ChatMessageDto message);

    Task ReceiveError(string message);

    Task ReceiveTyping(bool isTyping);

    /// <summary>Sent to the "admins" group: a message (from a customer, the AI, or another admin) for a conversation.</summary>
    Task ReceiveConversationMessage(ConversationMessageDto message);

    /// <summary>Sent to the "admins" group whenever a conversation's metadata changes (online/joined/last message).</summary>
    Task ReceiveConversationUpdated(ConversationDto conversation);
}
