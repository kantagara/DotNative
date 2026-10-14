using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public sealed record ChatUpdate(ChatMessage? Message = null, bool Connected = false);

internal sealed record ChatSocketAuth(
    string Host,
    [property: JsonPropertyName("Authorization")] string Authorization
);

internal sealed record ChatSocketExtensions(ChatSocketAuth Authorization);

internal sealed record ChatSocketPayload(string Data, ChatSocketExtensions Extensions);

internal sealed record ChatSocketFrame(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ChatSocketPayload? Payload = null
);

public sealed partial class ChatService
{
    public IAsyncEnumerable<ChatUpdate> WatchAsync(string chatId, CancellationToken token) =>
        WatchCore(
            [
                new(
                    "subscription Messages($chatId: ID!) { onCreateMessage(filter: {chatId: {eq: $chatId}}) { "
                        + MessageFields
                        + " } }",
                    new() { ChatId = chatId }
                ),
            ],
            chatId,
            token
        );

    public IAsyncEnumerable<ChatUpdate> WatchInboxAsync(string userId, CancellationToken token) =>
        WatchCore(
            [
                new(
                    "subscription NewConversation($userId: String!) { onCreateUserChat(filter: {userId: {eq: $userId}}) { id } }",
                    new() { UserId = userId }
                ),
                new(
                    "subscription ConversationUpdated($userId: String!) { onUpdateUserChat(filter: {userId: {eq: $userId}}) { id } }",
                    new() { UserId = userId }
                ),
            ],
            null,
            token
        );

    private async IAsyncEnumerable<ChatUpdate> WatchCore(
        ChatGraphRequest[] requests,
        string? chatId,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        var endpoint = new Uri(BackendConfiguration.ChatUrl);
        var auth = new ChatSocketAuth(endpoint.Host, await session.IdTokenAsync(token));
        var encoded = Convert
            .ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(auth, ChatJson.Default.ChatSocketAuth)
            )
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var uri = new UriBuilder(endpoint)
        {
            Scheme = "wss",
            Port = -1,
            Host = endpoint.Host.Replace(
                ".appsync-api.",
                ".appsync-realtime-api.",
                StringComparison.Ordinal
            ),
        }.Uri;
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
        handshake.CancelAfter(TimeSpan.FromSeconds(20));
        using var socket = await connect(uri, ["graphql-ws", "header-" + encoded], handshake.Token);
        await SendFrame(socket, new("connection_init"), handshake.Token);

        var keepalive = TimeSpan.FromSeconds(60);
        var started = false;
        while (!token.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(keepalive);
            using var frame = await ReceiveFrame(socket, started ? timeout.Token : handshake.Token);
            var root = frame.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "connection_ack")
            {
                if (
                    root.TryGetProperty("payload", out var ack)
                    && ack.TryGetProperty("connectionTimeoutMs", out var millis)
                )
                    keepalive = TimeSpan.FromMilliseconds(
                        Math.Clamp(millis.GetInt32(), 1000, 300000)
                    );
                foreach (var request in requests)
                    await SendFrame(
                        socket,
                        new(
                            "start",
                            Guid.NewGuid().ToString(),
                            new(
                                JsonSerializer.Serialize(
                                    request,
                                    ChatJson.Default.ChatGraphRequest
                                ),
                                new(auth)
                            )
                        ),
                        handshake.Token
                    );
            }
            else if (type == "start_ack")
            {
                started = true;
                yield return new(Connected: true);
            }
            else if (type == "data")
            {
                var response = root.GetProperty("payload")
                    .Deserialize(ChatJson.Default.ChatGraphResponse);
                if (response?.Errors is { Length: > 0 })
                    throw new InvalidOperationException("Chat subscription rejected.");
                if (response?.Data?.OnCreateMessage is { } message && message.ChatId == chatId)
                    yield return new(message, true);
                else if (
                    chatId is null
                    && (
                        response?.Data?.OnCreateUserChat is not null
                        || response?.Data?.OnUpdateUserChat is not null
                    )
                )
                    yield return new(Connected: true);
            }
            else if (type is "error" or "connection_error" or "complete")
                throw new InvalidOperationException("Chat subscription disconnected.");
        }
    }

    private static async Task<WebSocket> ConnectSocket(
        Uri uri,
        string[] protocols,
        CancellationToken token
    )
    {
        var socket = new ClientWebSocket();
        foreach (var protocol in protocols)
            socket.Options.AddSubProtocol(protocol);
        try
        {
            await socket.ConnectAsync(uri, token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static Task SendFrame(
        WebSocket socket,
        ChatSocketFrame frame,
        CancellationToken token
    ) =>
        socket.SendAsync(
            new ArraySegment<byte>(
                JsonSerializer.SerializeToUtf8Bytes(frame, ChatJson.Default.ChatSocketFrame)
            ),
            WebSocketMessageType.Text,
            true,
            token
        );

    private static async Task<JsonDocument> ReceiveFrame(WebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (received.MessageType != WebSocketMessageType.Text)
                throw new IOException("Chat socket closed.");
            stream.Write(buffer, 0, received.Count);
            if (stream.Length > 1024 * 1024)
                throw new InvalidDataException("Chat socket frame exceeded limit.");
        } while (!received.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray());
    }
}
