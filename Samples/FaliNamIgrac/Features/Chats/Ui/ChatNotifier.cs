using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed record ChatState(
    IReadOnlyList<ChatRoom>? Rooms = null,
    ChatRoom? Room = null,
    IReadOnlyList<ChatMessage>? Messages = null,
    IReadOnlyList<ChatUser>? Users = null,
    string Draft = "",
    string Search = "",
    bool Searching = false,
    string Error = "",
    string CacheError = "",
    bool Loading = false,
    bool Sending = false,
    bool Connected = false,
    bool CreatingGroup = false,
    string GroupName = "",
    IReadOnlyList<ChatUser>? SelectedUsers = null,
    bool EditingGroup = false,
    byte[]? GroupPhoto = null,
    bool HistoryComplete = false,
    string ReceiptError = "",
    bool Starting = false
);

public sealed class ChatNotifier(
    IUiDispatcher dispatcher,
    SessionNotifier session,
    IChatService api,
    IPresentationState presentation,
    IChatCache? cache = null,
    ChatFeatureText? text = null,
    Func<TimeSpan, CancellationToken, Task>? reconnectDelay = null
) : Notifier<ChatState>(dispatcher, new())
{
    public ChatFeatureText Text { get; set; } = text ?? new();
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private int revision,
        sessionRevision,
        loadRevision;
    private bool refreshRequested;
    private bool restored;
    private Task? restoring;
    private Task persistence = Task.CompletedTask;
    private readonly Dictionary<string, (ChatSnapshot Snapshot, int Generation)> queuedWrites =
        new();
    private bool cacheWriteFailed,
        writing;
    private readonly Dictionary<string, ChatMessage[]> histories = new();
    private TaskCompletionSource? flushSignal;

    public Task FlushAsync()
    {
        flushSignal?.TrySetResult();
        return persistence;
    }

    private async Task RestoreAsync(string user, int generation, CancellationToken token)
    {
        if (restored || cache is null)
            return;
        if (restoring is not null)
        {
            await restoring;
            return;
        }
        var task = RestoreCore();
        restoring = task;
        try
        {
            await task;
        }
        finally
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(restoring, task))
                    restoring = null;
            });
        }
        async Task RestoreCore()
        {
            try
            {
                await persistence; // Finish writes from a previous session before hydrating this account.
                var saved = await cache.ReadAsync(user, token);
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (generation != sessionRevision || token.IsCancellationRequested)
                        return;
                    restored = true;
                    if (saved is null)
                        return;
                    foreach (var item in saved.Drafts)
                        drafts.TryAdd(item.Key, item.Value);
                    foreach (var item in saved.Pending)
                        pending.TryAdd(item.Key, item.Value);
                    foreach (var item in saved.Messages)
                        histories.TryAdd(item.Key, item.Value);
                    var room = State.Room;
                    SetState(
                        State with
                        {
                            Rooms = State.Rooms ?? saved.Rooms,
                            Messages = room is null
                                ? State.Messages
                                : Merge(
                                    histories.GetValueOrDefault(room.Id, []),
                                    State.Messages ?? []
                                ),
                            Draft = room is null
                                ? State.Draft
                                : drafts.GetValueOrDefault(room.Id, State.Draft),
                        }
                    );
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (generation == sessionRevision)
                        restored = true;
                });
                await CacheFailure(generation);
            }
        }
    }

    private Task Persist()
    {
        if (cache is null || !restored || session.State is not SessionState.SignedIn signed)
            return Task.CompletedTask;
        if (State.Room is { } room && State.Messages is { } messages)
            histories[room.Id] = messages.TakeLast(500).ToArray();
        var rooms = (State.Rooms ?? []).ToList();
        if (State.Room is { } current)
        {
            rooms.RemoveAll(item => item.Id == current.Id);
            rooms.Insert(0, current);
        }
        var snapshot = new ChatSnapshot(
            1,
            rooms.Take(200).ToArray(),
            histories.TakeLast(200).ToDictionary(item => item.Key, item => item.Value),
            new(drafts),
            new(pending)
        );
        queuedWrites[signed.Profile.Id] = (snapshot, sessionRevision);
        if (!writing)
        {
            writing = true;
            flushSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            persistence = WriteQueued();
        }
        return persistence;
    }

    private async Task WriteQueued()
    {
        // Coalesce typing bursts; FlushAsync also waits for this final pending write.
        await Task.WhenAny(Task.Delay(100), flushSignal!.Task);
        while (true)
        {
            KeyValuePair<string, (ChatSnapshot Snapshot, int Generation)>? next = null;
            await uiDispatcher.InvokeAsync(() =>
            {
                if (queuedWrites.Count == 0)
                {
                    writing = false;
                    return;
                }
                next = queuedWrites.First();
                queuedWrites.Remove(next.Value.Key);
            });
            if (next is null)
                return;
            var item = next.Value;
            try
            {
                await cache!.WriteAsync(item.Key, item.Value.Snapshot, CancellationToken.None);
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (item.Value.Generation != sessionRevision)
                        return;
                    cacheWriteFailed = false;
                    SetState(State with { CacheError = "" });
                });
            }
            catch (Exception)
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (item.Value.Generation == sessionRevision)
                        cacheWriteFailed = true;
                });
                await CacheFailure(item.Value.Generation);
            }
        }
    }

    private Task CacheFailure(int generation) =>
        uiDispatcher.InvokeAsync(() =>
        {
            if (generation == sessionRevision)
                SetState(State with { CacheError = presentation.Text.ChatCacheUnavailable });
        });

    private ChatRoom? editingGroup;
    private string groupRequestId = Guid.NewGuid().ToString();
    private readonly Dictionary<string, string> drafts = new();
    private readonly Dictionary<string, ChatMessage> pending = new();
    private readonly HashSet<string> sending = new();
    private readonly HashSet<string> revokedRooms = new();
    private CancellationTokenSource? subscription;
    private CancellationTokenSource accountLifetime = new();
    private CancellationToken? activeToken;

    public void Activate(CancellationToken token)
    {
        activeToken = token;
        Subscribe();
    }

    public void Deactivate()
    {
        activeToken = null;
        refreshRequested = false;
        StopSubscription();
    }

    private void StopSubscription()
    {
        subscription?.Cancel();
        subscription?.Dispose();
        subscription = null;
        SetState(State with { Connected = false });
    }

    public void Clear()
    {
        accountLifetime.Cancel();
        accountLifetime.Dispose();
        accountLifetime = new();
        Deactivate();
        revision++;
        sessionRevision++;
        loadRevision++;
        drafts.Clear();
        pending.Clear();
        sending.Clear();
        revokedRooms.Clear();
        histories.Clear();
        cacheWriteFailed = false;
        restored = false;
        restoring = null;
        SetState(new());
    }

    public void SetDraft(string text)
    {
        if (State.Room is { } room)
            drafts[room.Id] = text;
        SetState(State with { Draft = text });
        _ = Persist();
    }

    public void SetSearch(string text) => SetState(State with { Search = text, Users = null });

    public void SearchUsers(bool group = false)
    {
        editingGroup = null;
        groupRequestId = Guid.NewGuid().ToString();
        StopSubscription();
        revision++;
        loadRevision++;
        SetState(
            State with
            {
                Room = null,
                Searching = true,
                Search = "",
                Users = null,
                Error = "",
                Loading = false,
                CreatingGroup = group,
                GroupName = "",
                SelectedUsers = [],
                EditingGroup = false,
                GroupPhoto = null,
            }
        );
    }

    public void EditGroup()
    {
        if (
            State.Room is not { } room
            || session.State is not SessionState.SignedIn signed
            || room.Admin != signed.Profile.Id
        )
            return;
        SearchUsers(true);
        editingGroup = room;
        SetState(
            State with
            {
                EditingGroup = true,
                GroupName = room.Title ?? "",
                SelectedUsers = room.Users.Where(user => user.Id != signed.Profile.Id).ToArray(),
            }
        );
    }

    public async Task LeaveGroupAsync(CancellationToken token)
    {
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = operationLifetime.Token;
        if (State.Room is not { } room || State.Loading)
            return;
        await Load(
            async user =>
            {
                await api.LeaveGroupAsync(room, user, token);
                return new();
            },
            (current, _) =>
            {
                StopSubscription();
                revision++;
                revokedRooms.Add(room.Id);
                drafts.Remove(room.Id);
                pending.Remove(room.Id);
                histories.Remove(room.Id);
                return current with
                {
                    Room = null,
                    Messages = null,
                    Draft = "",
                    Connected = false,
                    Rooms = current.Rooms?.Where(item => item.Id != room.Id).ToArray(),
                };
            },
            token
        );
        await uiDispatcher.InvokeAsync(Subscribe);
    }

    public async Task RenameGroupAsync(CancellationToken token) =>
        await ChangeGroupAsync(State.GroupName.Trim(), null, token);

    public async Task RemoveMemberAsync(string userId, CancellationToken token) =>
        await ChangeGroupAsync(State.Room?.Title ?? "", userId, token);

    private async Task ChangeGroupAsync(string title, string? remove, CancellationToken token)
    {
        if (
            State.Room is not { } room
            || State.Loading
            || title.Length < 3
            || session.State is not SessionState.SignedIn signed
            || room.Admin != signed.Profile.Id
            || remove == signed.Profile.Id
        )
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = linked.Token;
        await Load(
            async user =>
                new(
                    Room: await api.UpdateGroupAsync(
                        room,
                        user,
                        title,
                        room.Participants.Where(id => id != user && id != remove)
                            .Select(id =>
                                room.Users.FirstOrDefault(item => item.Id == id)
                                ?? new ChatUser(id, "", "")
                            )
                            .ToArray(),
                        token
                    )
                ),
            (current, result) => current with { Room = result.Room },
            token
        );
    }

    public void SetGroupName(string value) => SetState(State with { GroupName = value });

    public void ToggleGroupUser(ChatUser user) =>
        SetState(
            State with
            {
                SelectedUsers =
                    State.SelectedUsers?.Any(item => item.Id == user.Id) == true
                        ? State.SelectedUsers.Where(item => item.Id != user.Id).ToArray()
                        : [.. State.SelectedUsers ?? [], user],
            }
        );

    public async Task CreateGroupAsync(CancellationToken token)
    {
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = operationLifetime.Token;
        if (State.Loading)
            return;
        var name = State.GroupName;
        var users = State.SelectedUsers ?? [];
        if (name.Trim().Length < 3 || users.Count == 0)
        {
            SetState(State with { Error = presentation.Text.ChatGroupRequired });
            return;
        }
        var edit = editingGroup;
        var photo = State.GroupPhoto;
        await Load(
            async user =>
                new(
                    Room: edit is null
                        ? await api.CreateGroupWithPhotoAsync(
                            groupRequestId,
                            user,
                            name,
                            users,
                            photo,
                            token
                        )
                        : await api.UpdateGroupAsync(edit, user, name, users, token)
                ),
            (current, result) =>
                current with
                {
                    Room = result.Room,
                    Messages = [],
                    Searching = false,
                    CreatingGroup = false,
                    Draft = "",
                    Error = "",
                },
            token
        );
        await uiDispatcher.InvokeAsync(Subscribe);
    }

    public void Back()
    {
        _ = Persist();
        StopSubscription();
        revision++;
        loadRevision++;
        SetState(
            State with
            {
                Room = null,
                Messages = null,
                Draft = "",
                Searching = false,
                Search = "",
                Error = "",
                Loading = false,
                Sending = false,
                Starting = false,
            }
        );
        Subscribe();
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        if (State.Searching || State.Loading)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = linked.Token;
        var room = State.Room;
        await Load(
            async user =>
                room is null
                    ? new(Rooms: await api.ListAsync(user, token))
                    : await LoadRoomHistory(room, user, token),
            (current, result) =>
                room is null
                    ? current with
                    {
                        Rooms = result.Rooms,
                    }
                    : current with
                    {
                        Room = result.Room,
                        Messages = Merge(result.Messages ?? [], current.Messages ?? []),
                        HistoryComplete = true,
                    },
            token
        );
    }

    public Task OpenAsync(ChatRoom room, CancellationToken token)
    {
        _ = Persist();
        revision++;
        loadRevision++;
        SetState(
            State with
            {
                Room = room,
                Messages = histories.GetValueOrDefault(room.Id),
                Draft = drafts.GetValueOrDefault(room.Id, ""),
                Searching = false,
                Error = "",
                ReceiptError = "",
                Loading = false,
                HistoryComplete = false,
                Sending = sending.Contains(room.Id),
            }
        );
        Subscribe();
        return RefreshAsync(token);
    }

    public async Task SearchAsync(CancellationToken token)
    {
        if (State.Starting)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = linked.Token;
        var term = State.Search;
        await Load(
            async user =>
                new(
                    Users: term.Trim().Length < 2
                        ? []
                        : (await api.SearchAsync(term, token))
                            .Where(item => item.Id != user)
                            .ToArray()
                ),
            (current, result) =>
                current.Search == term ? current with { Users = result.Users } : current,
            token
        );
    }

    public async Task StartAsync(ChatUser other, CancellationToken token)
    {
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = operationLifetime.Token;
        if (State.Starting)
            return;
        var existing = State.Rooms?.FirstOrDefault(room =>
            room.Participants.Length == 2
            && room.Participants.Contains(other.Id)
            && string.IsNullOrEmpty(room.Admin)
            && string.IsNullOrEmpty(room.Title)
            && string.IsNullOrEmpty(room.GameId)
        );
        if (existing is not null)
        {
            await OpenAsync(existing, token);
            return;
        }
        var generation = sessionRevision;
        var currentView = revision;
        SetState(State with { Starting = true });
        await Load(
            async user => new(Room: await api.StartAsync(user, other, token)),
            (current, result) =>
                current with
                {
                    Room = result.Room,
                    Messages = null,
                    Searching = false,
                    Draft = drafts.GetValueOrDefault(result.Room!.Id, ""),
                    Error = "",
                },
            token
        );
        await uiDispatcher.InvokeAsync(() =>
        {
            if (generation != sessionRevision || currentView != revision)
                return;
            SetState(State with { Starting = false });
            if (State.Room is not null && !State.Searching && !token.IsCancellationRequested)
            {
                Subscribe();
                _ = RefreshAsync(token);
            }
        });
    }

    private async Task Load(
        Func<string, Task<ChatState>> operation,
        Func<ChatState, ChatState, ChatState> apply,
        CancellationToken token,
        [System.Runtime.CompilerServices.CallerMemberName] string operationName = ""
    )
    {
        if (session.State is not SessionState.SignedIn signed)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = linked.Token;
        Console.WriteLine($"[FNI chat] UI {operationName} begin");
        var request = ++loadRevision;
        var view = revision;
        var account = sessionRevision;
        SetState(State with { Loading = true, Error = "" });
        try
        {
            await RestoreAsync(signed.Profile.Id, account, token);
            if (account != sessionRevision || token.IsCancellationRequested)
                return;
            var result = await operation(signed.Profile.Id);
            await uiDispatcher.InvokeAsync(() =>
            {
                if (
                    !token.IsCancellationRequested
                    && request == loadRevision
                    && view == revision
                    && account == sessionRevision
                )
                {
                    if (result.Room is { } authorized)
                        revokedRooms.Remove(authorized.Id);
                    SetState(apply(State, result) with { Loading = false, Error = "" });
                    if (result.Rooms is { } authoritative)
                    {
                        var allowed = authoritative.Select(item => item.Id).ToHashSet();
                        foreach (
                            var id in histories
                                .Keys.Concat(drafts.Keys)
                                .Concat(pending.Keys)
                                .Distinct()
                                .Where(id => !allowed.Contains(id))
                                .ToArray()
                        )
                        {
                            revokedRooms.Add(id);
                            histories.Remove(id);
                            drafts.Remove(id);
                            pending.Remove(id);
                        }
                    }
                    _ = Persist();
                    _ = MarkReadAsync();
                }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.WriteLine(
                $"[FNI chat] UI {operationName} failed type={error.GetType().Name} inner={error.InnerException?.GetType().Name} stack={error.StackTrace}"
            );
            await uiDispatcher.InvokeAsync(() =>
            {
                if (request == loadRevision && view == revision && account == sessionRevision)
                {
                    if (error is UnauthorizedAccessException && State.Room is { } denied)
                    {
                        StopSubscription();
                        revokedRooms.Add(denied.Id);
                        histories.Remove(denied.Id);
                        drafts.Remove(denied.Id);
                        pending.Remove(denied.Id);
                        SetState(
                            State with
                            {
                                Room = null,
                                Messages = null,
                                Draft = "",
                                Rooms = State.Rooms?.Where(item => item.Id != denied.Id).ToArray(),
                            }
                        );
                        _ = Persist();
                    }
                    SetState(
                        State with
                        {
                            Error = FniErrors.UserMessage(presentation.Text, error),
                        }
                    );
                }
            });
        }
        finally
        {
            Console.WriteLine($"[FNI chat] UI {operationName} end");
            await uiDispatcher.InvokeAsync(() =>
            {
                if (request == loadRevision && view == revision && account == sessionRevision)
                {
                    SetState(State with { Loading = false });
                    if (
                        refreshRequested
                        && activeToken is { } refreshToken
                        && !refreshToken.IsCancellationRequested
                    )
                    {
                        refreshRequested = false;
                        _ = RefreshAsync(refreshToken);
                    }
                }
            });
        }
    }

    public bool HasPendingMessage => State.Room is { } room && pending.ContainsKey(room.Id);

    public async Task SendAsync(CancellationToken token)
    {
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = operationLifetime.Token;
        if (token.IsCancellationRequested)
            return;
        if (cache is not null && !restored && session.State is SessionState.SignedIn owner)
        {
            var generation = sessionRevision;
            await RestoreAsync(owner.Profile.Id, generation, token);
            Task resumed = Task.CompletedTask;
            await uiDispatcher.InvokeAsync(() =>
            {
                if (generation == sessionRevision && !token.IsCancellationRequested)
                    resumed = SendAsync(token);
            });
            await resumed;
            return;
        }
        if (
            session.State is not SessionState.SignedIn signed
            || State.Room is not { } room
            || (string.IsNullOrWhiteSpace(State.Draft) && !pending.ContainsKey(room.Id))
            || sending.Contains(room.Id)
        )
            return;
        var account = sessionRevision;
        var draft = State.Draft;
        var content = draft.Trim();
        var retry = pending.TryGetValue(room.Id, out var message);
        var sendingCurrentDraft = !retry || message!.Content == content;
        if (!retry)
            message = new(
                Guid.NewGuid().ToString(),
                room.Id,
                signed.Profile.Id,
                content,
                DateTimeOffset.UtcNow,
                [signed.Profile.Id]
            );
        pending[room.Id] = message!;
        sending.Add(room.Id);
        if (sendingCurrentDraft)
            drafts[room.Id] = "";
        SetState(
            State with
            {
                Sending = true,
                Error = "",
                Messages = Merge(State.Messages ?? [], [message!]),
                Draft = sendingCurrentDraft ? "" : State.Draft,
            }
        );
        ChatMessage? completed = null;
        try
        {
            // Commit the retry ID before the mutation so an interrupted app can reconcile it.
            await Persist();
            if (account != sessionRevision || token.IsCancellationRequested)
                return;
            if (cacheWriteFailed)
                throw new IOException("Pending message could not be stored.");
            ChatMessage? confirmed = null;
            if (retry)
                confirmed = (await api.MessagesAsync(room.Id, token)).FirstOrDefault(item =>
                    item.Id == message!.Id
                );
            confirmed ??= await api.SendAsync(message!, token);
            completed = confirmed;
            await uiDispatcher.InvokeAsync(() =>
            {
                if (
                    account != sessionRevision
                    || token.IsCancellationRequested
                    || revokedRooms.Contains(room.Id)
                )
                    return;
                pending.Remove(room.Id);
                if (State.Room?.Id == room.Id)
                    SetState(
                        State with
                        {
                            Messages = Merge(State.Messages ?? [], [confirmed]),
                            Draft = State.Draft,
                            Error = "",
                        }
                    );
                histories[room.Id] = Merge(histories.GetValueOrDefault(room.Id, []), [confirmed])
                    .TakeLast(500)
                    .ToArray();
                _ = Persist();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (account == sessionRevision && State.Room?.Id == room.Id)
                    SetState(State with { Error = presentation.Text.ErrorSendingMessage });
            });
        }
        finally
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (account != sessionRevision)
                    return;
                sending.Remove(room.Id);
                if (State.Room?.Id == room.Id)
                    SetState(State with { Sending = false });
            });
        }
        if (completed is not null && account == sessionRevision && !token.IsCancellationRequested)
        {
            try
            {
                await api.CompleteSendAsync(completed, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Console.WriteLine(
                    $"[FNI chat] delivery follow-up failed type={error.GetType().Name}"
                );
            }
        }
    }

    private async Task<ChatState> LoadRoomHistory(
        ChatRoom room,
        string user,
        CancellationToken token
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        var roomTask = api.GetAsync(room.Id, user, linked.Token);
        var historyTask = api.MessagesAsync(room.Id, linked.Token);
        await Task.WhenAll(roomTask, historyTask);
        var current = await roomTask;
        if (current is null || !current.Participants.Contains(user))
            throw new UnauthorizedAccessException();
        return new(Room: current, Messages: await historyTask);
    }

    // Call after session readiness, on the UI dispatcher. Errors remain visible and the same ID can be retried.
    public Task OpenByIdAsync(string chatId, CancellationToken token) =>
        ResolveRoomAsync((user, ct) => api.GetAsync(chatId, user, ct), token);

    public Task OpenGameAsync(string gameId, CancellationToken token) =>
        ResolveRoomAsync((user, ct) => api.FindGameAsync(gameId, user, ct), token);

    private async Task ResolveRoomAsync(
        Func<string, CancellationToken, Task<ChatRoom?>> find,
        CancellationToken token
    )
    {
        if (State.Loading)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        await Load(
            async user =>
                new(
                    Room: await find(user, linked.Token)
                        ?? throw new InvalidOperationException("Chat is unavailable.")
                ),
            (current, result) =>
                current with
                {
                    Room = result.Room,
                    Messages = null,
                    Draft = drafts.GetValueOrDefault(result.Room!.Id, ""),
                    Searching = false,
                    HistoryComplete = false,
                },
            linked.Token
        );
        Task refresh = Task.CompletedTask;
        await uiDispatcher.InvokeAsync(() =>
        {
            if (
                !linked.IsCancellationRequested
                && State.Room is not null
                && State.Error.Length == 0
            )
            {
                Subscribe();
                refresh = RefreshAsync(linked.Token);
            }
        });
        await refresh;
    }

    public async Task PickGroupPhotoAsync(
        Func<CancellationToken, Task<byte[]?>> pick,
        CancellationToken token
    )
    {
        if (State.Loading || State.EditingGroup)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        await Load(
            async user =>
            {
                var photo = await pick(linked.Token);
                if (photo is not null)
                    _ = PhotoFormat.Detect(photo);
                return new(GroupPhoto: photo);
            },
            (current, result) =>
                result.GroupPhoto is null
                    ? current
                    : current with
                    {
                        GroupPhoto = result.GroupPhoto,
                    },
            linked.Token
        );
    }

    public async Task DeleteForEveryoneAsync(CancellationToken token)
    {
        if (State.Loading || State.Sending || State.Room is not { } room)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        await Load(
            async user =>
            {
                await api.DeleteForEveryoneAsync(room, user, linked.Token);
                return new();
            },
            (current, _) =>
            {
                StopSubscription();
                revision++;
                revokedRooms.Add(room.Id);
                drafts.Remove(room.Id);
                pending.Remove(room.Id);
                histories.Remove(room.Id);
                return current with
                {
                    Room = null,
                    Messages = null,
                    Draft = "",
                    Rooms = current.Rooms?.Where(item => item.Id != room.Id).ToArray(),
                };
            },
            linked.Token
        );
        await uiDispatcher.InvokeAsync(Subscribe);
    }

    public Task RetryReadAsync() => MarkReadAsync();

    private bool markingRead;

    private async Task MarkReadAsync()
    {
        if (
            markingRead
            || activeToken is not { } token
            || token.IsCancellationRequested
            || State.Room is not { } room
            || session.State is not SessionState.SignedIn signed
        )
            return;
        var ids = (State.Messages ?? [])
            .Where(message =>
                message.SenderId != signed.Profile.Id && !message.SeenBy.Contains(signed.Profile.Id)
            )
            .Select(message => message.Id)
            .ToArray();
        if (ids.Length == 0)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            accountLifetime.Token
        );
        token = linked.Token;
        var account = sessionRevision;
        markingRead = true;
        try
        {
            await api.MarkReadAsync(signed.Profile.Id, ids, token);
            await uiDispatcher.InvokeAsync(() =>
            {
                if (
                    account == sessionRevision
                    && State.Room?.Id == room.Id
                    && !token.IsCancellationRequested
                )
                {
                    SetState(
                        State with
                        {
                            ReceiptError = "",
                            Messages = (State.Messages ?? [])
                                .Select(message =>
                                    ids.Contains(message.Id)
                                        ? message with
                                        {
                                            SeenBy = message
                                                .SeenBy.Append(signed.Profile.Id)
                                                .Distinct()
                                                .ToArray(),
                                        }
                                        : message
                                )
                                .ToArray(),
                        }
                    );
                    _ = Persist();
                }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (account == sessionRevision && State.Room?.Id == room.Id)
                    SetState(
                        State with
                        {
                            ReceiptError = FniErrors.UserMessage(presentation.Text, error),
                        }
                    );
            });
        }
        finally
        {
            await uiDispatcher.InvokeAsync(() => markingRead = false);
        }
    }

    private static IReadOnlyList<ChatMessage> Merge(
        IEnumerable<ChatMessage> first,
        IEnumerable<ChatMessage> second
    ) =>
        first
            .Concat(second)
            .GroupBy(message => message.Id)
            .Select(group =>
                group.Last() with
                {
                    SeenBy = group.SelectMany(message => message.SeenBy).Distinct().ToArray(),
                }
            )
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id, StringComparer.Ordinal)
            .ToArray();

    private void Subscribe()
    {
        StopSubscription();
        if (
            activeToken is not { } token
            || State.Searching
            || session.State is not SessionState.SignedIn signed
        )
            return;
        subscription = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = Watch(State.Room?.Id, signed.Profile.Id, revision, sessionRevision, subscription.Token);
    }

    private async Task Watch(
        string? roomId,
        string userId,
        int view,
        int account,
        CancellationToken token
    )
    {
        var backoff = 1;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await foreach (
                    var update in roomId is null
                        ? api.WatchInboxAsync(userId, token)
                        : api.WatchAsync(roomId, token)
                )
                {
                    await uiDispatcher.InvokeAsync(() =>
                    {
                        if (
                            token.IsCancellationRequested
                            || view != revision
                            || account != sessionRevision
                        )
                            return;
                        SetState(
                            State with
                            {
                                Connected = update.Connected,
                                Messages = update.Message is { } message
                                    ? Merge(State.Messages ?? [], [message])
                                    : State.Messages,
                            }
                        );
                        if (update.Message is not null)
                        {
                            _ = Persist();
                            _ = MarkReadAsync();
                        }
                        // Establish first, then reconcile history to cover the subscription gap.
                        if (update.Message is null && update.Connected)
                        {
                            if (State.Loading)
                                refreshRequested = true;
                            else
                                _ = RefreshAsync(token);
                        }
                    });
                    backoff = 1;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            { /* Reconnect with a fresh Cognito token; never expose credentials in errors. */
            }
            await uiDispatcher.InvokeAsync(() =>
            {
                if (
                    !token.IsCancellationRequested
                    && view == revision
                    && account == sessionRevision
                )
                    SetState(State with { Connected = false });
            });
            try
            {
                await (reconnectDelay ?? Task.Delay)(TimeSpan.FromSeconds(backoff), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            backoff = Math.Min(backoff * 2, 30);
        }
    }
}
