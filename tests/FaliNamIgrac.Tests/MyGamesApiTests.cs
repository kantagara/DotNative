using System.Net;
using FaliNamIgrac;

internal static class MyGamesApiTests
{
    public static async Task RunAsync()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/"),
        };
        var session = new FakeSession();
        using var transport = new ApiClient(http, session);
        var api = new GamesApi(transport, session);
        var result = await api.MyGamesAsync(
            FakeGames.Profile with
            {
                Id = "player/id",
            },
            7,
            2,
            true,
            default
        );
        Require(
            handler.Uri!.AbsolutePath == "/game-data/user-games/player%2Fid",
            "User id was not escaped"
        );
        var query = handler.Uri.Query;
        Require(
            query.Contains("sportId=7")
                && query.Contains("page=2")
                && query.Contains("appointmentsPassed=true")
                && query.Contains("limit=30"),
            "Incorrect query contract"
        );
        Require(handler.Authorization == "Bearer unused", "Missing authorization");
        Require(
            result
                is [
                    {
                        Id: "42",
                        SportId: 7,
                        Price: 125,
                        TotalPrice: 500,
                        Terrain: "parquet",
                        Venue: "indoor",
                        Organizer: "Ana Test"
                    },
                ],
            "Response mapping changed"
        );
        handler.Body = """
            {"appointment":{"appointment_id":"details","appointment_address":"Court","appointment_date":"2026-10-20T00:00:00Z",
            "start_time":"10:00","end_time":"11:00","sport_id":7,"players_needed":1,"players_total":4,"price":500,
            "owner_firstname":"Ana","owner_lastname":"Test","organizer_id":12,"owner_profile_img_url":"owner.jpg","owner_phone_number":"fixture-phone"},
            "players":[{"user_id":34,"firstname":"Mila","lastname":"Test","acceptance_status":"ACCEPTED","profile_img_url":"player.jpg","phone_number":null}]}
            """;
        var details = await api.DetailsAsync("details", default);
        Require(
            details.Game.OrganizerId == 12
                && details.Game.OrganizerPhoto == "owner.jpg"
                && details.Game.OrganizerPhone == "fixture-phone",
            "Organizer metadata was dropped"
        );
        Require(
            details.Participants
                is [
                    {
                        Id: 34,
                        Name: "Mila Test",
                        Status: "ACCEPTED",
                        Photo: "player.jpg",
                        Phone: null
                    },
                ],
            "Participant identity/status/avatar mapping was dropped"
        );
        handler.Body = "[]";
        Require(
            (await api.MyGamesAsync(FakeGames.Profile, 1, 1, false, default)).Count == 0,
            "Empty result failed"
        );
        Require(
            handler.Uri!.Query.Contains("appointmentsPassed=false"),
            "Upcoming flag was omitted"
        );
        Require(new CatalogState() is { Sport: 0, MySport: 0 }, "All sports must be the default");
        handler.Response = request =>
        {
            var parameters = request
                .RequestUri!.Query.TrimStart('?')
                .Split('&')
                .Select(pair => pair.Split('='))
                .ToDictionary(pair => pair[0], pair => pair[1]);
            var sport = int.Parse(parameters["sportId"]);
            var page = int.Parse(parameters["page"]);
            Require(sport == 0, "All sports must use one backend query");
            var rows = Enumerable
                .Range(0, 80)
                .Select(index => (Index: index, Rank: index))
                .Skip((page - 1) * 30)
                .Take(30)
                .ToArray();
            return System.Text.Json.JsonSerializer.Serialize(
                rows.Select(row => new
                {
                    appointment_id = $"game-{row.Rank}",
                    appointment_address = "Court",
                    appointment_date = DateTimeOffset.UnixEpoch.AddDays(100 - row.Rank),
                    start_time = "10:00",
                    end_time = "11:00",
                    sport_id = sport,
                    players_needed = 1,
                    players_total = 4,
                    price_per_user = 125,
                    owner_firstname = "Ana",
                    owner_lastname = "Test",
                })
            );
        };
        var first = await api.MyGamesAsync(FakeGames.Profile, 0, 1, false, default);
        var second = await api.MyGamesAsync(FakeGames.Profile, 0, 2, false, default);
        var last = await api.MyGamesAsync(FakeGames.Profile, 0, 3, true, default);
        Require(
            first
                .Select(game => game.Id)
                .SequenceEqual(Enumerable.Range(0, 30).Select(i => $"game-{i}")),
            "First all-sports page is not globally ordered"
        );
        Require(
            second
                .Select(game => game.Id)
                .SequenceEqual(Enumerable.Range(30, 30).Select(i => $"game-{i}")),
            "Second all-sports page skipped appointments"
        );
        Require(last.Count == 20 && last[0].Id == "game-60", "Last page is incomplete");
        handler.Status = HttpStatusCode.Forbidden;
        try
        {
            await api.MyGamesAsync(FakeGames.Profile, 0, 1, false, default);
            throw new Exception("HTTP failure was treated as an empty list");
        }
        catch (InvalidOperationException) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Uri? Uri;
        public string? Authorization;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Func<HttpRequestMessage, string>? Response;
        public string Body = """
            [{"appointment_id":42,"appointment_address":"Court","appointment_date":"2026-10-20T00:00:00Z",
            "start_time":"10:00","end_time":"11:00","sport_id":7,"players_needed":1,"players_total":4,
            "price":500,"terrain_type":"parquet","venue_type":"indoor","price_per_user":125,"currency":"RSD","owner_firstname":"Ana","owner_lastname":"Test"}]
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(
                new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Response?.Invoke(request) ?? Body),
                }
            );
        }
    }
}
