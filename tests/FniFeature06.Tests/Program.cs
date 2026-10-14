using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DotNative;
using FaliNamIgrac;

var tests = new (string Name, Action Run)[]
{
    ("backend routes, JSON, credentials, status and cancellation", Transport),
    ("token registration dedupe, refresh, revoke, logout and account switch", TokenLifecycle),
    ("permission denial, unsupported and retry", PermissionAndRetry),
    ("late register response cannot restore logged-out state", StaleToken),
    ("failed delete retains retryable cleanup", CleanupRetry),
    ("concurrent retries share even a failing registration", ConcurrentFailure),
    ("payload validation and normalization", Payload),
    ("cold start waits for session/router and dedupes taps", Navigation),
    ("navigation failure retries and denied access fails closed", NavigationRetry),
    ("late navigation and callbacks cannot cross accounts", StaleNavigation),
    ("foreground chat policy matches Flutter", Foreground),
    ("resolver checks membership, current account and rating eligibility", AccessResolver),
    ("router unmount and caller cancellation keep intent for retry", RouterAndCancellation),
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS " + test.Name);
}
Console.WriteLine($"Passed {tests.Length} notification test groups.");

static void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}
static void Transport()
{
    using var handler = new HttpHandler();
    using var http = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://example.invalid/api/"),
    };
    var api = new DeviceTokenApi(http);
    var account = new NotificationAccount(7, "account-seven");
    api.RegisterAsync(account, "device-token", default).GetAwaiter().GetResult();
    api.DeleteAsync(account, "device-token", default).GetAwaiter().GetResult();
    Check(
        handler.Paths.SequenceEqual(
            new[] { "/api/device-tokens/register-token", "/api/device-tokens/delete-token" }
        ),
        "wrong endpoints"
    );
    Check(
        handler.Auth == "Bearer account-seven" && handler.Method == HttpMethod.Post,
        "wrong auth/method"
    );
    using var json = JsonDocument.Parse(handler.Body!);
    Check(
        json.RootElement.GetProperty("user_id").GetInt32() == 7
            && json.RootElement.GetProperty("token").GetString() == "device-token",
        "wrong JSON contract"
    );
    handler.Status = HttpStatusCode.Forbidden;
    try
    {
        api.RegisterAsync(account, "device-token", default).GetAwaiter().GetResult();
        throw new Exception("HTTP error swallowed");
    }
    catch (HttpRequestException e)
    {
        Check(e.StatusCode == HttpStatusCode.Forbidden, "status lost");
    }
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        api.RegisterAsync(account, "device-token", cts.Token).GetAwaiter().GetResult();
        throw new Exception("cancel ignored");
    }
    catch (OperationCanceledException) { }
}
static void TokenLifecycle()
{
    var ui = new Dispatcher();
    var device = new Device();
    var api = new TokenApi();
    using var notifier = new DeviceTokenNotifier(ui, device, api);
    ui.Finish(notifier.SetAccountAsync(new(1, "one")));
    ui.Finish(Task.WhenAll(notifier.RetryAsync(), notifier.RetryAsync()));
    Check(api.Calls.SequenceEqual(new[] { "register:1:first" }), "duplicate registration");
    ui.Finish(notifier.TokenRefreshedAsync("second"));
    Check(
        api.Calls.SequenceEqual(
            new[] { "register:1:first", "delete:1:first", "register:1:second" }
        ),
        "rotation order wrong"
    );
    device.Value = "second";
    ui.Finish(notifier.SetAccountAsync(new(2, "two")));
    Check(
        api.Calls.TakeLast(2).SequenceEqual(new[] { "delete:1:second", "register:2:second" }),
        "account binding wrong"
    );
    device.Permission = NotificationPermission.Denied;
    ui.Finish(notifier.ResumeAsync());
    Check(
        api.Calls.Last() == "delete:2:second"
            && notifier.State.Status == DeviceTokenStatus.PermissionDenied,
        "revocation retained token"
    );
    ui.Finish(notifier.SetAccountAsync(null));
    Check(notifier.State.Status == DeviceTokenStatus.SignedOut, "logout not complete");
}
static void PermissionAndRetry()
{
    var ui = new Dispatcher();
    var device = new Device { Permission = NotificationPermission.Denied };
    var api = new TokenApi();
    using var notifier = new DeviceTokenNotifier(ui, device, api);
    ui.Finish(notifier.SetAccountAsync(new(1, "one")));
    Check(
        api.Calls.Count == 0
            && device.TokenReads == 0
            && notifier.State.Status == DeviceTokenStatus.PermissionDenied,
        "denied still registered"
    );
    device.Permission = NotificationPermission.Unsupported;
    ui.Finish(notifier.RetryAsync());
    Check(notifier.State.Status == DeviceTokenStatus.Unsupported, "unsupported reported success");
    device.Permission = NotificationPermission.Authorized;
    api.FailRegister = true;
    ui.Finish(notifier.RetryAsync());
    Check(notifier.State.Status == DeviceTokenStatus.Error, "failure reported success");
    api.FailRegister = false;
    ui.Finish(notifier.RetryAsync());
    Check(notifier.State.Status == DeviceTokenStatus.Registered, "retry failed");
    var count = api.Calls.Count;
    ui.Finish(notifier.RetryAsync());
    Check(api.Calls.Count == count, "successful retry repeated register");
}
static void StaleToken()
{
    var ui = new Dispatcher();
    var api = new TokenApi { HoldRegister = new() };
    using var notifier = new DeviceTokenNotifier(ui, new Device(), api);
    var first = notifier.SetAccountAsync(new(1, "one"));
    Check(notifier.State.Status == DeviceTokenStatus.Loading, "missing loading");
    var logout = notifier.SetAccountAsync(null);
    api.HoldRegister.SetResult();
    ui.Finish(Task.WhenAll(first, logout));
    Check(
        notifier.State.Status == DeviceTokenStatus.SignedOut
            && api.Calls.Last() == "delete:1:first",
        "stale register resurrected account or missed cleanup"
    );
}
static void ConcurrentFailure()
{
    var ui = new Dispatcher();
    var api = new TokenApi { HoldRegister = new(), FailRegister = true };
    using var notifier = new DeviceTokenNotifier(ui, new Device(), api);
    var first = notifier.SetAccountAsync(new(1, "one"));
    var second = notifier.RetryAsync();
    api.HoldRegister.SetResult();
    ui.Finish(Task.WhenAll(first, second));
    Check(
        api.Calls.Count == 1 && notifier.State.Status == DeviceTokenStatus.Error,
        "concurrent failure duplicated registration"
    );
    api.FailRegister = false;
    api.HoldRegister = null;
    ui.Finish(notifier.RetryAsync());
    Check(
        api.Calls.Count == 2 && notifier.State.Status == DeviceTokenStatus.Registered,
        "explicit retry not sent"
    );
}
static void CleanupRetry()
{
    var ui = new Dispatcher();
    var api = new TokenApi();
    using var notifier = new DeviceTokenNotifier(ui, new Device(), api);
    ui.Finish(notifier.SetAccountAsync(new(1, "one")));
    api.FailDelete = true;
    ui.Finish(notifier.SetAccountAsync(null));
    Check(
        notifier.State.Status == DeviceTokenStatus.Error && notifier.State.CleanupPending,
        "delete failure hidden"
    );
    api.FailDelete = false;
    ui.Finish(notifier.RetryAsync());
    Check(
        notifier.State.Status == DeviceTokenStatus.SignedOut && !notifier.State.CleanupPending,
        "cleanup retry failed"
    );
}
static NotificationIntent Intent(string delivery = "push-1")
{
    Check(
        NotificationPayload.TryParse(
            new Dictionary<string, string> { ["type"] = "message", ["chatId"] = "chat-1" },
            delivery,
            out var parsed
        ),
        "valid payload rejected"
    );
    return parsed!;
}
static void Payload()
{
    const string id = "abcdefab-1234-1234-1234-abcdefabcdef";
    Check(
        NotificationPayload.TryParseUri("https://app.falinamigrac.com/game/" + id, out var link)
            && link!.Target == new NotificationTarget(NotificationDestination.GameDetails, id),
        "shared game link rejected"
    );
    foreach (
        var bad in new[]
        {
            "http://app.falinamigrac.com/game/" + id,
            "https://evil.example/game/" + id,
            "https://app.falinamigrac.com:444/game/" + id,
            "https://user@app.falinamigrac.com/game/" + id,
            "https://app.falinamigrac.com/game/not-a-game",
            "https://app.falinamigrac.com/game/" + id + "/extra",
        }
    )
        Check(!NotificationPayload.TryParseUri(bad, out _), "untrusted link accepted");
    var data = new Dictionary<string, string>
    {
        ["arg_type"] = " NEW_APPOINTMENT ",
        ["arg_gameId"] = "ABCDEFAB-1234-1234-1234-ABCDEFABCDEF",
    };
    Check(
        NotificationPayload.TryParse(data, "id", out var result)
            && result!.Target.Id == "abcdefab-1234-1234-1234-abcdefabcdef",
        "normalization failed"
    );
    data["arg_gameId"] = "../../admin";
    Check(!NotificationPayload.TryParse(data, "id", out _), "path accepted");
    foreach (
        var invalid in new[]
        {
            "null",
            "[]",
            "{",
            "{\"type\":\"message\",\"chatId\":5}",
            "{\"type\":\"message\",\"type\":\"message\",\"chatId\":\"chat-1\"}",
            "{\"type\":\"message\",\"chatId\":\"https://evil\"}",
            "{\"type\":\"admin\",\"chatId\":\"chat-1\"}",
        }
    )
        Check(
            !NotificationPayload.TryParseLocal(invalid, out _),
            "malformed payload accepted: " + invalid
        );
    Check(
        NotificationPayload.TryParseLocal(
            "{\"type\":\"rate_players\",\"gameId\":\"abcdefab-1234-1234-1234-abcdefabcdef\"}",
            out var local
        )
            && local!.Target.Destination == NotificationDestination.RatePlayers,
        "local rating unsupported"
    );
    Check(Intent().DeliveryId == "push-1", "transport identity lost");
    Check(
        NotificationPayload.TryParseLocal(
            NotificationPayload.ToLocalPayload(Intent()),
            out var roundtrip
        )
            && roundtrip == Intent(),
        "local notification lost remote delivery identity"
    );
}
static void Navigation()
{
    var ui = new Dispatcher();
    var resolver = new Resolver();
    using var nav = new NotificationNavigationNotifier(ui, resolver);
    Check(nav.Enqueue(Intent(), 0), "cold-start event not queued");
    ui.Finish(nav.DrainAsync());
    Check(resolver.Calls == 0, "navigated signed out");
    nav.SetAccount(1);
    ui.Finish(nav.DrainAsync());
    Check(resolver.Calls == 0, "navigated before router");
    nav.SetRouterReady(true);
    ui.Finish(Task.WhenAll(nav.DrainAsync(), nav.DrainAsync()));
    Check(resolver.Commits == 1 && nav.State.Pending == 0, "queued navigation not committed once");
    Check(!nav.Enqueue(Intent(), nav.Epoch), "duplicate tap accepted");
}
static void NavigationRetry()
{
    var ui = new Dispatcher();
    var resolver = new Resolver { Fail = true };
    using var nav = new NotificationNavigationNotifier(ui, resolver);
    nav.SetAccount(1);
    nav.SetRouterReady(true);
    nav.Enqueue(Intent(), nav.Epoch);
    ui.Finish(nav.DrainAsync());
    Check(
        nav.State.Status == NotificationNavigationStatus.Error && nav.State.Pending == 1,
        "network error consumed intent"
    );
    resolver.Fail = false;
    ui.Finish(nav.DrainAsync());
    Check(resolver.Commits == 1, "navigation retry failed");
    resolver.Deny = true;
    nav.Enqueue(Intent("denied"), nav.Epoch);
    ui.Finish(nav.DrainAsync());
    Check(
        nav.State.Status == NotificationNavigationStatus.AccessDenied && resolver.Commits == 1,
        "access denial bypassed"
    );
}
static void StaleNavigation()
{
    var ui = new Dispatcher();
    var resolver = new Resolver { Hold = new() };
    using var nav = new NotificationNavigationNotifier(ui, resolver);
    nav.SetAccount(1);
    nav.SetRouterReady(true);
    var old = nav.Epoch;
    nav.Enqueue(Intent(), old);
    var task = nav.DrainAsync();
    nav.SetAccount(null);
    nav.SetAccount(2);
    resolver.Hold.SetResult();
    ui.Finish(task);
    Check(resolver.Commits == 0 && nav.State.Pending == 0, "late route crossed account");
    Check(
        !nav.Enqueue(Intent("late"), old) && !nav.Enqueue(Intent(), nav.Epoch),
        "late callback/duplicate crossed account"
    );
    nav.InvalidateSession();
    Check(!nav.Enqueue(Intent("signedout"), nav.Epoch), "signed-out event carried to next login");
}
static void Foreground()
{
    Check(
        NotificationForegroundPolicy.Decide(Intent(), true, "chat-1")
            == ForegroundNotificationAction.Ignore,
        "active chat notification shown"
    );
    Check(
        NotificationForegroundPolicy.Decide(Intent(), true, "other")
            == ForegroundNotificationAction.ChatHandledByRealtime,
        "foreground chat duplicate shown"
    );
    Check(
        NotificationForegroundPolicy.Decide(Intent(), false, null)
            == ForegroundNotificationAction.Ignore,
        "signed-out notification shown"
    );
    var game = new NotificationIntent(
        "game",
        new(NotificationDestination.GameDetails, "abcdefab-1234-1234-1234-abcdefabcdef")
    );
    Check(
        NotificationForegroundPolicy.Decide(game, true, null)
            == ForegroundNotificationAction.ShowLocalNotification,
        "game notification suppressed"
    );
}
static void AccessResolver()
{
    var chats = new Chats();
    var games = new Games();
    int? current = 1;
    int commits = 0;
    var resolver = new AuthenticatedNotificationResolver(
        chats,
        games,
        () => current,
        _ => commits++,
        _ => commits++
    );
    var target = Intent().Target;
    chats.Room = new("chat-1", ["2"], DateTimeOffset.UnixEpoch);
    Check(
        resolver.ResolveAsync(1, target, default).GetAwaiter().GetResult() is null,
        "nonmember granted chat access"
    );
    chats.Room = chats.Room with { Participants = ["1", "2"] };
    var commit = resolver.ResolveAsync(1, target, default).GetAwaiter().GetResult();
    Check(commit is not null, "member denied");
    current = 2;
    commit!();
    Check(commits == 0, "account changed before commit");
    Check(
        resolver.ResolveAsync(1, target, default).GetAwaiter().GetResult() is null,
        "stale account resolved"
    );
    current = 1;
    Check(
        resolver
            .ResolveAsync(1, new(NotificationDestination.RatePlayers, "game"), default)
            .GetAwaiter()
            .GetResult()
            is null,
        "missing rating eligibility adapter allowed access"
    );
    commit = resolver.ResolveAsync(1, target, default).GetAwaiter().GetResult();
    commit!();
    Check(commits == 1, "member route not opened");
}
static void RouterAndCancellation()
{
    var ui = new Dispatcher();
    var resolver = new Resolver { Hold = new() };
    using var nav = new NotificationNavigationNotifier(ui, resolver);
    nav.SetAccount(1);
    nav.SetRouterReady(true);
    nav.Enqueue(Intent(), nav.Epoch);
    var task = nav.DrainAsync();
    nav.SetRouterReady(false);
    resolver.Hold.SetResult();
    ui.Finish(task);
    Check(resolver.Commits == 0 && nav.State.Pending == 1, "unmounted router navigated");
    nav.SetRouterReady(true);
    resolver.Hold = new();
    using var cancel = new CancellationTokenSource();
    task = nav.DrainAsync(cancel.Token);
    cancel.Cancel();
    resolver.Hold.SetResult();
    ui.Finish(task);
    Check(
        resolver.Commits == 0 && nav.State.Status == NotificationNavigationStatus.Error,
        "cancellation committed route"
    );
    resolver.Hold = null;
    ui.Finish(nav.DrainAsync());
    Check(resolver.Commits == 1, "cancelled tap not retryable");
}

sealed class Chats : IChatService
{
    public ChatRoom? Room;

    public Task<ChatRoom?> GetAsync(string id, string user, CancellationToken token) =>
        Task.FromResult(Room);

    public Task<IReadOnlyList<ChatRoom>> ListAsync(string userId, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ChatRoom>>(Room is null ? [] : [Room]);

    public IAsyncEnumerable<ChatUpdate> WatchInboxAsync(string userId, CancellationToken token) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ChatUpdate> WatchAsync(string chatId, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(string chatId, CancellationToken token) =>
        throw new NotSupportedException();

    public Task MarkReadAsync(string userId, string[] ids, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<ChatMessage> SendAsync(ChatMessage message, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ChatUser>> SearchAsync(string text, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<ChatRoom> UpdateGroupAsync(
        ChatRoom room,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    ) => throw new NotSupportedException();

    public Task LeaveGroupAsync(ChatRoom room, string userId, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<ChatRoom> CreateGroupAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    ) => throw new NotSupportedException();

    public Task<ChatRoom> StartAsync(string userId, ChatUser other, CancellationToken token) =>
        throw new NotSupportedException();
}

sealed class Games : IGamesApi
{
    public Task<PlayerProfile> ProfileAsync(CancellationToken token) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Game>> ListAsync(
        PlayerProfile user,
        int sport,
        int page,
        CancellationToken token
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<Game>> MyGamesAsync(
        PlayerProfile user,
        int sport,
        int page,
        bool passed,
        CancellationToken token
    ) => throw new NotSupportedException();

    public Task<GameDetails> DetailsAsync(string id, CancellationToken token) =>
        throw new NotSupportedException();

    public Task ApplyAsync(string id, string userId, CancellationToken token) =>
        throw new NotSupportedException();
}

sealed class Device : INotificationDevice
{
    public NotificationPermission Permission = NotificationPermission.Authorized;
    public string Value = "first";
    public int TokenReads;

    public Task<NotificationPermission> RequestPermissionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Permission);
    }

    public Task<string> GetTokenAsync(CancellationToken token)
    {
        TokenReads++;
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Value);
    }
}

sealed class TokenApi : IDeviceTokenApi
{
    public readonly List<string> Calls = new();
    public bool FailRegister,
        FailDelete;
    public TaskCompletionSource? HoldRegister;

    public async Task RegisterAsync(
        NotificationAccount account,
        string value,
        CancellationToken token
    )
    {
        Calls.Add($"register:{account.UserId}:{value}");
        if (HoldRegister is not null)
            await HoldRegister.Task; // Deliberately ignores cancellation to model late transport completion.
        if (FailRegister)
            throw new HttpRequestException();
    }

    public Task DeleteAsync(NotificationAccount account, string value, CancellationToken token)
    {
        Calls.Add($"delete:{account.UserId}:{value}");
        if (FailDelete)
            throw new HttpRequestException();
        return Task.CompletedTask;
    }
}

sealed class Resolver : INotificationDestinationResolver
{
    public int Calls,
        Commits;
    public bool Fail,
        Deny;
    public TaskCompletionSource? Hold;

    public async Task<Action?> ResolveAsync(
        int user,
        NotificationTarget target,
        CancellationToken token
    )
    {
        Calls++;
        if (Hold is not null)
            await Hold.Task;
        if (Fail)
            throw new HttpRequestException();
        return Deny ? null : () => Commits++;
    }
}

sealed class HttpHandler : HttpMessageHandler
{
    public readonly List<string> Paths = new();
    public string? Auth,
        Body;
    public HttpMethod? Method;
    public HttpStatusCode Status = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        Paths.Add(request.RequestUri!.AbsolutePath);
        Auth = request.Headers.Authorization?.ToString();
        Method = request.Method;
        Body = await request.Content!.ReadAsStringAsync(token);
        return new(Status);
    }
}

sealed class Dispatcher : IUiDispatcher
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void Finish(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var action))
                action();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException("Test task did not complete");
        task.GetAwaiter().GetResult();
        while (queue.TryDequeue(out var action))
            action();
    }
}
