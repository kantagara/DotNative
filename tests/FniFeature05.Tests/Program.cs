using FaliNamIgrac;

await ChatTransportTests.RunAsync();
await FeatureServiceTests.RunAsync();
var ui = new Dispatcher();
var games = new FakeGames();
var session = new SessionNotifier(ui, new FakeSession(), new FakeRegistration(), games);
games.CurrentProfile = FakeGames.Profile;
ui.Finish(session.SignInAsync("fixture@example.com", "fixture", default));
var api = new FakeChat();
var cache = new MemoryCache();
var chat = new ChatNotifier(ui, session, api, new PresentationState(), cache);
ui.Finish(chat.RefreshAsync(default));
chat.SearchUsers();
ui.Finish(chat.StartAsync(new ChatUser("other", "Other", "Player", null, null), default));
Check(
    chat.State.Room?.Id == "room" && api.StartCalls == 0,
    "Existing direct chat must open without a create/index mutation"
);
chat.Back();
ui.Finish(chat.OpenAsync(FakeChat.Room, default));
chat.SetDraft("first");
ChatMessage? accepted = null;
int sends = 0;
api.Send = message =>
{
    sends++;
    accepted = message;
    return Task.FromException<ChatMessage>(new HttpRequestException());
};
ui.Finish(chat.SendAsync(default));
ui.Finish(chat.FlushAsync());
Check(
    cache.Snapshot!.Pending["room"].Id == accepted!.Id,
    "Pending ID must be durable before sending"
);
chat.Clear();
var restarted = new ChatNotifier(ui, session, api, new PresentationState(), cache);
api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([accepted!]);
ui.Finish(restarted.OpenAsync(FakeChat.Room, default));
restarted.SetDraft("second");
ui.Finish(restarted.SendAsync(default));
Check(
    sends == 1 && restarted.State.Draft == "second",
    "Restart retry must reconcile old ID and preserve edited draft"
);
Check(restarted.State.Messages!.Count == 1, "Reconciliation deduplicates");
var blocked = new TaskCompletionSource<ChatMessage>();
ChatMessage? outgoing = null;
api.Send = message =>
{
    sends++;
    outgoing = message;
    return blocked.Task;
};
var inFlight = restarted.SendAsync(default);
ui.Finish(restarted.FlushAsync());

// Let the continuation reach the stub transport before testing the second tap.
var deadline = DateTime.UtcNow.AddSeconds(5);
while (outgoing is null && DateTime.UtcNow < deadline)
{
    ui.Drain();
    Thread.Sleep(1);
}
Check(outgoing is not null, "Send must reach stub");
Check(
    restarted.State.Messages!.Any(item => item.Id == outgoing!.Id) && restarted.State.Draft == "",
    "Message appears and composer clears before acknowledgement"
);
var effects = new TaskCompletionSource();
var effectsStarted = false;
api.CompleteSend = () =>
{
    effectsStarted = true;
    return effects.Task;
};
ui.Finish(restarted.SendAsync(default));
Check(sends == 2, "Double tap must not send twice");
blocked.SetResult(outgoing!);
deadline = DateTime.UtcNow.AddSeconds(5);
while (!effectsStarted && DateTime.UtcNow < deadline)
{
    ui.Drain();
    Thread.Sleep(1);
}
Check(
    effectsStarted && !restarted.State.Sending,
    "Notification work must not keep the send button busy"
);
effects.SetResult();
ui.Finish(inFlight);
api.CompleteSend = () => Task.CompletedTask;
api.Messages = () =>
    Task.FromResult<IReadOnlyList<ChatMessage>>(
        Enumerable
            .Range(0, 620)
            .Select(i => new ChatMessage(
                i.ToString(),
                "room",
                "other",
                "history",
                DateTimeOffset.UnixEpoch.AddSeconds(i),
                []
            ))
            .ToArray()
    );
ui.Finish(restarted.RefreshAsync(default));
ui.Finish(restarted.FlushAsync());
Check(
    restarted.State.HistoryComplete && restarted.State.Messages!.Count >= 620,
    "Full history remains available in memory"
);
Check(cache.Snapshot!.Messages["room"].Length == 500, "Disk snapshot is bounded at 500");
restarted.SetDraft("late member send");
var memberSend = new TaskCompletionSource<ChatMessage>();
ChatMessage? memberMessage = null;
api.Send = message =>
{
    memberMessage = message;
    return memberSend.Task;
};
var pendingMemberSend = restarted.SendAsync(default);
ui.Finish(restarted.FlushAsync());
deadline = DateTime.UtcNow.AddSeconds(5);
while (memberMessage is null && DateTime.UtcNow < deadline)
{
    ui.Drain();
    Thread.Sleep(1);
}
Check(memberMessage is not null, "Membership send must reach stub");
api.Member = false;
ui.Finish(restarted.RefreshAsync(default));
memberSend.SetResult(memberMessage!);
ui.Finish(pendingMemberSend);
ui.Finish(restarted.FlushAsync());
Check(
    !cache.Snapshot!.Messages.ContainsKey("room"),
    "Late send must not restore revoked room cache"
);
Check(
    restarted.State.Room is null
        && restarted.State.Messages is null
        && restarted.State.Error.Length > 0,
    "Membership removal must purge visible history and expose failure"
);
api.Member = true;
api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
ui.Finish(restarted.OpenByIdAsync("room", default));
Check(
    restarted.State.Room?.Id == "room" && restarted.State.HistoryComplete,
    "Push ID must resolve and await history"
);
var late = new TaskCompletionSource<IReadOnlyList<ChatMessage>>();
api.Messages = () => late.Task;
var refresh = restarted.RefreshAsync(default);
restarted.Clear();
late.SetResult([accepted!]);
ui.Finish(refresh);
Check(
    restarted.State.Room is null && restarted.State.Messages is null,
    "Late response must not repopulate cleared account"
);
var brokenCache = new MemoryCache { FailWrites = true };
var blockedChat = new ChatNotifier(ui, session, api, new PresentationState(), brokenCache);
api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
ui.Finish(blockedChat.OpenAsync(FakeChat.Room, default));
blockedChat.SetDraft("must not send");
var forbiddenSends = 0;
api.Send = message =>
{
    forbiddenSends++;
    return Task.FromResult(message);
};
ui.Finish(blockedChat.SendAsync(default));
Check(
    forbiddenSends == 0 && blockedChat.State.CacheError.Length > 0,
    "Failed durable pending write must block network send"
);
var reconnectApi = new FakeChat { WatchFailures = 1 };
int retries = 0;
var reconnectChat = new ChatNotifier(
    ui,
    session,
    reconnectApi,
    new PresentationState(),
    reconnectDelay: (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        retries++;
        return Task.CompletedTask;
    }
);
using var active = new CancellationTokenSource();
reconnectChat.Activate(active.Token);
ui.Drain();
Check(
    retries == 1 && reconnectApi.WatchCalls == 2 && reconnectChat.State.Connected,
    "Reconnect must resubscribe and reconcile after a transport failure"
);
reconnectChat.Deactivate();
Check(!reconnectChat.State.Connected, "Deactivate must clear connection status");
Console.WriteLine(
    "PASS: stub HTTP/websocket transport, durable restart/retry, changed draft, deduplication, 620-message history, stale response"
);

static void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}

sealed class MemoryCache : IChatCache
{
    public ChatSnapshot? Snapshot;
    public bool FailWrites;

    public Task<ChatSnapshot?> ReadAsync(string userId, CancellationToken token) =>
        Task.FromResult(Snapshot);

    public Task WriteAsync(string userId, ChatSnapshot snapshot, CancellationToken token)
    {
        if (FailWrites)
            throw new IOException();
        Snapshot = snapshot;
        return Task.CompletedTask;
    }
}
