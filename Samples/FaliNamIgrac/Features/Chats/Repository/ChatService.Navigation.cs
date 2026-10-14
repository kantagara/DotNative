namespace FaliNamIgrac;

public sealed partial class ChatService
{
    public async Task<ChatRoom?> GetAsync(string id, string userId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Chat ID is required.");
        var room = (
            await Query(
                "query Existing($id: ID!) { getChat(id: $id) { " + RoomFields + " } }",
                new() { Id = id },
                token
            )
        ).GetChat;
        if (room is null)
            return null;
        if (!room.Participants.Contains(userId))
            throw new UnauthorizedAccessException();
        var users = new List<ChatUser>();
        foreach (var batch in room.Participants.Chunk(50))
            users.AddRange(
                await rest.GetAsync<ChatUsersQuery, ChatUser[]>(
                    "chat/userInfo",
                    new(string.Join(',', batch)),
                    token,
                    authenticated: true
                )
            );
        return room with { Users = users.ToArray() };
    }

    public async Task<ChatRoom?> FindGameAsync(
        string gameId,
        string userId,
        CancellationToken token
    )
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("Game ID is required.");
        var deterministic = await GetAsync("game_" + gameId, userId, token);
        if (deterministic is not null && deterministic.GameId == gameId)
            return deterministic;
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var data = await Query(
                "query GameChat($gameId: String!, $nextToken: String) { listChats(filter: {gameId: {eq: $gameId}}, limit: 100, nextToken: $nextToken) { items { "
                    + RoomFields
                    + " } nextToken } }",
                new() { GameId = gameId, NextToken = cursor },
                token
            );
            var page = data.ListChats ?? throw new InvalidDataException("Missing game chats.");
            var room = page
                .Items.OfType<ChatRoom>()
                .FirstOrDefault(item =>
                    item.GameId == gameId && item.Participants.Contains(userId)
                );
            if (room is not null)
                return await GetAsync(room.Id, userId, token);
            cursor = page.NextToken;
            if (cursor is not null && !cursors.Add(cursor))
                throw new InvalidDataException("Repeated game chat cursor.");
        } while (cursor is not null);
        return null;
    }

    public async Task DeleteForEveryoneAsync(ChatRoom room, string userId, CancellationToken token)
    {
        if (
            !room.Participants.Contains(userId)
            || !(
                room.Admin == userId
                || (
                    string.IsNullOrEmpty(room.Title)
                    && string.IsNullOrEmpty(room.GameId)
                    && room.Participants.Length == 2
                )
            )
        )
            throw new UnauthorizedAccessException();
        var data = await Query(
            "mutation DeleteConversation($input: DeleteChatInput!) { deleteChat(input: $input) { id } }",
            new() { Input = new() { Id = room.Id } },
            token
        );
        if (data.DeleteChat?.Id != room.Id)
            throw new InvalidDataException("Deletion was not confirmed.");
        // No deleteMessage: the server revokes membership/access to the deleted chat.
        // Orphan UserChat relationships are hidden by ChatAccess; no cross-account cleanup here.
    }
}
