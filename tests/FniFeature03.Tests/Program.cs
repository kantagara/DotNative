using System.Globalization;
using System.Net;
using System.Text.Json;
using DotNative;
using FaliNamIgrac;

var user = new PlayerProfile("12", "Test", 44.81, 20.46, 30);
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
    checks++;
}
void Reject(Action action)
{
    try
    {
        action();
    }
    catch (ArgumentException)
    {
        checks++;
        return;
    }
    throw new Exception("Expected validation failure");
}
Dictionary<string, string> Parse(string path) =>
    path.Split('?')[1]
        .Split('&')
        .Select(x => x.Split('=', 2))
        .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sr-Latn-RS");
var query = new CatalogSearchQuery(
    Filters: new(0, 125.5m, 1.25m),
    Order: CatalogOrder.PriceDescending
);
var fields = Parse(query.Path(user));
Check(fields["distance"] == "1250" && fields["userLat"] == "44.81", "Invariant units");
Check(!fields.ContainsKey("sportId") && fields["page"] == "1", "All sports default");
using (var json = JsonDocument.Parse(fields["filters"]))
{
    Check(json.RootElement.GetProperty("minPrice").GetDecimal() == 0, "Zero price omitted");
    Check(json.RootElement.GetProperty("maxPrice").GetDecimal() == 125.5m, "Decimal price lost");
    Check(json.RootElement.GetProperty("sortByPrice").GetString() == "DESC", "Price direction");
}
foreach (var order in Enum.GetValues<CatalogOrder>())
{
    using var doc = JsonDocument.Parse(Parse((query with { Order = order }).Path(user))["filters"]);
    Check(
        doc.RootElement.TryGetProperty("sortByDistance", out _) == (order == CatalogOrder.Closest),
        "Sort precedence"
    );
    Check(
        doc.RootElement.GetProperty("sortByTime").GetString()
            == (order == CatalogOrder.TimeDescending ? "DESC" : "ASC"),
        "Time direction"
    );
}
Check(
    Parse((query with { Sport = FniSports.All.First().Id, Page = 2 }).Path(user))
        .ContainsKey("sportId"),
    "Exact sport omitted"
);
Reject(() => (query with { Page = 0 }).Validate());
Reject(() => (query with { Page = 10001 }).Validate());
Reject(() => (query with { Limit = 201 }).Validate());
Reject(() => (query with { Sport = -1 }).Validate());
Reject(() => new CatalogFilters(20, 10).Validate());
Reject(() => new CatalogFilters(-1).Validate());
Reject(() => new CatalogFilters(DistanceKm: 201).Validate());
Reject(() => query.Path(user with { Latitude = double.NaN }));
Check(
    new CatalogFilterDraft("", "0", "0").TryApply(out var zero)
        && zero.MaxPrice == 0
        && zero.DistanceKm == 0,
    "Boundaries"
);
Check(new CatalogFilterDraft("1,5", "2.5", "200").TryApply(out _), "Localized decimal");
foreach (var bad in new[] { "-1", "NaN", "Infinity", "1e4", "1,000.5" })
    Check(!new CatalogFilterDraft(bad).TryApply(out _), "Bad input accepted " + bad);
Check(!new CatalogFilterDraft(DistanceKm: " ").TryApply(out _), "Blank radius accepted");

var transport = new Transport();
var api = new CatalogSearchApi(transport);
transport.Reply = path =>
{
    var p = Parse(path);
    Check(p["filters"].Contains("sortByPrice"), "Sort not sent for each page");
    Check(!p.ContainsKey("sportId"), "All-sports search must use one backend request");
    var page = int.Parse(p["page"]);
    return JsonSerializer.Serialize(
        Enumerable.Range(0, 60).Reverse().Skip((page - 1) * 30).Take(30).Select(i => Row(i, 1))
    );
};
var first = await api.SearchAsync(user, query, default);
var second = await api.SearchAsync(user, query with { Page = 2 }, default);
Check(
    first[0].Id == "59" && first[^1].Id == "30" && second[0].Id == "29" && second[^1].Id == "0",
    "Backend page order altered"
);
Check(transport.Authenticated, "Authentication missing");
transport.Reply = _ => "[]";
Check((await api.SearchAsync(user, query, default)).Count == 0, "Empty result");
transport.Status = HttpStatusCode.Forbidden;
try
{
    await api.SearchAsync(user, query, default);
    throw new Exception("HTTP failure swallowed");
}
catch (HttpRequestException)
{
    checks++;
}
transport.Status = HttpStatusCode.OK;
transport.Reply = _ => "null";
try
{
    await api.SearchAsync(user, query, default);
    throw new Exception("Null treated as success");
}
catch (System.IO.InvalidDataException)
{
    checks++;
}
var allRequests = 0;
transport.Reply = path =>
{
    allRequests++;
    var p = Parse(path);
    Check(p["sportId"] == "0" && p["limit"] == "30", "All-sports backend contract");
    var page = int.Parse(p["page"]);
    return JsonSerializer.Serialize(
        Enumerable.Range((page - 1) * 30, 30).Select(i => Row(i, i % 2 + 1))
    );
};
first = await api.MyGamesAsync(user);
second = await api.MyGamesAsync(user, page: 2);
Check(allRequests == 2, "Each all-sports page must make exactly one request");
Check(first[0].Id == "0" && second[0].Id == "30", "Server pagination was changed");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try
    {
        await api.MyGamesAsync(user, token: cancelled.Token);
        throw new Exception("Cancellation ignored");
    }
    catch (OperationCanceledException)
    {
        checks++;
    }
}

var delayed = new DelayedApi();
PlayerProfile? current = user;
using var notifier = new CatalogSearchNotifier(new Dispatcher(), delayed, () => current);
notifier.BeginEdit();
notifier.Edit(new("10", "20", "2"));
notifier.CancelEdit();
Check(notifier.State.Query.EffectiveFilters == new CatalogFilters(), "Cancel committed draft");
notifier.BeginEdit();
notifier.Edit(new("20", "10"));
await notifier.ApplyAsync();
Check(
    notifier.State.InvalidDraft && delayed.Requests.Count == 0,
    "Invalid draft requested network"
);
notifier.ResetDraft();
Check(notifier.State.Draft == new CatalogFilterDraft(), "Reset failed");
notifier.Edit(new("10", "20", "2"));
var old = notifier.ApplyAsync();
Check(
    notifier.State.Query.Page == 1 && notifier.State.Query.EffectiveFilters.MinPrice == 10,
    "Apply failed"
);
var duplicate = notifier.RetryAsync();
Check(ReferenceEquals(old, duplicate) && delayed.Requests.Count == 1, "Duplicate request");
notifier.BeginEdit();
notifier.ResetDraft();
notifier.CancelEdit();
Check(
    notifier.State.Query.EffectiveFilters.MinPrice == 10 && notifier.State.Draft.MinPrice == "10",
    "Reset was committed without Apply"
);
var latest = notifier.ChangeAsync(new(Order: CatalogOrder.TimeDescending, Page: 5));
Check(
    delayed.Tokens[0].IsCancellationRequested && notifier.State.Query.Page == 1,
    "Supersession failed"
);
delayed.Requests[1].SetResult(second);
await latest;
delayed.Requests[0].SetResult(first);
await old;
Check(notifier.State.Games == second && !notifier.State.Loading, "Stale result won");
var failed = notifier.RetryAsync();
delayed.Requests[^1].SetException(new HttpRequestException());
await failed;
Check(
    notifier.State.Error is HttpRequestException && notifier.State.Games.Count == 0,
    "Error state missing"
);
var retry = notifier.RetryAsync();
delayed.Requests[^1].SetResult(first);
await retry;
Check(notifier.State.Error is null && notifier.State.HasMore, "Retry failed");
var next = notifier.NextAsync();
Check(notifier.State.Query.Page == 2, "Next page failed");
delayed.Requests[^1].SetResult([]);
await next;
Check(!notifier.State.HasMore, "End not detected");
var stale = notifier.RetryAsync();
notifier.Clear();
current = null;
delayed.Requests[^1].SetResult(first);
await stale;
Check(notifier.State.Games.Count == 0 && notifier.State.Query.Sport == 0, "Logout stale response");
current = user;
using (var cancelled = new CancellationTokenSource())
{
    var pending = notifier.RetryAsync(cancelled.Token);
    cancelled.Cancel();
    delayed.Requests[^1].SetResult(first);
    await pending;
    Check(
        !notifier.State.Loading && notifier.State.Games.Count == 0 && notifier.State.Error is null,
        "Cancellation state"
    );
}
var accountRace = notifier.RetryAsync();
current = user with { Id = "99" };
delayed.Requests[^1].SetResult(first);
await accountRace;
Check(notifier.State.Games.Count == 0, "Account switch accepted stale results");
current = user;
var accountPending = notifier.RetryAsync();
current = user with { Id = "99" };
var switched = notifier.RetryAsync();
Check(delayed.Tokens[^2].IsCancellationRequested, "Account switch deduplicated with old request");
delayed.Requests[^1].SetResult(second);
await switched;
delayed.Requests[^2].SetResult(first);
await accountPending;
Check(notifier.State.Games == second, "Account race result");
current = user;
var oldLogin = notifier.RetryAsync();
notifier.Clear();
current = null;
current = user;
var newLogin = notifier.RetryAsync();
delayed.Requests[^1].SetResult(second);
await newLogin;
delayed.Requests[^2].SetException(new HttpRequestException("old login"));
await oldLogin;
Check(notifier.State.Games == second && notifier.State.Error is null, "Prior login error won");
notifier.BeginEdit();
notifier.ResetDraft();
var reset = notifier.ApplyAsync();
Check(
    notifier.State.Query.EffectiveFilters == new CatalogFilters() && notifier.State.Query.Page == 1,
    "Applied reset failed"
);
delayed.Requests[^1].SetResult([]);
await reset;
Console.WriteLine($"PASS: {checks} filtering assertions");

static object Row(int rank, int sport) =>
    new
    {
        appointment_id = rank.ToString(),
        appointment_address = "Court",
        appointment_date = DateTimeOffset.UnixEpoch.AddDays(1000 - rank),
        start_time = "10:00",
        end_time = "11:00",
        sport_id = sport,
        players_needed = 1,
        players_total = 4,
        price_per_user = rank,
        owner_firstname = "Test",
        owner_lastname = "Player",
    };

sealed class Dispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}

sealed class DelayedApi : ICatalogSearchApi
{
    public List<TaskCompletionSource<IReadOnlyList<Game>>> Requests { get; } = [];
    public List<CancellationToken> Tokens { get; } = [];

    public Task<IReadOnlyList<Game>> SearchAsync(
        PlayerProfile user,
        CatalogSearchQuery query,
        CancellationToken token
    )
    {
        Tokens.Add(token);
        var pending = new TaskCompletionSource<IReadOnlyList<Game>>();
        Requests.Add(pending);
        return pending.Task;
    }
}

sealed class Transport : IApiClient
{
    public Func<string, string> Reply { get; set; } = _ => "[]";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public bool Authenticated;

    public Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    )
    {
        token.ThrowIfCancellationRequested();
        Authenticated = authenticated;
        return Task.FromResult(
            new HttpResponseMessage(Status) { Content = new StringContent(Reply(path)) }
        );
    }

    public Task<HttpResponseMessage> GetResponseAsync<T>(
        string p,
        T q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> GetAsync<T>(
        string p,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<R> GetAsync<Q, R>(
        string p,
        Q q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<HttpResponseMessage> PostJsonAsync<T>(
        string p,
        T q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<HttpResponseMessage> PutJsonAsync<T>(
        string p,
        T q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();
}
