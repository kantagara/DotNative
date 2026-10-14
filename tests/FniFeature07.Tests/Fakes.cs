using FaliNamIgrac;

sealed class FakeSession : ISessionService
{
    public Func<Task> SignIn = () => Task.CompletedTask;
    public int SignIns;
    public bool IsSignedIn => true;
    public string Email { get; set; } = "test@example.com";

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

    public int Confirmations,
        Resends;
    public bool WrongCode;

    public Task ConfirmSignUpAsync(string email, string code, CancellationToken token)
    {
        Confirmations++;
        return WrongCode
            ? Task.FromException(new InvalidOperationException("fixture wrong code"))
            : Task.CompletedTask;
    }

    public Task ResendSignUpCodeAsync(string email, CancellationToken token)
    {
        Resends++;
        return Task.CompletedTask;
    }

    public Task RequestPasswordResetAsync(string email, CancellationToken token) => ResetRequest();

    public Func<Task> ConfirmReset = () => Task.CompletedTask;
    public Func<Task> ResetRequest = () => Task.CompletedTask;

    public Task ConfirmPasswordResetAsync(
        string email,
        string code,
        string password,
        CancellationToken token
    ) => ConfirmReset();

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
        return Task.FromResult(Profile);
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

sealed class FakeProfile : IProfileService
{
    public Task<bool> IsNicknameAvailableAsync(string nickname, CancellationToken token) =>
        Task.FromResult(true);

    public int Creates;

    public Task<string> CreateAsync(NewProfile profile, CancellationToken token)
    {
        Creates++;
        return Task.FromResult("player");
    }

    public bool FailUpload;

    public Task UploadPhotoAsync(string userId, byte[] photo, CancellationToken token) =>
        FailUpload ? Task.FromException(new HttpRequestException()) : Task.CompletedTask;
}

sealed class FakePlaces : IPlacesService
{
    public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string text, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<PlaceSuggestion>>([]);

    public Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token) =>
        Task.FromResult(new SelectedPlace("Belgrade", 44, 20));
}
