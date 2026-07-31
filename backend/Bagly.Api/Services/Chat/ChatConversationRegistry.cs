using System.Collections.Concurrent;

namespace Bagly.Api.Services.Chat;

public sealed class ChatConversationRegistry : IChatConversationRegistry
{
    private const int MaxTranscriptLength = 100;

    private readonly ConcurrentDictionary<Guid, Conversation> _conversations = new();

    // Tracks which conversations each admin connection has joined, so we can clean up on disconnect
    // without having to scan every conversation.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _adminJoins = new();

    public ConversationDto CustomerConnected(Guid customerId, string name, string email, string connectionId)
    {
        var conversation = GetOrCreate(customerId);
        lock (conversation.Lock)
        {
            conversation.Name = name;
            conversation.Email = email;
            conversation.OnlineConnectionIds.Add(connectionId);
            return ToDto(conversation);
        }
    }

    public ConversationDto? CustomerDisconnected(Guid customerId, string connectionId)
    {
        if (!_conversations.TryGetValue(customerId, out var conversation))
        {
            return null;
        }

        lock (conversation.Lock)
        {
            conversation.OnlineConnectionIds.Remove(connectionId);
            return ToDto(conversation);
        }
    }

    public ConversationDto RecordMessage(Guid customerId, string role, string content)
    {
        var conversation = GetOrCreate(customerId);
        lock (conversation.Lock)
        {
            conversation.LastMessage = content;
            conversation.LastAt = DateTime.UtcNow;
            conversation.Transcript.Add(new ConversationMessageDto(customerId, role, content, conversation.LastAt.Value));
            while (conversation.Transcript.Count > MaxTranscriptLength)
            {
                conversation.Transcript.RemoveAt(0);
            }

            return ToDto(conversation);
        }
    }

    public bool IsJoinedByAdmin(Guid customerId)
    {
        if (!_conversations.TryGetValue(customerId, out var conversation))
        {
            return false;
        }

        lock (conversation.Lock)
        {
            return conversation.JoinedAdminConnectionIds.Count > 0;
        }
    }

    public ConversationDto? AdminJoin(Guid customerId, string connectionId, string adminName)
    {
        if (!_conversations.TryGetValue(customerId, out var conversation))
        {
            return null;
        }

        lock (conversation.Lock)
        {
            conversation.JoinedAdminConnectionIds[connectionId] = adminName;
            conversation.JoinedAdminName = adminName;
            _adminJoins.GetOrAdd(connectionId, static _ => new ConcurrentDictionary<Guid, byte>())[customerId] = 0;
            return ToDto(conversation);
        }
    }

    public ConversationDto? AdminLeave(Guid customerId, string connectionId)
    {
        if (!_conversations.TryGetValue(customerId, out var conversation))
        {
            return null;
        }

        lock (conversation.Lock)
        {
            conversation.JoinedAdminConnectionIds.TryRemove(connectionId, out _);
            conversation.JoinedAdminName = conversation.JoinedAdminConnectionIds.Values.LastOrDefault();

            if (_adminJoins.TryGetValue(connectionId, out var joined))
            {
                joined.TryRemove(customerId, out _);
            }

            return ToDto(conversation);
        }
    }

    public IReadOnlyList<Guid> AdminDisconnected(string connectionId)
    {
        if (!_adminJoins.TryRemove(connectionId, out var joined))
        {
            return [];
        }

        var affected = new List<Guid>();
        foreach (var customerId in joined.Keys)
        {
            if (AdminLeave(customerId, connectionId) is not null)
            {
                affected.Add(customerId);
            }
        }

        return affected;
    }

    public ConversationDto? GetSnapshot(Guid customerId) =>
        _conversations.TryGetValue(customerId, out var conversation) ? ToDto(conversation) : null;

    public IReadOnlyList<ConversationDto> GetSnapshots() =>
        _conversations.Values
            .Select(ToDto)
            .OrderByDescending(c => c.LastAt ?? DateTime.MinValue)
            .ToList();

    public IReadOnlyList<ConversationMessageDto> GetHistory(Guid customerId)
    {
        if (!_conversations.TryGetValue(customerId, out var conversation))
        {
            return [];
        }

        lock (conversation.Lock)
        {
            return [.. conversation.Transcript];
        }
    }

    private Conversation GetOrCreate(Guid customerId) =>
        _conversations.GetOrAdd(customerId, static id => new Conversation(id));

    private static ConversationDto ToDto(Conversation conversation) => new(
        conversation.CustomerId,
        conversation.Name,
        conversation.Email,
        conversation.LastMessage,
        conversation.LastAt,
        conversation.OnlineConnectionIds.Count > 0,
        conversation.JoinedAdminConnectionIds.Count > 0,
        conversation.JoinedAdminName);

    private sealed class Conversation(Guid customerId)
    {
        public readonly object Lock = new();
        public Guid CustomerId { get; } = customerId;
        public string Name { get; set; } = "Customer";
        public string Email { get; set; } = string.Empty;
        public string? LastMessage { get; set; }
        public DateTime? LastAt { get; set; }
        public readonly HashSet<string> OnlineConnectionIds = new();
        public readonly ConcurrentDictionary<string, string> JoinedAdminConnectionIds = new();
        public string? JoinedAdminName { get; set; }
        public readonly List<ConversationMessageDto> Transcript = [];
    }
}
