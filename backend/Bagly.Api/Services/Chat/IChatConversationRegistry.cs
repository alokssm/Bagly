namespace Bagly.Api.Services.Chat;

/// <summary>
/// In-memory registry of customer↔admin chat conversations for the storefront chat hub.
/// Tracks which customers are online, which admin (if any) has joined a conversation, and a
/// bounded transcript so an admin opening a conversation can see recent context.
/// </summary>
public interface IChatConversationRegistry
{
    /// <summary>Registers a customer connection and returns the up-to-date conversation snapshot.</summary>
    ConversationDto CustomerConnected(Guid customerId, string name, string email, string connectionId);

    /// <summary>Removes a customer connection; conversation stays in the registry (offline) with history intact.</summary>
    ConversationDto? CustomerDisconnected(Guid customerId, string connectionId);

    /// <summary>Records a customer-authored message and returns the updated snapshot.</summary>
    ConversationDto RecordMessage(Guid customerId, string role, string content);

    /// <summary>True when at least one admin connection has joined this customer's conversation.</summary>
    bool IsJoinedByAdmin(Guid customerId);

    /// <summary>Marks an admin connection as having joined a customer's conversation.</summary>
    ConversationDto? AdminJoin(Guid customerId, string connectionId, string adminName);

    /// <summary>Removes an admin connection from a customer's conversation.</summary>
    ConversationDto? AdminLeave(Guid customerId, string connectionId);

    /// <summary>Removes an admin connection from every conversation it had joined (on disconnect).</summary>
    IReadOnlyList<Guid> AdminDisconnected(string connectionId);

    /// <summary>Returns a snapshot for a single conversation, if known.</summary>
    ConversationDto? GetSnapshot(Guid customerId);

    /// <summary>Returns snapshots for every known conversation, most recently active first.</summary>
    IReadOnlyList<ConversationDto> GetSnapshots();

    /// <summary>Returns the bounded recent transcript for a conversation (may be empty).</summary>
    IReadOnlyList<ConversationMessageDto> GetHistory(Guid customerId);
}
