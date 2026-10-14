using System.Net;
using System.Text.Json;
using FaliNamIgrac;

static class FeatureServiceTests
{
    static readonly ChatRoom Room = new(
        "group",
        ["1", "2"],
        DateTimeOffset.UnixEpoch,
        "Group",
        Admin: "1"
    );

    public static async Task RunAsync()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/") };
        using var rest = new ApiClient(http, new FakeSession());
        var photos = new Photos();
        using var service = new ChatService(rest, new FakeSession(), http, photos: photos);
        photos.Fail = true;
        await Throws<IOException>(() =>
            service.CreateGroupWithPhotoAsync(
                "group",
                "1",
                "Group",
                [new("2", "Two", "User")],
                [1],
                default
            )
        );
        Check(handler.Requests == 0, "Failed photo upload must prevent chat creation");
        photos.Fail = false;
        var group = await service.CreateGroupWithPhotoAsync(
            "group",
            "1",
            "Group",
            [new("2", "Two", "User")],
            [1],
            default
        );
        Check(
            group.ImageUrl == "public/group-photos/group.png" && handler.Photo == group.ImageUrl,
            "Photo path must reach GraphQL"
        );
        Check(
            photos.Keys.SequenceEqual(new[] { "group", "group" }),
            "Retry must reuse group upload identity"
        );
        var found = await service.FindGameAsync("42", "1", default);
        Check(
            found?.Id == "legacy" && handler.GamePages == 2,
            "Game lookup must traverse empty filtered pages"
        );
        await service.DeleteForEveryoneAsync(Room, "1", default);
        Check(
            handler.Deletes == 1 && !handler.DeleteMessage,
            "Delete must use chat deletion, never delete messages"
        );
        await Throws<UnauthorizedAccessException>(() =>
            service.DeleteForEveryoneAsync(Room, "2", default)
        );
        Check(handler.Deletes == 1, "Nonadmin group member must not delete group");
        handler.ConfirmDelete = false;
        await Throws<InvalidDataException>(() =>
            service.DeleteForEveryoneAsync(Room, "1", default)
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Throws<OperationCanceledException>(() =>
            service.GetAsync("group", "1", cancelled.Token)
        );
        Console.WriteLine(
            "PASS: failed/successful group photo, stable upload key, paginated game lookup, delete semantics/authorization/confirmation, cancellation"
        );
    }

    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static async Task Throws<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }

    sealed class Photos : IGroupPhotoStorage
    {
        public bool Fail;
        public List<string> Keys = [];

        public Task<string> UploadAsync(string id, byte[] photo, CancellationToken token)
        {
            Keys.Add(id);
            return Fail
                ? Task.FromException<string>(new IOException())
                : Task.FromResult("public/group-photos/" + id + ".png");
        }
    }

    sealed class Handler : HttpMessageHandler
    {
        public int Requests,
            GamePages,
            Deletes;
        public bool DeleteMessage,
            ConfirmDelete = true;
        public string? Photo;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            Requests++;
            if (request.Method == HttpMethod.Get)
                return Reply("[]");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = doc.RootElement;
            var query = root.GetProperty("query").GetString()!;
            var variables = root.GetProperty("variables");
            if (query.Contains("deleteMessage"))
                DeleteMessage = true;
            if (query.Contains("createChat"))
            {
                Photo = variables.GetProperty("input").GetProperty("imageUrl").GetString();
                return Reply(
                    "{\"data\":{\"createChat\":"
                        + variables.GetProperty("input").GetRawText()
                        + "}}"
                );
            }
            if (query.Contains("getUserChat"))
                return Reply("{\"data\":{\"getUserChat\":{\"id\":\"index\"}}}");
            if (query.Contains("listChats"))
            {
                GamePages++;
                return Reply(
                    GamePages == 1
                        ? "{\"data\":{\"listChats\":{\"items\":[],\"nextToken\":\"next\"}}}"
                        : "{\"data\":{\"listChats\":{\"items\":["
                            + Legacy
                            + "],\"nextToken\":null}}}"
                );
            }
            if (query.Contains("getChat"))
                return Reply(
                    "{\"data\":{\"getChat\":"
                        + (variables.GetProperty("id").GetString() == "legacy" ? Legacy : "null")
                        + "}}"
                );
            if (query.Contains("deleteChat"))
            {
                Deletes++;
                return Reply(
                    "{\"data\":{\"deleteChat\":"
                        + (ConfirmDelete ? "{\"id\":\"group\"}" : "null")
                        + "}}"
                );
            }
            throw new Exception(query);
        }

        const string Legacy =
            """{"id":"legacy","participants":["1","2"],"createdAt":"2026-01-01T00:00:00Z","gameId":"42","admin":"1","title":"Game"}""";

        static HttpResponseMessage Reply(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
