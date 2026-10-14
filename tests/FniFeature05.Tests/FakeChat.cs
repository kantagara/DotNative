using FaliNamIgrac;

sealed class FakeChat : IChatService
{
    public bool Member = true;
    public int StartCalls;
    public int WatchFailures,
        WatchCalls;

    public Task<ChatRoom?> GetAsync(string id, string user, CancellationToken token) =>
        Task.FromResult<ChatRoom?>(Member ? Room with { Id = id } : null);

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
        WatchCalls++;
        if (WatchFailures-- > 0)
            throw new IOException("Disconnected");
        yield return new(Connected: true);
        await foreach (var update in Updates.Reader.ReadAllAsync(token))
            yield return update;
    }

    public Task<IReadOnlyList<ChatRoom>> ListAsync(string userId, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ChatRoom>>([Room]);

    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(string chatId, CancellationToken token) =>
        Messages();

    public Task MarkReadAsync(string userId, string[] ids, CancellationToken token) =>
        Task.CompletedTask;

    public Func<Task> CompleteSend = () => Task.CompletedTask;

    public Task CompleteSendAsync(ChatMessage message, CancellationToken token) => CompleteSend();

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

    public Task<ChatRoom> StartAsync(string userId, ChatUser other, CancellationToken token)
    {
        StartCalls++;
        return Task.FromResult(Room);
    }
}
