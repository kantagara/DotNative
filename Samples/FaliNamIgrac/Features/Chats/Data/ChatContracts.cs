using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public sealed record ChatMessage(
    string Id,
    string ChatId,
    string SenderId,
    string Content,
    DateTimeOffset CreatedAt,
    string[] SeenBy
);

public sealed record ChatUser(
    [property: JsonConverter(typeof(ScalarStringConverter))] string Id,
    string FirstName,
    string LastName,
    string? Nickname = null,
    string? ProfileImageUrl = null
)
{
    public string Name => (FirstName + " " + LastName).Trim();
}

public sealed record ChatRoom(
    string Id,
    string[] Participants,
    DateTimeOffset CreatedAt,
    string? Title = null,
    string? ImageUrl = null,
    string? Admin = null,
    string? GameId = null
)
{
    public string? LastMessageContent { get; init; }
    public DateTimeOffset? LastMessageAt { get; init; }
    public ChatUser[] Users { get; init; } = [];

    public string DisplayName(string userId) =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : string.Join(", ", Users.Where(user => user.Id != userId).Select(user => user.Name));
}

internal sealed record ChatConnection<T>(T?[] Items, string? NextToken);

internal sealed record ChatSummary(
    string ChatId,
    string? LastMessageContent,
    DateTimeOffset? LastMessageAt,
    ChatRoom? Chat = null
);

internal sealed record ChatGraphData(
    ChatConnection<ChatSummary>? ListUserChatsByUser = null,
    ChatConnection<ChatRoom>? ListChats = null,
    ChatConnection<ChatMessage>? MessagesByChat = null,
    ChatRoom? GetChat = null,
    ChatRoom? CreateChat = null,
    ChatRoom? UpdateChat = null,
    ChatIndexEntry? DeleteChat = null,
    ChatIndexEntry? DeleteUserChat = null,
    ChatMessage? CreateMessage = null,
    ChatMessage? OnCreateMessage = null,
    ChatIndexEntry? OnCreateUserChat = null,
    ChatIndexEntry? OnUpdateUserChat = null,
    ChatIndexEntry? GetUserChat = null,
    ChatIndexEntry? CreateUserChat = null,
    ChatIndexEntry? UpdateUserChat = null
);

internal sealed record ChatIndexEntry(string Id);

internal sealed record ChatGraphError(string Message, string? ErrorType = null);

internal sealed record ChatGraphResponse(ChatGraphData? Data, ChatGraphError[]? Errors);

internal sealed record ChatGraphRequest(string Query, ChatVariables Variables);

internal sealed record ChatVariables
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextToken { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatInput? Input { get; init; }
}

// A closed GraphQL input contract shared by the chat and message mutations.
internal sealed record ChatInput
{
    public required string Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? SeenBy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Participants { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Admin { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastMessageContent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastMessageAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastMessageSenderId { get; init; }
}

internal sealed record ChatUsersQuery(string UserIds);

internal sealed record ChatSearchQuery(string SearchTerm);

internal sealed record ChatReadRequest(string UserId, string[] MessageIds);

internal sealed record ChatNotificationRequest(
    string Message,
    string ChatId,
    int SenderId,
    string MessageId,
    int[] UserIds
);
