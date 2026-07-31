namespace Bagly.Api.Services.Chat;

/// <summary>Snapshot of a customer↔admin conversation, as seen from the admin chat panel.</summary>
public sealed record ConversationDto(
    Guid CustomerId,
    string Name,
    string Email,
    string? LastMessage,
    DateTime? LastAt,
    bool IsOnline,
    bool IsJoined,
    string? JoinedAdminName);

/// <summary>A single chat message tagged with the customer conversation it belongs to (used for admin broadcasts).</summary>
public sealed record ConversationMessageDto(
    Guid CustomerId,
    string Role,
    string Content,
    DateTime Timestamp);
