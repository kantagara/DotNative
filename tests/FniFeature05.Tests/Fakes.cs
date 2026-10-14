using System.Collections.Concurrent;
using DotNative;
using FaliNamIgrac;

sealed class Dispatcher : IUiDispatcher
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void Drain()
    {
        while (queue.TryDequeue(out var action))
            action();
    }

    public void Finish(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Drain();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException();
        task.GetAwaiter().GetResult();
        Drain();
    }
}

sealed class FakeSession : ISessionService
{
    public Func<Task> SignIn = () => Task.CompletedTask;
    public int SignIns;
    public bool IsSignedIn => true;
    public string Email => "test@example.com";

    public Task SignInAsync(string email, string password, CancellationToken token)
    {
        SignIns++;
        return SignIn();
    }

    public Task<bool> RestoreAsync(CancellationToken token) => Task.FromResult(true);

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken token) =>
        Task.FromResult(new AccountInfo(Email));

    public Task SignOutAsync(CancellationToken token) => Task.CompletedTask;

    public Task<AccountInfo> AcceptWebSessionAsync(
        string accessToken,
        string refreshToken,
        int expiresIn,
        string idToken,
        CancellationToken token
    ) => GetAccountInfoAsync(token);

    public Task SignUpAsync(string email, string password, CancellationToken token) =>
        Task.CompletedTask;

    public Task ConfirmSignUpAsync(string email, string code, CancellationToken token) =>
        Task.CompletedTask;

    public Task RequestPasswordResetAsync(string email, CancellationToken token) =>
        Task.CompletedTask;

    public Task ConfirmPasswordResetAsync(
        string email,
        string code,
        string password,
        CancellationToken token
    ) => Task.CompletedTask;

    public Task<string> AccessTokenAsync(CancellationToken token) => Task.FromResult("unused");

    public Task<string> IdTokenAsync(CancellationToken token) => Task.FromResult("unused");

    public void Dispose() { }
}

sealed class FakeRegistration : IRegistrationService
{
    public RegistrationIdentity? Identity = null;

    public Task ClearTemporaryUserAsync(CancellationToken token) => Task.CompletedTask;

    public Task<RegistrationIdentity?> RestoreTemporaryUserAsync(CancellationToken token) =>
        Task.FromResult(Identity);

    public Task<RegistrationIdentity?> ResolveSignedInUserAsync(
        AccountInfo account,
        AccountAuthType? authType,
        bool restoringSession,
        CancellationToken token
    ) => Task.FromResult(Identity);

    public Task<RegistrationChallenge> BeginAsync(RegistrationForm form, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<RegistrationIdentity> ConfirmAsync(
        RegistrationChallenge challenge,
        string code,
        CancellationToken token
    ) => throw new NotSupportedException();

    public Task ConfirmCodeAsync(
        RegistrationChallenge challenge,
        string code,
        CancellationToken token
    ) => Task.CompletedTask;

    public Task<RegistrationIdentity> SignInAfterRegistrationAsync(
        RegistrationChallenge challenge,
        CancellationToken token
    ) => Task.FromResult(Identity!);

    public Task<string?> GetOrCreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    ) => Task.FromResult<string?>("42");
}

sealed class FakeGames : IGamesApi
{
    public static readonly PlayerProfile Profile = new("player", "Player", 44, 20, 25);
    public PlayerProfile CurrentProfile { get; set; } = Profile;
    public static readonly Game Game = new(
        "game",
        "Court",
        DateTimeOffset.UnixEpoch,
        "10:00",
        "11:00",
        2,
        1,
        5,
        100,
        "RSD",
        "Player",
        "Info"
    );
    public Func<Task<IReadOnlyList<Game>>> List = () =>
        Task.FromResult<IReadOnlyList<Game>>([Game]);
    public int ListCalls,
        ProfileCalls;
    public int ApplyCalls;
    public (string User, int Sport, int Page, bool Passed) MyRequest;
    public bool FailDetails = false;

    public Task<PlayerProfile> ProfileAsync(CancellationToken token)
    {
        ProfileCalls++;
        return Task.FromResult(CurrentProfile);
    }

    public Task<IReadOnlyList<Game>> ListAsync(
        PlayerProfile user,
        int sport,
        int page,
        CancellationToken token
    )
    {
        ListCalls++;
        return List();
    }

    public Task<IReadOnlyList<Game>> MyGamesAsync(
        PlayerProfile user,
        int sport,
        int page,
        bool passed,
        CancellationToken token
    )
    {
        MyRequest = (user.Id, sport, page, passed);
        return List();
    }

    public Task<GameDetails> DetailsAsync(string id, CancellationToken token) =>
        FailDetails
            ? Task.FromException<GameDetails>(new HttpRequestException())
            : Task.FromResult(new GameDetails(Game, ["Player"]));

    public Task ApplyAsync(string id, string userId, CancellationToken token)
    {
        ApplyCalls++;
        return Task.CompletedTask;
    }
}
