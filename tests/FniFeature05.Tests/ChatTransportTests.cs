using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FaliNamIgrac;

internal static class ChatTransportTests
{
    private const string Room = """
        {"id":"room","participants":["1","2"],"createdAt":"2026-10-01T10:00:00Z","title":null}
        """;
    private const string Message = """
        {"id":"m","chatId":"room","senderId":"2","content":"Hello","createdAt":"2026-10-01T10:00:00Z","seenBy":["2"]}
        """;

    public static async Task RunAsync()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/"),
        };
        using var rest = new ApiClient(http, new FakeSession());
        using var socket = new Socket();
        Uri? connectionUri = null;
        string[]? protocols = null;
        using var service = new ChatService(
            rest,
            new FakeSession(),
            http,
            (uri, headers, _) =>
            {
                connectionUri = uri;
                protocols = headers;
                return Task.FromResult<WebSocket>(socket);
            }
        );
        var rooms = await service.ListAsync("1", default);
        Check(
            rooms.Count == 1
                && rooms[0].DisplayName("1") == "Other Player"
                && rooms[0].LastMessageContent == "Hello",
            "Conversation names or summaries were not decoded"
        );
        Check(handler.RoomScans == 0, "Inbox must use the user index without scanning all chats");
        var messages = await service.MessagesAsync("room", default);
        Check(messages.Count == 1 && messages[0].Id == "m", "Duplicate messages were not removed");
        var sent = await service.SendAsync(
            new("out", "room", "1", "Quotes: \"hello\"\nnew line", DateTimeOffset.UtcNow, ["1"]),
            default
        );
        Check(
            sent.Id == "out" && sent.Content.Contains('\n'),
            "Message transport corrupted content"
        );
        Check(handler.GraphAuthorization == "unused", "AppSync did not use raw Cognito JWT");
        await service.MarkReadAsync("1", ["m"], default);
        Check(handler.ReadBody?.Contains("messageIds") == true, "Read receipt was not sent");
        handler.FailGraph = true;
        try
        {
            await service.MessagesAsync("room", default);
            throw new Exception("GraphQL errors were ignored");
        }
        catch (InvalidOperationException) { }
        handler.FailGraph = false;

        socket.Frames.Enqueue(
            "{\"type\":\"connection_ack\",\"payload\":{\"connectionTimeoutMs\":30000}}"
        );
        socket.Frames.Enqueue("{\"type\":\"start_ack\"}");
        socket.Frames.Enqueue(
            "{\"type\":\"data\",\"payload\":{\"data\":{\"onCreateMessage\":" + Message + "}}}"
        );
        await using (var watcher = service.WatchAsync("room", default).GetAsyncEnumerator())
        {
            Check(
                await watcher.MoveNextAsync() && watcher.Current.Connected,
                "Subscription did not connect"
            );
            Check(
                await watcher.MoveNextAsync() && watcher.Current.Message?.Id == "m",
                "Realtime message was not decoded"
            );
        }
        Check(
            connectionUri?.Scheme == "wss" && connectionUri.Host.Contains("appsync-realtime-api"),
            "Wrong realtime endpoint"
        );
        Check(
            protocols is ["graphql-ws", _] && protocols[1].StartsWith("header-"),
            "AppSync handshake headers missing"
        );
        using var start = JsonDocument.Parse(socket.Sent[1]);
        Check(
            start.RootElement.GetProperty("type").GetString() == "start",
            "Subscription registration missing"
        );
        var payload = start.RootElement.GetProperty("payload");
        using var data = JsonDocument.Parse(payload.GetProperty("data").GetString()!);
        Check(
            data.RootElement.GetProperty("variables").GetProperty("chatId").GetString() == "room",
            "Subscription filter lost"
        );
        Check(
            payload
                .GetProperty("extensions")
                .GetProperty("authorization")
                .GetProperty("Authorization")
                .GetString() == "unused",
            "Subscription authorization missing"
        );
        Check(socket.Disposed, "Leaving chat leaked the socket");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string? GraphAuthorization,
            ReadBody,
            AccountBody;
        public bool FailGraph;
        public int RoomScans;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.EndsWith("userByEmail"))
                body = """
                    {"userData":{"user_id":1,"username":"one@example.com","firstname":"One","lastname":"Player","nickname":"one","birth_date":"2000-01-01","location_lat":44,"location_lon":20,"profile_img_url":"existing-photo","sports":[{"sportName":"tennis","skillLevel":4,"notificationType":"OFF"}]}}
                    """;
            else if (path.EndsWith("update-user"))
            {
                AccountBody = await request.Content!.ReadAsStringAsync(token);
                body = "{}";
            }
            else if (path.EndsWith("userInfo"))
                body = "[{\"id\":2,\"firstName\":\"Other\",\"lastName\":\"Player\"}]";
            else if (path.EndsWith("markAllSeenMessages"))
            {
                ReadBody = await request.Content!.ReadAsStringAsync(token);
                body = "{}";
            }
            else
            {
                GraphAuthorization = request.Headers.GetValues("Authorization").Single();
                using var document = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(token)
                );
                var query = document.RootElement.GetProperty("query").GetString()!;
                if (FailGraph)
                    body = "{\"errors\":[{\"message\":\"Denied\"}]}";
                else if (query.Contains("listChats("))
                {
                    RoomScans++;
                    body = "{\"data\":{\"listChats\":{\"items\":[],\"nextToken\":null}}}";
                }
                else if (query.Contains("listUserChatsByUser("))
                    body =
                        "{\"data\":{\"listUserChatsByUser\":{\"items\":[{\"chatId\":\"room\",\"lastMessageContent\":\"Hello\",\"lastMessageAt\":\"2026-10-01T10:00:00Z\",\"chat\":"
                        + Room
                        + "}],\"nextToken\":null}}}";
                else if (query.Contains("messagesByChat("))
                    body =
                        "{\"data\":{\"messagesByChat\":{\"items\":["
                        + Message
                        + ","
                        + Message
                        + "],\"nextToken\":null}}}";
                else if (query.Contains("createMessage("))
                    body =
                        "{\"data\":{\"createMessage\":"
                        + document
                            .RootElement.GetProperty("variables")
                            .GetProperty("input")
                            .GetRawText()
                        + "}}";
                else
                    body = "{\"data\":{\"getChat\":null}}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Socket : WebSocket
    {
        public readonly Queue<string> Frames = new();
        public readonly List<string> Sent = [];
        public bool Disposed;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State =>
            Disposed ? WebSocketState.Closed : WebSocketState.Open;
        public override string? SubProtocol => "graphql-ws";

        public override void Abort() => Disposed = true;

        public override void Dispose() => Disposed = true;

        public override Task CloseAsync(
            WebSocketCloseStatus status,
            string? description,
            CancellationToken token
        )
        {
            Dispose();
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus status,
            string? description,
            CancellationToken token
        ) => CloseAsync(status, description, token);

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType type,
            bool end,
            CancellationToken token
        )
        {
            Sent.Add(Encoding.UTF8.GetString(buffer));
            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken token
        )
        {
            var bytes = Encoding.UTF8.GetBytes(Frames.Dequeue());
            bytes.CopyTo(buffer.AsSpan());
            return Task.FromResult(
                new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true)
            );
        }
    }
}
