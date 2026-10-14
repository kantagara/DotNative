using System.Net.Http.Json;

namespace FaliNamIgrac;

public interface IChatService
{
    IAsyncEnumerable<ChatUpdate> WatchInboxAsync(string userId, CancellationToken token);
    IAsyncEnumerable<ChatUpdate> WatchAsync(string chatId, CancellationToken token);
    Task<IReadOnlyList<ChatRoom>> ListAsync(string userId, CancellationToken token);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(string chatId, CancellationToken token);
    Task MarkReadAsync(string userId, string[] messageIds, CancellationToken token);
    Task<ChatMessage> SendAsync(ChatMessage message, CancellationToken token);
    Task CompleteSendAsync(ChatMessage message, CancellationToken token) => Task.CompletedTask;
    Task<IReadOnlyList<ChatUser>> SearchAsync(string text, CancellationToken token);
    Task<ChatRoom> UpdateGroupAsync(
        ChatRoom room,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    );
    Task LeaveGroupAsync(ChatRoom room, string userId, CancellationToken token);
    Task<ChatRoom> CreateGroupAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    );
    Task<ChatRoom> StartAsync(string userId, ChatUser other, CancellationToken token);
    async Task<ChatRoom?> GetAsync(string id, string userId, CancellationToken token) =>
        (await ListAsync(userId, token)).FirstOrDefault(room => room.Id == id);
    async Task<ChatRoom?> FindGameAsync(string gameId, string userId, CancellationToken token) =>
        (await ListAsync(userId, token)).FirstOrDefault(room => room.GameId == gameId);
    Task DeleteForEveryoneAsync(ChatRoom room, string userId, CancellationToken token) =>
        Task.FromException(new NotSupportedException());
    Task<ChatRoom> CreateGroupWithPhotoAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        byte[]? photo,
        CancellationToken token
    ) =>
        photo is null
            ? CreateGroupAsync(id, userId, title, users, token)
            : Task.FromException<ChatRoom>(new NotSupportedException());
}

public sealed partial class ChatService : IChatService, IDisposable
{
    private readonly IApiClient rest;
    private readonly ISessionService session;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly IGroupPhotoStorage photos;
    private readonly Func<
        Uri,
        string[],
        CancellationToken,
        Task<System.Net.WebSockets.WebSocket>
    > connect;

    public ChatService(IApiClient rest, ISessionService session)
        : this(rest, session, new HttpClient { Timeout = TimeSpan.FromSeconds(30) }) =>
        ownsHttp = true;

    public ChatService(
        IApiClient rest,
        ISessionService session,
        HttpClient http,
        Func<Uri, string[], CancellationToken, Task<System.Net.WebSockets.WebSocket>>? connect =
            null,
        IGroupPhotoStorage? photos = null
    )
    {
        (this.rest, this.session, this.http) = (rest, session, http);
        this.connect = connect ?? ConnectSocket;
        this.photos = photos ?? new GroupPhotoStorage(session);
    }

    public void Dispose()
    {
        if (ownsHttp)
            http.Dispose();
    }

    private const string RoomFields = "id participants createdAt title imageUrl admin gameId";
    private const string MessageFields = "id chatId senderId content createdAt seenBy";

    private async Task<ChatGraphData> Query(
        string query,
        ChatVariables variables,
        CancellationToken token
    )
    {
        var operation = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Split('(')[0];
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[FNI chat] {operation} begin");
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BackendConfiguration.ChatUrl
            );
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                await session.IdTokenAsync(token)
            );
            request.Content = JsonContent.Create(
                new ChatGraphRequest(query, variables),
                ChatJson.Default.ChatGraphRequest
            );
            using var response = await http.SendAsync(request, token);
            Console.WriteLine($"[FNI chat] {operation} HTTP {(int)response.StatusCode}");
            response.EnsureSuccessStatusCode();
            var body =
                await response.Content.ReadFromJsonAsync(ChatJson.Default.ChatGraphResponse, token)
                ?? throw new InvalidDataException("Missing chat response.");
            if (body.Errors is { Length: > 0 })
            {
                foreach (var error in body.Errors)
                    Console.WriteLine(
                        $"[FNI chat] {operation} GraphQL error type={error.ErrorType ?? "unspecified"}"
                    );
                if (
                    body.Errors.Any(error =>
                        error.ErrorType?.Contains(
                            "Unauthorized",
                            StringComparison.OrdinalIgnoreCase
                        ) == true
                    )
                )
                    throw new UnauthorizedAccessException();
                throw new InvalidOperationException(body.Errors[0].Message);
            }
            return body.Data ?? throw new InvalidDataException("Missing chat response.");
        }
        catch (Exception error)
        {
            Console.WriteLine(
                $"[FNI chat] {operation} failed type={error.GetType().Name} elapsedMs={elapsed.ElapsedMilliseconds}"
            );
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            {
                if (cause is System.Net.WebException web)
                    Console.WriteLine($"[FNI chat] {operation} network status={web.Status}");
                if (cause is System.Net.Sockets.SocketException socket)
                    Console.WriteLine(
                        $"[FNI chat] {operation} socket code={socket.SocketErrorCode}"
                    );
            }
            throw;
        }
        finally
        {
            Console.WriteLine(
                $"[FNI chat] {operation} end elapsedMs={elapsed.ElapsedMilliseconds}"
            );
        }
    }

    public async Task<IReadOnlyList<ChatRoom>> ListAsync(string userId, CancellationToken token)
    {
        var rooms = new List<ChatRoom>();
        var summaries = new Dictionary<string, ChatSummary>();
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var data = await Query(
                "query Summaries($userId: String!, $nextToken: String) { listUserChatsByUser(userId: $userId, limit: 100, nextToken: $nextToken) { items { chatId lastMessageContent lastMessageAt chat { "
                    + RoomFields
                    + " } } nextToken } }",
                new() { UserId = userId, NextToken = cursor },
                token
            );
            var page =
                data.ListUserChatsByUser
                ?? throw new InvalidDataException("Missing conversation summaries.");
            foreach (var summary in page.Items.OfType<ChatSummary>())
            {
                // Deleted or revoked relationships may remain in the index.
                if (summary.Chat is not { } room || !room.Participants.Contains(userId))
                    continue;
                rooms.Add(room);
                summaries[room.Id] = summary;
            }
            cursor = page.NextToken;
            if (cursor is not null && !cursors.Add(cursor))
                throw new InvalidDataException("Repeated summary cursor.");
        } while (cursor is not null);
        var users = new List<ChatUser>();
        foreach (var batch in rooms.SelectMany(room => room.Participants).Distinct().Chunk(50))
            users.AddRange(
                await rest.GetAsync<ChatUsersQuery, ChatUser[]>(
                    "chat/userInfo",
                    new(string.Join(',', batch)),
                    token,
                    authenticated: true
                )
            );
        return rooms
            .DistinctBy(room => room.Id)
            .Select(room =>
                room with
                {
                    Users = users.Where(user => room.Participants.Contains(user.Id)).ToArray(),
                    LastMessageContent = summaries.GetValueOrDefault(room.Id)?.LastMessageContent,
                    LastMessageAt = summaries.GetValueOrDefault(room.Id)?.LastMessageAt,
                }
            )
            .OrderByDescending(room => room.LastMessageAt ?? room.CreatedAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(
        string chatId,
        CancellationToken token
    )
    {
        var messages = new List<ChatMessage>();
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var data = await Query(
                "query Messages($chatId: ID!, $nextToken: String) { messagesByChat(chatId: $chatId, sortDirection: ASC, limit: 100, nextToken: $nextToken) { items { "
                    + MessageFields
                    + " } nextToken } }",
                new() { ChatId = chatId, NextToken = cursor },
                token
            );
            var page = data.MessagesByChat ?? throw new InvalidDataException("Missing messages.");
            messages.AddRange(
                page.Items.OfType<ChatMessage>().Where(message => message.ChatId == chatId)
            );
            cursor = page.NextToken;
            if (cursor is not null && !cursors.Add(cursor))
                throw new InvalidDataException("Repeated message cursor.");
        } while (cursor is not null);
        return messages
            .DistinctBy(message => message.Id)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToArray();
    }

    public async Task MarkReadAsync(string userId, string[] messageIds, CancellationToken token)
    {
        foreach (var batch in messageIds.Chunk(100))
        {
            using var response = await rest.PostJsonAsync(
                "chat/markAllSeenMessages",
                new ChatReadRequest(userId, batch),
                token,
                authenticated: true
            );
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<ChatMessage> SendAsync(ChatMessage message, CancellationToken token)
    {
        var data = await Query(
            "mutation Send($input: CreateMessageInput!) { createMessage(input: $input) { "
                + MessageFields
                + " } }",
            new()
            {
                Input = new()
                {
                    Id = message.Id,
                    ChatId = message.ChatId,
                    SenderId = message.SenderId,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt,
                    SeenBy = message.SeenBy,
                },
            },
            token
        );
        var sent =
            data.CreateMessage ?? throw new InvalidDataException("Message was not confirmed.");
        return sent;
    }

    private readonly SemaphoreSlim sendEffects = new(1, 1);

    public async Task CompleteSendAsync(ChatMessage sent, CancellationToken token)
    {
        await sendEffects.WaitAsync(token);
        try
        {
            var room = (
                await Query(
                    "query Existing($id: ID!) { getChat(id: $id) { " + RoomFields + " } }",
                    new() { Id = sent.ChatId },
                    token
                )
            ).GetChat;
            if (room is not null)
            {
                await EnsureIndexes(room, token);
                foreach (var participant in room.Participants)
                    await Query(
                        "mutation Summary($input: UpdateUserChatInput!) { updateUserChat(input: $input) { id userId chatId createdAt lastMessageContent lastMessageAt lastMessageSenderId } }",
                        new()
                        {
                            Input = new()
                            {
                                Id = participant + "_" + room.Id,
                                LastMessageContent = sent.Content,
                                LastMessageAt = sent.CreatedAt,
                                LastMessageSenderId = sent.SenderId,
                            },
                        },
                        token
                    );
                using var notification = await rest.PostJsonAsync(
                    "chat/sendMessageNotification",
                    new ChatNotificationRequest(
                        sent.Content,
                        sent.ChatId,
                        int.Parse(sent.SenderId),
                        sent.Id,
                        room.Participants.Where(id => id != sent.SenderId)
                            .Select(int.Parse)
                            .ToArray()
                    ),
                    token,
                    authenticated: true
                );
                notification.EnsureSuccessStatusCode();
            }
        }
        catch (Exception error)
            when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            // Message creation is authoritative; summary repair must not present it as unsent.
            Console.Error.WriteLine(
                "Chat message confirmed; conversation summary update failed: "
                    + error.GetType().Name
            );
        }
        finally
        {
            sendEffects.Release();
        }
    }

    public async Task<IReadOnlyList<ChatUser>> SearchAsync(string text, CancellationToken token) =>
        await rest.GetAsync<ChatSearchQuery, ChatUser[]>(
            "chat/search-user",
            new(text.Trim()),
            token,
            authenticated: true
        );

    public async Task<ChatRoom> StartAsync(string userId, ChatUser other, CancellationToken token)
    {
        var existing = (await ListAsync(userId, token)).FirstOrDefault(room =>
            room.Participants.Length == 2
            && room.Participants.Contains(other.Id)
            && string.IsNullOrEmpty(room.Title)
            && string.IsNullOrEmpty(room.GameId)
        );
        if (existing is not null)
        {
            await EnsureIndexes(existing, token);
            return existing;
        }
        var participants = new[] { userId, other.Id }.Order(StringComparer.Ordinal).ToArray();
        var id = "direct_" + string.Join('_', participants);
        try
        {
            var data = await Query(
                "mutation Start($input: CreateChatInput!) { createChat(input: $input) { "
                    + RoomFields
                    + " } }",
                new()
                {
                    Input = new()
                    {
                        Id = id,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Participants = participants,
                        Admin = "",
                    },
                },
                token
            );
            var room = (
                data.CreateChat ?? throw new InvalidDataException("Chat was not created.")
            ) with
            {
                Users = [other],
            };
            await EnsureIndexes(room, token);
            return room;
        }
        catch (InvalidOperationException)
        {
            // Another client may have created the deterministic direct chat concurrently.
            var data = await Query(
                "query Existing($id: ID!) { getChat(id: $id) { " + RoomFields + " } }",
                new() { Id = id },
                token
            );
            if (
                data.GetChat is not { } room
                || !room.Participants.Contains(userId)
                || !room.Participants.Contains(other.Id)
            )
                throw;
            await EnsureIndexes(room, token);
            return room with { Users = [other] };
        }
    }

    public async Task<ChatRoom> CreateGroupAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    ) => await CreateGroupWithPhotoAsync(id, userId, title, users, null, token);

    public async Task<ChatRoom> CreateGroupWithPhotoAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        byte[]? photo,
        CancellationToken token
    )
    {
        var participants = users.Select(user => user.Id).Append(userId).Distinct().ToArray();
        if (title.Trim().Length < 3 || participants.Length < 2)
            throw new ArgumentException("Invalid group.");
        var imageUrl = photo is null ? null : await photos.UploadAsync(id, photo, token);
        token.ThrowIfCancellationRequested();
        ChatRoom? room;
        try
        {
            room = (
                await Query(
                    "mutation Group($input: CreateChatInput!) { createChat(input: $input) { "
                        + RoomFields
                        + " } }",
                    new()
                    {
                        Input = new()
                        {
                            Id = id,
                            CreatedAt = DateTimeOffset.UtcNow,
                            Participants = participants,
                            Title = title.Trim(),
                            Admin = userId,
                            ImageUrl = imageUrl,
                        },
                    },
                    token
                )
            ).CreateChat;
        }
        catch (InvalidOperationException)
        {
            // The form reuses its id when a previous create response was lost.
            room = (
                await Query(
                    "query Existing($id: ID!) { getChat(id: $id) { " + RoomFields + " } }",
                    new() { Id = id },
                    token
                )
            ).GetChat;
            if (room is null || room.Admin != userId)
                throw;
        }
        if (room is null)
            throw new InvalidDataException("Group was not created.");
        await EnsureIndexes(room, token);
        return room with { Users = users.ToArray() };
    }

    public async Task<ChatRoom> UpdateGroupAsync(
        ChatRoom room,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    )
    {
        if (room.Admin != userId || title.Trim().Length < 3)
            throw new InvalidOperationException("Group update is not allowed.");
        var unresolved = room.Participants.Where(id => !room.Users.Any(user => user.Id == id));
        var participants = users
            .Select(user => user.Id)
            .Concat(unresolved)
            .Append(userId)
            .Distinct()
            .ToArray();
        var updated =
            (
                await Query(
                    "mutation EditGroup($input: UpdateChatInput!) { updateChat(input: $input) { "
                        + RoomFields
                        + " } }",
                    new()
                    {
                        Input = new()
                        {
                            Id = room.Id,
                            Title = title.Trim(),
                            Participants = participants,
                            Admin = userId,
                        },
                    },
                    token
                )
            ).UpdateChat
            ?? throw new InvalidDataException("Group update was not confirmed.");
        await EnsureIndexes(updated, token);
        foreach (var removed in room.Participants.Except(participants))
            await DeleteIndex(removed, room.Id, token);
        return updated with { Users = users.ToArray() };
    }

    public async Task LeaveGroupAsync(ChatRoom room, string userId, CancellationToken token)
    {
        if (string.IsNullOrEmpty(room.Admin) || !room.Participants.Contains(userId))
            throw new InvalidOperationException("Not a group member.");
        var remaining = room.Participants.Where(id => id != userId).ToArray();
        if (remaining.Length == 0)
            await DeleteForEveryoneAsync(room, userId, token);
        else
        {
            var result = await Query(
                "mutation Leave($input: UpdateChatInput!) { updateChat(input: $input) { "
                    + RoomFields
                    + " } }",
                new()
                {
                    Input = new()
                    {
                        Id = room.Id,
                        Participants = remaining,
                        Admin = room.Admin == userId ? remaining[0] : room.Admin,
                    },
                },
                token
            );
            if (result.UpdateChat is null || result.UpdateChat.Participants.Contains(userId))
                throw new InvalidDataException("Leaving group was not confirmed.");
        }
        await DeleteIndex(userId, room.Id, token);
    }

    private async Task DeleteIndex(string userId, string roomId, CancellationToken token) =>
        _ = await Query(
            "mutation RemoveIndex($input: DeleteUserChatInput!) { deleteUserChat(input: $input) { id } }",
            new() { Input = new() { Id = userId + "_" + roomId } },
            token
        );

    private async Task EnsureIndexes(ChatRoom room, CancellationToken token)
    {
        foreach (var participant in room.Participants)
        {
            var id = participant + "_" + room.Id;
            var existing = await Query(
                "query Index($id: ID!) { getUserChat(id: $id) { id } }",
                new() { Id = id },
                token
            );
            if (existing.GetUserChat is not null)
                continue;
            try
            {
                await Query(
                    "mutation Index($input: CreateUserChatInput!) { createUserChat(input: $input) { id userId chatId createdAt } }",
                    new()
                    {
                        Input = new()
                        {
                            Id = id,
                            UserId = participant,
                            ChatId = room.Id,
                            CreatedAt = room.CreatedAt,
                        },
                    },
                    token
                );
            }
            catch (InvalidOperationException)
            {
                // Concurrent clients can backfill the same deterministic membership row.
                var check = await Query(
                    "query Index($id: ID!) { getUserChat(id: $id) { id } }",
                    new() { Id = id },
                    token
                );
                if (check.GetUserChat is null)
                    throw;
            }
        }
    }
}
