using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DotNative;
using FaliNamIgrac;

var dispatcher = new Dispatcher();
var today = new DateOnly(2030, 1, 1);
var valid = new GameEditorDraft
{
    SportId = 1,
    Address = "Court",
    Latitude = 44.8,
    Longitude = 20.4,
    Date = today.AddDays(1),
    Start = new(18, 30),
    End = new(19, 30),
    Needed = "2",
    Total = "10",
    Price = "1234.50",
};
void Check(bool ok, string message)
{
    if (!ok)
        throw new Exception(message);
}
Check(valid.Validate(today).Count == 0, "valid draft");
foreach (
    var draft in new[]
    {
        valid with
        {
            Needed = "11",
        },
        valid with
        {
            Needed = "0",
        },
        valid with
        {
            Total = "-1",
        },
        valid with
        {
            Total = "abc",
        },
    }
)
    Check(draft.Validate(today).Contains(GameEditorIssue.Capacity), "capacity validation");
foreach (
    var draft in new[]
    {
        valid with
        {
            End = valid.Start,
        },
        valid with
        {
            End = new(0, 30),
        },
        valid with
        {
            Start = null,
        },
    }
)
    Check(draft.Validate(today).Contains(GameEditorIssue.Time), "time validation");
Check(
    (valid with { Date = today.AddDays(-1) }).Validate(today).Contains(GameEditorIssue.Date),
    "past date"
);
foreach (var price in new[] { "-1", "NaN", "1e3", "", "1.2.3" })
    Check(
        (valid with { Price = price }).Validate(today).Contains(GameEditorIssue.Price),
        "price validation"
    );
Check((valid with { Price = "0" }).Validate(today).Count == 0, "free game");
Check(
    valid.ToUpdate().Price == 1234.50m
        && valid.ToUpdate().AppointmentDate.Kind == DateTimeKind.Unspecified,
    "total price and local date semantics"
);
using var handler = new Handler();
using var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/") };
var session = new Session();
var signed = false;
var api = new GameEditorApi(
    http,
    session,
    (request, token) =>
    {
        signed = request.RequestUri!.IsAbsoluteUri;
        request.Headers.Authorization = new("AWS4-HMAC-SHA256", "test-signature");
        return Task.CompletedTask;
    }
);
handler.Body = "{\"appointmentId\":\"created-id\"}";
Check(
    dispatcher.Finish(api.CreateAsync(new(valid.ToUpdate(), 4, 1), default)) == "created-id",
    "create id"
);
Check(
    handler.Path == "/game-data/create-game"
        && signed
        && handler.Auth == "AWS4-HMAC-SHA256 test-signature",
    "create route/auth"
);
using (var json = JsonDocument.Parse(handler.Payload!))
{
    Check(
        json.RootElement.GetProperty("price").GetDecimal() == 1234.50m,
        "no arbitrary price conversion"
    );
    Check(json.RootElement.GetProperty("startTime").GetString() == "18:30", "HH:mm");
    Check(
        !json.RootElement.GetProperty("appointmentDate").GetString()!.EndsWith('Z'),
        "no UTC shift"
    );
}
var full = valid with
{
    Terrain = "parquet",
    Venue = "outdoor",
    MinSkill = 2,
    MaxSkill = 4,
    MinAge = 21,
    MaxAge = 55,
    Description = "keep",
};
dispatcher.Finish(api.UpdateAsync("game/id", full.ToUpdate(), default));
using (var json = JsonDocument.Parse(handler.Payload!))
{
    Check(
        !json.RootElement.TryGetProperty("sportId", out _)
            && !json.RootElement.TryGetProperty("organizerId", out _),
        "update exclusions"
    );
    Check(
        json.RootElement.GetProperty("minAge").GetInt32() == 21
            && json.RootElement.GetProperty("additionalInfo").GetString() == "keep",
        "preserve hidden fields"
    );
}
foreach (var status in new[] { 401, 403, 400, 422, 500, 503 })
{
    handler.Status = status;
    try
    {
        dispatcher.Finish(api.UpdateAsync("id", full.ToUpdate(), default));
        throw new Exception("false HTTP success");
    }
    catch (GameEditorException e)
    {
        Check(e.Status == status, "status classification");
    }
}
handler.Status = 201;
handler.Body = "{}";
try
{
    dispatcher.Finish(api.CreateAsync(new(valid.ToUpdate(), 4, 1), default));
    throw new Exception("missing id accepted");
}
catch (GameEditorException e)
{
    Check(e.Issue == GameEditorIssue.InvalidResponse, "invalid success body");
}

string? epoch = "account:1";
var fake = new FakeApi();
var places = new Places();
GameEditorNotifier Editor(string? id = null) =>
    new(dispatcher, fake, places, () => epoch, 4, id, () => today);
using var editor = Editor();
editor.Change(_ => valid);
fake.Pending = new();
var first = editor.SaveAsync();
Check(
    editor.Busy && dispatcher.Finish(editor.SaveAsync()) is null && fake.Calls == 1,
    "double submit"
);
fake.Pending.SetException(new GameEditorException(GameEditorIssue.Server, 500));
Check(
    dispatcher.Finish(first) is null
        && editor.State.Draft == valid
        && editor.State.Error == GameEditorIssue.Server,
    "retain on failure"
);
fake.Pending = null;
Check(dispatcher.Finish(editor.SaveAsync()) is not null, "retry success");
Check(dispatcher.Finish(editor.SaveAsync()) is null, "no resubmit after success");
using var canceled = Editor();
canceled.Change(_ => valid);
fake.Pending = new();
using var cancel = new CancellationTokenSource();
var saving = canceled.SaveAsync(cancel.Token);
cancel.Cancel();
fake.Pending.SetResult("late");
Check(
    dispatcher.Finish(saving) is null && !canceled.State.Saved && canceled.State.Draft == valid,
    "canceled late response"
);
using var stale = Editor();
stale.Change(_ => valid);
fake.Pending = new();
var old = stale.SaveAsync();
epoch = "account:2";
fake.Pending.SetResult("late");
Check(dispatcher.Finish(old) is null && !stale.State.Saved, "session generation");
fake.Pending = null;
using var edit = Editor("edit-id");
dispatcher.Finish(edit.LoadAsync());
Check(
    edit.State.Loaded && edit.State.Draft.Price == "1000" && edit.State.Draft.MinAge == 21,
    "full edit prefill"
);
edit.Change(d => d with { Description = "new", SportId = 2 });
Check(edit.State.Draft.SportId == 1, "immutable edit sport");
dispatcher.Finish(edit.SaveAsync());
Check(
    fake.Updated is { MinAge: 21, TerrainType: "grass", Price: 1000, AdditionalInfo: "new" },
    "edit retained fields"
);
using var location = Editor();
location.Change(_ => valid);
places.Pending = new();
var search = location.SearchAsync("first");
places.Pending = null;
dispatcher.Finish(location.SearchAsync("second"));
places.First!.SetResult([new("stale", "stale")]);
dispatcher.Finish(search);
Check(
    location.State.Suggestions[0].Id == "second" && location.State.Draft.Latitude is null,
    "stale search/invalidate coords"
);
dispatcher.Finish(location.SelectPlaceAsync(new("second", "second")));
Check(location.State.Draft.Latitude == 44.8, "place details selected");

// Retry all HTTP classes at the notifier boundary and preserve the exact raw draft.
foreach (
    var issue in new[]
    {
        GameEditorIssue.Unauthorized,
        GameEditorIssue.Rejected,
        GameEditorIssue.Server,
    }
)
{
    using var failed = Editor();
    failed.Change(_ => valid);
    fake.Pending = new();
    var task = failed.SaveAsync();
    fake.Pending.SetException(new GameEditorException(issue));
    Check(
        dispatcher.Finish(task) is null
            && failed.State.Error == issue
            && failed.State.Draft == valid,
        "notifier failure retained"
    );
    fake.Pending = null;
    Check(dispatcher.Finish(failed.SaveAsync()) is not null, "notifier retry");
}
using var closed = Editor();
closed.Change(_ => valid);
fake.Pending = new();
var closing = closed.SaveAsync();
closed.Dispose();
fake.Pending.SetResult("late");
Check(dispatcher.Finish(closing) is null && !closed.State.Saved, "close suppresses success");
fake.Pending = null;
handler.Status = 200;
handler.Body = "{\"appointment\":{}}";
try
{
    dispatcher.Finish(api.LoadAsync("id", default));
    throw new Exception("partial details accepted");
}
catch (JsonException) { }
using var invalid = Editor();
invalid.Change(d => d with { Total = "bad" });
var calls = fake.Calls;
Check(
    dispatcher.Finish(invalid.SaveAsync()) is null
        && fake.Calls == calls
        && invalid.State.Validation.Count > 0,
    "invalid form does not send"
);
fake.FailLoad = true;
using var loadRetry = Editor("edit-id");
dispatcher.Finish(loadRetry.LoadAsync());
Check(!loadRetry.State.Loaded && loadRetry.State.Error == GameEditorIssue.Server, "load error");
fake.FailLoad = false;
dispatcher.Finish(loadRetry.LoadAsync());
Check(loadRetry.State.Loaded && loadRetry.State.Error is null, "load retry");
handler.Body = JsonSerializer.Serialize(
    new GameEditorDetails(dispatcher.Finish(fake.LoadAsync("api-id", default))),
    GameEditorJson.Default.GameEditorDetails
);
var loaded = dispatcher.Finish(api.LoadAsync("api-id", default));
Check(
    loaded.ToDraft().Price == "1000"
        && handler.Auth == "Bearer token"
        && handler.Path == "/game-data/games/api-id",
    "full API detail contract"
);
places.Fail = true;
dispatcher.Finish(location.SearchAsync("retry query"));
Check(
    location.State.PlacesError == GameEditorIssue.Network
        && location.State.Draft.Address == "retry query",
    "Places retains query on failure"
);
places.Fail = false;
dispatcher.Finish(location.RetryPlacesAsync());
Check(
    location.State.Suggestions[0].Id == "retry query" && !location.State.PlacesBusy,
    "Places retry"
);
Console.WriteLine(
    "PASS Feature01: validation, contracts, HTTP errors, retry, edit preservation, double-submit, cancellation, stale session and Places."
);

sealed class Dispatcher : IUiDispatcher
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void Finish(Task task)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < limit)
        {
            while (queue.TryDequeue(out var a))
                a();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException();
        task.GetAwaiter().GetResult();
        while (queue.TryDequeue(out var a))
            a();
    }

    public T Finish<T>(Task<T> task)
    {
        Finish((Task)task);
        return task.Result;
    }
}

sealed class Handler : HttpMessageHandler
{
    public int Status = 200;
    public string Body = "{}";
    public string? Payload,
        Path,
        Auth;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken token
    )
    {
        Path = request.RequestUri!.AbsolutePath;
        Auth = request.Headers.Authorization?.ToString();
        Payload = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        return new((HttpStatusCode)Status) { Content = new StringContent(Body) };
    }
}

sealed class FakeApi : IGameEditorApi
{
    public int Calls;
    public bool FailLoad;
    public TaskCompletionSource<string>? Pending;
    public GameEditorUpdate? Updated;

    public Task<string> CreateAsync(GameEditorCreate request, CancellationToken token)
    {
        Calls++;
        return Pending?.Task ?? Task.FromResult("saved");
    }

    public Task UpdateAsync(string id, GameEditorUpdate request, CancellationToken token)
    {
        Updated = request;
        return Task.CompletedTask;
    }

    public Task<GameEditorAppointment> LoadAsync(string id, CancellationToken token) =>
        FailLoad
            ? Task.FromException<GameEditorAppointment>(
                new GameEditorException(GameEditorIssue.Server)
            )
            : Task.FromResult(
                new GameEditorAppointment
                {
                    Id = id,
                    Sport = 1,
                    Organizer = 4,
                    Address = "Court",
                    Date = new(2030, 1, 2),
                    Start = "18:30:00",
                    End = "19:30:00",
                    Lat = 44.8,
                    Lng = 20.4,
                    Needed = 2,
                    Total = 10,
                    Price = 1000,
                    Currency = "RSD",
                    Terrain = "grass",
                    Venue = "outdoor",
                    Info = "old",
                    MinSkill = 2,
                    MaxSkill = 4,
                    MinAge = 21,
                    MaxAge = 55,
                }
            );
}

sealed class Places : IPlacesService
{
    public bool Fail;
    public TaskCompletionSource<IReadOnlyList<PlaceSuggestion>>? Pending,
        First;

    public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string text, CancellationToken token)
    {
        if (Fail)
            return Task.FromException<IReadOnlyList<PlaceSuggestion>>(new HttpRequestException());
        if (Pending is not null)
        {
            First = Pending;
            return Pending.Task;
        }
        return Task.FromResult<IReadOnlyList<PlaceSuggestion>>([new(text, text)]);
    }

    public Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token) =>
        Task.FromResult(new SelectedPlace(place.Description, 44.8, 20.4));
}

sealed class Session : ISessionService
{
    public bool IsSignedIn => true;
    public string Email => "test@example.invalid";

    public Task<string> AccessTokenAsync(CancellationToken token) => Task.FromResult("token");

    public void Dispose() { }

    public Task SignInAsync(string e, string p, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<AccountInfo> AcceptWebSessionAsync(
        string a,
        string r,
        int e,
        string i,
        CancellationToken t
    ) => throw new NotSupportedException();

    public Task SignUpAsync(string e, string p, CancellationToken t) =>
        throw new NotSupportedException();

    public Task ConfirmSignUpAsync(string e, string c, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken t) =>
        throw new NotSupportedException();

    public Task RequestPasswordResetAsync(string e, CancellationToken t) =>
        throw new NotSupportedException();

    public Task ConfirmPasswordResetAsync(string e, string c, string p, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<bool> RestoreAsync(CancellationToken t) => throw new NotSupportedException();

    public Task<string> IdTokenAsync(CancellationToken t) => throw new NotSupportedException();

    public Task SignOutAsync(CancellationToken t) => throw new NotSupportedException();
}
