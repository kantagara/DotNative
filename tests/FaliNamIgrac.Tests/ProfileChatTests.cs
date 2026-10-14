using FaliNamIgrac;

sealed class FakeAccount : IAccountService
{
    public static AccountData Data = new()
    {
        Id = "player",
        Firstname = "Player",
        Lastname = "Test",
        Nickname = "tester",
        BirthDate = "2000-01-01",
    };
    public Func<Task<AccountData>> Load = () => Task.FromResult(Data);
    public AccountData? Saved;

    public Task<AccountData> LoadAsync(CancellationToken token) => Load();

    public Task SaveAsync(AccountData account, CancellationToken token)
    {
        Saved = account;
        return Task.CompletedTask;
    }
}

sealed class FakeChat : IChatService
{
    public static ChatRoom Room = new("room", ["player", "other"], DateTimeOffset.UnixEpoch);
    public Func<Task<IReadOnlyList<ChatMessage>>> Messages = () =>
        Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    public Func<ChatMessage, Task<ChatMessage>> Send = message => Task.FromResult(message);
    public readonly System.Threading.Channels.Channel<ChatUpdate> Updates =
        System.Threading.Channels.Channel.CreateUnbounded<ChatUpdate>();

    public IAsyncEnumerable<ChatUpdate> WatchInboxAsync(string userId, CancellationToken token) =>
        WatchAsync("", token);

    public async IAsyncEnumerable<ChatUpdate> WatchAsync(
        string chatId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token
    )
    {
        yield return new(Connected: true);
        await foreach (var update in Updates.Reader.ReadAllAsync(token))
            yield return update;
    }

    public Func<CancellationToken, Task<IReadOnlyList<ChatRoom>>> Rooms = _ =>
        Task.FromResult<IReadOnlyList<ChatRoom>>([Room]);
    public int ListCalls;

    public Task<IReadOnlyList<ChatRoom>> ListAsync(string userId, CancellationToken token)
    {
        ListCalls++;
        return Rooms(token);
    }

    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(string chatId, CancellationToken token) =>
        Messages();

    public Task MarkReadAsync(string userId, string[] ids, CancellationToken token) =>
        Task.CompletedTask;

    public Task<ChatMessage> SendAsync(ChatMessage message, CancellationToken token) =>
        Send(message);

    public Task<IReadOnlyList<ChatUser>> SearchAsync(string text, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ChatUser>>([]);

    public Task<ChatRoom> UpdateGroupAsync(
        ChatRoom room,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    ) => Task.FromResult(room with { Title = title, Users = users.ToArray() });

    public Task LeaveGroupAsync(ChatRoom room, string userId, CancellationToken token) =>
        Task.CompletedTask;

    public Task<ChatRoom> CreateGroupAsync(
        string id,
        string userId,
        string title,
        IReadOnlyList<ChatUser> users,
        CancellationToken token
    ) =>
        Task.FromResult(
            Room with
            {
                Id = id,
                Title = title,
                Admin = userId,
                Users = users.ToArray(),
            }
        );

    public Task<ChatRoom> StartAsync(string userId, ChatUser other, CancellationToken token) =>
        Task.FromResult(Room);
}

internal static class ProfileChatTests
{
    public static void Run()
    {
        using var f = new Fixture();
        f.SignIn();
        var profileApi = new FakeAccount();
        var profile = new AccountNotifier(f.Dispatcher, f.Session, profileApi, f.Presentation);
        f.Dispatcher.Finish(profile.LoadAsync(default));
        profile.Edit(data =>
            data with
            {
                Firstname = "Updated",
                Sports = [new("tennis", 4, "OFF")],
            }
        );
        f.Dispatcher.Finish(profile.SaveAsync(default));
        Check(
            profileApi.Saved?.Firstname == "Updated"
                && profile.State.Saved?.Sports[0].SkillLevel == 4,
            "Profile save failed"
        );
        profile.Edit(data => data with { Firstname = "" });
        f.Dispatcher.Finish(profile.SaveAsync(default));
        Check(
            profile.State.Message == f.Presentation.Text.EditProfileMissingInfo
                && profileApi.Saved?.Firstname == "Updated",
            "Invalid profile reached backend"
        );
        profile.CancelEdit();
        Check(profile.State.Draft?.Firstname == "Updated", "Cancel lost saved profile");

        var api = new FakeChat();
        var chat = new ChatNotifier(f.Dispatcher, f.Session, api, f.Presentation);
        f.Dispatcher.Finish(chat.OpenAsync(FakeChat.Room, default));
        chat.SetDraft("Keep this draft");
        var incoming = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        api.Messages = () => incoming.Task;
        var refresh = chat.RefreshAsync(default);
        chat.SetDraft("Typed while refreshing");
        incoming.SetResult([]);
        f.Dispatcher.Finish(refresh);
        Check(chat.State.Draft == "Typed while refreshing", "Refresh erased typed text");

        var send = new TaskCompletionSource<ChatMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        ChatMessage? sent = null;
        api.Send = message =>
        {
            sent = message;
            return send.Task;
        };
        var sending = chat.SendAsync(default);
        chat.SetDraft("Next message");
        // Double-tap cannot submit the same outgoing message twice.
        f.Dispatcher.Finish(chat.SendAsync(default));
        send.SetResult(sent!);
        f.Dispatcher.Finish(sending);
        Check(
            chat.State.Messages?.Count == 1 && chat.State.Draft == "Next message",
            "Send erased the next draft"
        );

        // Lost HTTP response after server accepted message: retry discovers same id.
        ChatMessage? accepted = null;
        var sends = 0;
        api.Send = message =>
        {
            sends++;
            accepted = message;
            return Task.FromException<ChatMessage>(new HttpRequestException());
        };
        f.Dispatcher.Finish(chat.SendAsync(default));
        Check(
            chat.State.Error.Length > 0
                && chat.State.Draft == ""
                && chat.HasPendingMessage
                && chat.State.Messages!.Any(item =>
                    item.Id == accepted!.Id && item.Content == "Next message"
                ),
            "Failed optimistic send must retain its message and retry ID"
        );
        api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([accepted!]);
        f.Dispatcher.Finish(chat.SendAsync(default));
        Check(sends == 1 && chat.State.Draft == "", "Retry duplicated confirmed message");

        chat.SetDraft("Room one draft");
        chat.Back();
        f.Dispatcher.Finish(chat.OpenAsync(FakeChat.Room with { Id = "second" }, default));
        chat.SetDraft("Room two draft");
        f.Dispatcher.Finish(chat.OpenAsync(FakeChat.Room, default));
        Check(chat.State.Draft == "Room one draft", "Room navigation lost draft");

        incoming = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Messages = () => incoming.Task;
        refresh = chat.RefreshAsync(default);
        chat.Back();
        incoming.SetResult([accepted!]);
        f.Dispatcher.Finish(refresh);
        Check(
            chat.State.Room is null && chat.State.Messages is null,
            "Old room response reopened the conversation"
        );

        api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        f.Dispatcher.Finish(chat.OpenAsync(FakeChat.Room, default));
        using var live = new CancellationTokenSource();
        chat.Activate(live.Token);
        f.Dispatcher.Drain();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listen = chat.Listen(() =>
        {
            if (chat.State.Messages?.Any(message => message.Id == "live") == true)
                received.TrySetResult();
        });
        var realtime = new ChatMessage(
            "live",
            "room",
            "other",
            "Realtime",
            DateTimeOffset.UtcNow,
            ["other"]
        );
        api.Updates.Writer.TryWrite(new(realtime, true));
        f.Dispatcher.Finish(received.Task);
        Check(chat.State.Connected, "Realtime connection state was lost");
        api.Messages = () => Task.FromResult<IReadOnlyList<ChatMessage>>([realtime]);
        f.Dispatcher.Finish(chat.RefreshAsync(default));
        Check(
            chat.State.Messages?.Count(message => message.Id == "live") == 1,
            "History reconciliation duplicated a realtime message"
        );
        chat.Deactivate();
        live.Cancel();

        chat.SearchUsers(true);
        chat.SetGroupName("Team chat");
        chat.ToggleGroupUser(new("other", "Other", "Player"));
        f.Dispatcher.Finish(chat.CreateGroupAsync(default));
        Check(
            chat.State.Room?.Title == "Team chat" && chat.State.Room.Admin == "player",
            "Group creation failed"
        );
        chat.EditGroup();
        chat.SetGroupName("Updated team");
        f.Dispatcher.Finish(chat.CreateGroupAsync(default));
        Check(
            chat.State.Room?.Title == "Updated team",
            "Group edit created a different conversation"
        );
        f.Dispatcher.Finish(chat.LeaveGroupAsync(default));
        Check(chat.State.Room is null, "Leaving group did not close conversation");

        var staleProfile = new TaskCompletionSource<AccountData>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        profileApi.Load = () => staleProfile.Task;
        var loading = profile.LoadAsync(default);
        profile.Clear();
        chat.Clear();
        f.Dispatcher.Finish(f.Session.SignOutAsync(default));
        staleProfile.SetResult(FakeAccount.Data);
        f.Dispatcher.Finish(loading);
        Check(
            profile.State.Saved is null && chat.State.Rooms is null,
            "Sign-out retained private data"
        );

        var theme = new FakeTheme();
        string? savedLanguage = null;
        var settings = new SettingsNotifier(
            f.Dispatcher,
            theme,
            f.Presentation,
            (language, _) =>
            {
                savedLanguage = language;
                return Task.CompletedTask;
            }
        );
        f.Dispatcher.Finish(settings.SetLanguageAsync("sr", default));
        Check(
            savedLanguage == "sr" && f.Presentation.Locale.Value == "sr",
            "Language did not persist and apply"
        );
        var failed = new SettingsNotifier(
            f.Dispatcher,
            theme,
            f.Presentation,
            (_, _) => Task.FromException(new IOException())
        );
        f.Dispatcher.Finish(failed.SetLanguageAsync("ru", default));
        Check(
            f.Presentation.Locale.Value == "sr" && failed.State.Length > 0,
            "Failed persistence changed active language"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
