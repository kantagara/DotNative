using System.Text.Json;
using System.Text.Json.Serialization;
using DotNative.Hive;

namespace FaliNamIgrac;

public sealed record ChatSnapshot(
    int Version,
    ChatRoom[] Rooms,
    Dictionary<string, ChatMessage[]> Messages,
    Dictionary<string, string> Drafts,
    Dictionary<string, ChatMessage> Pending
);

[JsonSerializable(typeof(ChatSnapshot))]
internal partial class ChatCacheJson : JsonSerializerContext;

public interface IChatCache
{
    Task<ChatSnapshot?> ReadAsync(string userId, CancellationToken token);
    Task WriteAsync(string userId, ChatSnapshot snapshot, CancellationToken token);
}

public sealed class ChatCache(IHive hive, string? legacyDirectory = null) : IChatCache
{
    public Task<ChatSnapshot?> ReadAsync(string userId, CancellationToken token) =>
        Task.Run(
            async () =>
            {
                using var box = await hive.OpenBoxAsync(BoxName(userId), cancellationToken: token);
                ChatSnapshot? data;
                try
                {
                    if (legacyDirectory is not null)
                        await box.ImportLegacyJsonAsync(
                            "snapshot",
                            legacyDirectory,
                            userId,
                            ChatCacheJson.Default.ChatSnapshot,
                            token
                        );
                    data = box.Get("snapshot", ChatCacheJson.Default.ChatSnapshot);
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (InvalidDataException)
                {
                    return null;
                }
                if (
                    data
                    is not {
                        Version: 1,
                        Rooms: not null,
                        Messages: not null,
                        Drafts: not null,
                        Pending: not null
                    }
                )
                    return null;
                // Treat malformed/foreign records as a cache miss, never as authoritative membership.
                if (
                    data.Rooms.Any(room =>
                        room is null
                        || room.Participants is null
                        || !room.Participants.Contains(userId)
                        || room.Users is null
                        || room.Users.Any(user =>
                            user is null
                            || user.Id is null
                            || user.FirstName is null
                            || user.LastName is null
                        )
                        || room.Id is null
                    )
                    || data.Messages.Any(pair =>
                        pair.Value is null
                        || pair.Value.Any(message =>
                            message is null
                            || message.ChatId != pair.Key
                            || message.Id is null
                            || message.SeenBy is null
                            || message.Content is null
                            || message.SenderId is null
                        )
                    )
                    || data.Drafts.Any(pair => pair.Value is null)
                    || data.Pending.Any(pair =>
                        pair.Value is null
                        || pair.Value.ChatId != pair.Key
                        || pair.Value.SenderId != userId
                        || pair.Value.Id is null
                        || pair.Value.Content is null
                        || pair.Value.SeenBy is null
                    )
                )
                    return null;
                return data;
            },
            token
        );

    public Task WriteAsync(string userId, ChatSnapshot snapshot, CancellationToken token) =>
        Task.Run(
            async () =>
            {
                using var box = await hive.OpenBoxAsync(BoxName(userId), cancellationToken: token);
                token.ThrowIfCancellationRequested();
                box.Put("snapshot", snapshot, ChatCacheJson.Default.ChatSnapshot);
            },
            token
        );

    private static string BoxName(string userId) => "chat-" + HiveKeys.Hash(userId)[..48];
}
