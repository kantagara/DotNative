using System.Net;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Runtime;
using DotNative;
using DotNative.Cognito;
using DotNative.SecureStorage;
using FaliNamIgrac;

TestPump.Run(async () =>
{
    Environment.SetEnvironmentVariable("FNI_CLIENT_ID", "fixture-client-id");

    await Run(
        "registration contract: errors never become NotRegistered",
        async () =>
        {
            var handler = new HttpFake();
            using var http = new HttpClient(handler)
            {
                BaseAddress = new("https://example.invalid/"),
            };
            using var client = new ApiClient(http, new FakeSession());
            var api = new RegistrationApi(client);
            handler.Response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("\"TempUser\""),
            };
            Check(
                await api.CheckAsync("a+b@example.invalid", default)
                    == UserRegistrationStatus.TempUser
            );
            Check(handler.Uri!.Query.Contains("%2B", StringComparison.OrdinalIgnoreCase));
            handler.Response = new(HttpStatusCode.ServiceUnavailable);
            await Throws<HttpRequestException>(() => api.CheckAsync("a", default));
            handler.Response = new(HttpStatusCode.Unauthorized);
            await Throws<HttpRequestException>(() => api.CheckAuthenticatedAsync("a", default));
            Check(handler.Auth == "Bearer unused");
            handler.Response = new(HttpStatusCode.NotFound);
            Check(await api.GetTemporaryUserIdAsync("a", default) is null);
            handler.Response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tempUserId\":42}"),
            };
            Check(await api.GetTemporaryUserIdAsync("a", default) == "42");
            handler.Response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("\"FutureUnknownState\""),
            };
            await Throws<FormatException>(() => api.CheckAsync("a", default));
        }
    );
    await Run(
        "nickname contract distinguishes conflict from network failure",
        async () =>
        {
            var handler = new HttpFake();
            using var http = new HttpClient(handler)
            {
                BaseAddress = new("https://example.invalid/"),
            };
            using var client = new ApiClient(http, new FakeSession());
            var profiles = new ProfileService(client, new FakeSession());
            handler.Response = new(HttpStatusCode.Conflict);
            Check(!await profiles.IsNicknameAvailableAsync("p", default));
            handler.Response = new(HttpStatusCode.InternalServerError);
            await Throws<HttpRequestException>(() =>
                profiles.IsNicknameAvailableAsync("p", default)
            );
        }
    );
    await Run(
        "Cognito challenges and malformed token results are blocked",
        async () =>
        {
            foreach (
                var challenge in new[]
                {
                    "SMS_MFA",
                    "SOFTWARE_TOKEN_MFA",
                    "NEW_PASSWORD_REQUIRED",
                    "MFA_SETUP",
                    "SELECT_MFA_TYPE",
                    "CUSTOM_CHALLENGE",
                    "SMS_OTP",
                    "EMAIL_OTP",
                    "SELECT_CHALLENGE",
                    "WEB_AUTHN",
                    "FUTURE_CHALLENGE",
                }
            )
                await Throws<UnsupportedAuthenticationChallengeException>(() =>
                    Task.FromResult(CognitoSessionResult.RequireComplete(Tokens(), challenge))
                );
            Check(
                CognitoSessionResult.RequireComplete(Tokens(), null).AccessToken == "access-fixture"
            );
            await Throws<InvalidDataException>(() =>
                Task.FromResult(CognitoSessionResult.RequireComplete(new(), null))
            );
        }
    );
    await Run(
        "refresh single flight, refresh-token retention and restart",
        async () =>
        {
            var storage = new MemoryStorage();
            using var aws = new CognitoFake();
            using var session = new SessionService(aws, () => storage);
            await session.AcceptWebSessionAsync(
                "access-fixture",
                "refresh-fixture",
                1,
                "id-fixture",
                default
            );
            var values = await Task.WhenAll(
                session.AccessTokenAsync(default),
                session.AccessTokenAsync(default)
            );
            Check(aws.Refreshes == 1 && values.All(v => v == "renewed-fixture"));
            Check(storage.Values["session-v1"].Contains("refresh-fixture"));
            using var restarted = new SessionService(aws, () => storage);
            Check(await restarted.RestoreAsync(default) && restarted.Email == "test@example.com");
        }
    );
    await Run(
        "refresh cancellation and transient failure preserve retry",
        async () =>
        {
            var storage = new MemoryStorage();
            using var aws = new CognitoFake();
            using var session = new SessionService(aws, () => storage);
            await session.AcceptWebSessionAsync(
                "access-fixture",
                "refresh-fixture",
                1,
                "id-fixture",
                default
            );
            aws.FailRefresh = new HttpRequestException();
            await Throws<HttpRequestException>(() => session.AccessTokenAsync(default));
            Check(storage.Values.ContainsKey("session-v1"));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Throws<OperationCanceledException>(() => session.AccessTokenAsync(cts.Token));
            aws.FailRefresh = null;
            Check(await session.AccessTokenAsync(default) == "renewed-fixture");
        }
    );
    await Run(
        "rejected refresh clears restored session",
        async () =>
        {
            var storage = new MemoryStorage();
            using var aws = new CognitoFake();
            using var session = new SessionService(aws, () => storage);
            await session.AcceptWebSessionAsync(
                "access-fixture",
                "refresh-fixture",
                1,
                "id-fixture",
                default
            );
            aws.FailRefresh = new NotAuthorizedException("expired fixture");
            using var restarted = new SessionService(aws, () => storage);
            Check(
                !await restarted.RestoreAsync(default)
                    && !restarted.IsSignedIn
                    && !storage.Values.ContainsKey("session-v1")
            );
        }
    );
    await Run(
        "late OAuth acceptance cannot resurrect logout",
        async () =>
        {
            var storage = new MemoryStorage();
            using var aws = new CognitoFake();
            using var session = new SessionService(aws, () => storage);
            aws.UserWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = session.AcceptWebSessionAsync(
                "access-fixture",
                "refresh-fixture",
                3600,
                "id-fixture",
                default
            );
            await session.SignOutAsync(default);
            aws.UserWait.SetResult(CognitoFake.User());
            await Throws<OperationCanceledException>(() => pending);
            Check(!session.IsSignedIn && !storage.Values.ContainsKey("session-v1"));
        }
    );
    await Run(
        "OAuth callback state/host/path/error validation (released plugin)",
        async () =>
        {
            var redirect = new Uri("fni-fixture://callback/complete");
            Check(
                CognitoAuthorization.ReadAuthorizationCode(
                    new("fni-fixture://callback/complete?state=s&code=c"),
                    redirect,
                    "s"
                ) == "c"
            );
            foreach (
                var callback in new[]
                {
                    "fni-fixture://other/complete?state=s&code=c",
                    "fni-fixture://callback/wrong?state=s&code=c",
                    "fni-fixture://callback/complete?state=wrong&code=c",
                    "fni-fixture://callback/complete?state=s",
                }
            )
                await Throws<InvalidDataException>(() =>
                    Task.FromResult(
                        CognitoAuthorization.ReadAuthorizationCode(new(callback), redirect, "s")
                    )
                );
            await Throws<InvalidOperationException>(() =>
                Task.FromResult(
                    CognitoAuthorization.ReadAuthorizationCode(
                        new("fni-fixture://callback/complete?state=s&error=access_denied"),
                        redirect,
                        "s"
                    )
                )
            );
        }
    );
    await Run(
        "reset wrong code retries without losing progress",
        async () =>
        {
            var auth = new FakeSession();
            var controller = new RecoveryController(auth, new InlineDispatcher());
            controller.Email = "test@example.com";
            await controller.RequestResetAsync(default);
            controller.Code = "123456";
            Check(controller.AdvanceToPassword());
            controller.Password = controller.Confirmation = "Fixture-password-1";
            auth.ConfirmReset = () =>
                Task.FromException(new InvalidOperationException("wrong code fixture"));
            Check(
                !await controller.ConfirmResetAsync(default)
                    && controller.State == RecoveryStep.Code
            );
            Check(controller.Code == "123456" && controller.Password.Length > 0);
            auth.ConfirmReset = () => Task.CompletedTask;
            Check(
                await controller.ConfirmResetAsync(default)
                    && controller.State == RecoveryStep.Confirmed
                    && controller.Password == ""
            );
        }
    );
    await Run(
        "reset initialization discards late response",
        async () =>
        {
            var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var auth = new FakeSession { ResetRequest = () => wait.Task };
            var controller = new RecoveryController(auth, new InlineDispatcher());
            controller.Email = "test@example.com";
            var request = controller.RequestResetAsync(default);
            controller.Initialize();
            wait.SetResult();
            await request;
            Check(controller.State == RecoveryStep.Initial && !controller.RequestSucceeded);
        }
    );
    await Run(
        "session logout preempts busy login and duplicate submit",
        async () =>
        {
            var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var auth = new FakeSession { SignIn = () => wait.Task };
            var notifier = new SessionNotifier(
                new InlineDispatcher(),
                auth,
                new FakeRegistration(),
                new FakeGames()
            );
            var first = notifier.SignInAsync("a", "p", default);
            await notifier.SignInAsync("a", "p", default);
            Check(auth.SignIns == 1);
            await notifier.SignOutAsync(default);
            wait.SetResult();
            await Throws<OperationCanceledException>(() => first);
            Check(notifier.State is SessionState.SignedOut);
        }
    );
    await Run(
        "onboarding upload failure retries same profile; draft survives restart",
        async () =>
        {
            var memory = new MemoryStorage();
            var drafts = new OnboardingDraftStore(memory);
            var profiles = new FakeProfile { FailUpload = true };
            var registration = new FakeRegistration();
            var identity = new RegistrationIdentity("test@example.com", "42", new());
            var controller = new OnboardingController(
                new InlineDispatcher(),
                profiles,
                new FakePlaces(),
                registration,
                new FakeGames(),
                drafts
            );
            controller.Initialize(identity);
            controller.FirstName = "A";
            controller.LastName = "B";
            controller.Nickname = "player";
            controller.NicknameAvailable = true;
            controller.SelectedPlace = new("Belgrade", 44, 20);
            controller.SetPhoto([1, 2, 3]);
            await Throws<HttpRequestException>(() => controller.SaveAsync(default));
            Check(profiles.Creates == 1);
            var restored = new OnboardingController(
                new InlineDispatcher(),
                profiles,
                new FakePlaces(),
                registration,
                new FakeGames(),
                drafts
            );
            restored.Initialize(identity);
            await restored.RestoreDraftAsync(default);
            Check(restored.FirstName == "A" && restored.CanSave);
            profiles.FailUpload = false;
            Check((await restored.SaveAsync(default))?.Id == "player" && profiles.Creates == 1);
            Check(await drafts.ReadAsync(identity, default) is null);
        }
    );
    await Run(
        "draft account isolation and nickname invalidation",
        async () =>
        {
            var drafts = new OnboardingDraftStore(new MemoryStorage());
            var controller = new OnboardingController(
                new InlineDispatcher(),
                new FakeProfile(),
                new FakePlaces(),
                new FakeRegistration(),
                new FakeGames(),
                drafts
            );
            var identity = new RegistrationIdentity("first@example.com", "42", new());
            controller.Initialize(identity);
            controller.Nickname = "old";
            controller.NicknameAvailable = true;
            controller.Nickname = "new";
            Check(!controller.NicknameAvailable);
            await controller.SaveDraftAsync(default);
            Check(
                await drafts.ReadAsync(identity with { Email = "second@example.com" }, default)
                    is null
            );
        }
    );
    await Run(
        "registration confirmation, resend and restart after login failure",
        async () =>
        {
            var storage = new MemoryStorage();
            var auth = new FakeSession();
            var backend = new RegistrationFake();
            var service = new RegistrationService(auth, backend, () => storage);
            var challenge = await service.BeginAsync(
                new("test@example.com", "Fixture-password-1", "Fixture-password-1", true, true),
                default
            );
            await service.ResendCodeAsync(challenge, default);
            Check(auth.Resends == 1);
            auth.WrongCode = true;
            await Throws<InvalidOperationException>(() =>
                service.ConfirmCodeAsync(challenge, "111111", default)
            );
            Check(!challenge.CodeConfirmed);
            auth.WrongCode = false;
            await service.ConfirmCodeAsync(challenge, "222222", default);
            Check(challenge.CodeConfirmed);
            auth.SignIn = () => Task.FromException(new HttpRequestException());
            await Throws<HttpRequestException>(() =>
                service.SignInAfterRegistrationAsync(challenge, default)
            );
            Check(backend.Creates == 0);
            var restarted = new RegistrationService(auth, backend, () => storage);
            var resumed = await restarted.ResumeConfirmationAsync(
                "test@example.com",
                "Fixture-password-1",
                default
            );
            Check(resumed is { CodeConfirmed: true });
            await restarted.ConfirmCodeAsync(resumed!, "", default);
            Check(auth.Confirmations == 2);
            Check(
                await restarted.ResumeConfirmationAsync(
                    "other@example.com",
                    "Fixture-password-1",
                    default
                )
                    is null
            );
            Check(!storage.Values.Values.Any(v => v.Contains("Fixture-password-1")));
            auth.SignIn = () => Task.CompletedTask;
            var identity = await restarted.SignInAfterRegistrationAsync(resumed!, default);
            Check(identity.TemporaryUserId == "42" && backend.Creates == 1);
            Check(
                await restarted.ResumeConfirmationAsync(
                    "test@example.com",
                    "Fixture-password-1",
                    default
                )
                    is null
            );
            auth.Email = "other@example.com";
            Check(await restarted.RestoreTemporaryUserAsync(default) is null);
        }
    );
    await Run(
        "registration resolver never opens home on failed restore lookup",
        async () =>
        {
            var backend = new RegistrationFake
            {
                Status = UserRegistrationStatus.RegisteredButNotInTempOrUsers,
                Fail = true,
            };
            var service = new RegistrationService(
                new FakeSession(),
                backend,
                () => new MemoryStorage()
            );
            await Throws<HttpRequestException>(() =>
                service.ResolveSignedInUserAsync(new("test@example.com"), null, true, default)
            );
        }
    );
    await Run(
        "session success routes registered and temporary accounts separately",
        async () =>
        {
            var auth = new FakeSession();
            var registration = new FakeRegistration();
            var notifier = new SessionNotifier(
                new InlineDispatcher(),
                auth,
                registration,
                new FakeGames()
            );
            await notifier.SignInAsync("test@example.com", "fixture", default);
            Check(notifier.State is SessionState.SignedIn);
            await notifier.SignOutAsync(default);
            registration.Identity = new("test@example.com", "42", new());
            await notifier.SignInAsync("test@example.com", "fixture", default);
            Check(notifier.State is SessionState.Onboarding);
            notifier.CompleteOnboarding(
                registration.Identity with
                {
                    Email = "other@example.com",
                },
                FakeGames.Profile
            );
            Check(notifier.State is SessionState.Onboarding);
            notifier.CompleteOnboarding(registration.Identity, FakeGames.Profile);
            Check(notifier.State is SessionState.SignedIn);
        }
    );
    await Run(
        "recovery restart remembers recipient but never code or password",
        async () =>
        {
            var memory = new MemoryStorage();
            var progress = new RecoveryProgressStore(memory);
            var auth = new FakeSession();
            var recovery = new RecoveryController(auth, new InlineDispatcher(), progress)
            {
                Email = "test@example.com",
            };
            await recovery.RequestResetAsync(default);
            recovery.Code = "123456";
            Check(recovery.AdvanceToPassword());
            recovery.Password = "fixture-secret";
            var restored = new RecoveryController(auth, new InlineDispatcher(), progress);
            Check(await restored.RestoreProgressAsync(default));
            Check(
                restored.Email == "test@example.com"
                    && restored.State == RecoveryStep.Reset
                    && restored.Code == ""
                    && restored.Password == ""
            );
            Check(
                !memory.Values.Values.Any(v => v.Contains("fixture-secret") || v.Contains("123456"))
            );
        }
    );
    await Run(
        "cancelled onboarding cannot clear progress or return profile",
        async () =>
        {
            var wait = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var profiles = new PendingProfile(wait.Task);
            var controller = new OnboardingController(
                new InlineDispatcher(),
                profiles,
                new FakePlaces(),
                new FakeRegistration(),
                new FakeGames()
            );
            controller.Initialize(new("test@example.com", "42", new()));
            controller.FirstName = "A";
            controller.LastName = "B";
            controller.Nickname = "player";
            controller.NicknameAvailable = true;
            controller.SelectedPlace = new("Belgrade", 44, 20);
            var request = controller.SaveAsync(default);
            controller.CancelPending();
            wait.SetResult("player");
            await Throws<OperationCanceledException>(() => request);
        }
    );
    Console.WriteLine("All Feature07 tests passed.");
});

static AuthenticationResultType Tokens() =>
    new()
    {
        AccessToken = "access-fixture",
        RefreshToken = "refresh-fixture",
        IdToken = "id-fixture",
        ExpiresIn = 3600,
    };
static void Check(bool value)
{
    if (!value)
        throw new Exception("Assertion failed");
}
static async Task Throws<T>(Func<Task> action)
    where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }
    throw new Exception("Expected " + typeof(T).Name);
}
static async Task Run(string name, Func<Task> test)
{
    await test();
    Console.WriteLine("PASS " + name);
}

sealed class InlineDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}

sealed class HttpFake : HttpMessageHandler
{
    public HttpResponseMessage Response = new(HttpStatusCode.OK);
    public Uri? Uri;
    public string? Auth;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        Uri = request.RequestUri;
        Auth = request.Headers.Authorization?.ToString();
        return Task.FromResult(Response);
    }
}

sealed class MemoryStorage : ISecureStorage
{
    public Dictionary<string, string> Values = new();

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.GetValueOrDefault(key));

    public Task WriteAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value is null)
            Values.Remove(key);
        else
            Values[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.ContainsKey(key));

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyDictionary<string, string>>(Values);

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        Values.Clear();
        return Task.CompletedTask;
    }
}

sealed class CognitoFake()
    : AmazonCognitoIdentityProviderClient(new AnonymousAWSCredentials(), RegionEndpoint.EUCentral1)
{
    public int Refreshes;
    public Exception? FailRefresh;
    public TaskCompletionSource<GetUserResponse>? UserWait;

    public static GetUserResponse User() =>
        new()
        {
            UserAttributes =
            [
                new() { Name = "email", Value = "test@example.com" },
                new() { Name = "sub", Value = "fixture-subject" },
            ],
        };

    public override Task<GetUserResponse> GetUserAsync(
        GetUserRequest request,
        CancellationToken cancellationToken = default
    ) => UserWait?.Task ?? Task.FromResult(User());

    public override Task<InitiateAuthResponse> InitiateAuthAsync(
        InitiateAuthRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Refreshes++;
        if (FailRefresh is not null)
            return Task.FromException<InitiateAuthResponse>(FailRefresh);
        return Task.FromResult(
            new InitiateAuthResponse
            {
                AuthenticationResult = new()
                {
                    AccessToken = "renewed-fixture",
                    IdToken = "id-renewed-fixture",
                    ExpiresIn = 3600,
                },
            }
        );
    }
}

sealed class TestPump : SynchronizationContext
{
    private readonly System.Collections.Concurrent.BlockingCollection<(
        SendOrPostCallback,
        object?
    )> queue = new();

    public override void Post(SendOrPostCallback callback, object? state) =>
        queue.Add((callback, state));

    public static void Run(Func<Task> action)
    {
        var previous = Current;
        var pump = new TestPump();
        SetSynchronizationContext(pump);
        try
        {
            var task = action();
            while (!task.IsCompleted)
                if (pump.queue.TryTake(out var item, 100))
                    item.Item1(item.Item2);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

sealed class RegistrationFake : IRegistrationApi
{
    public UserRegistrationStatus Status = UserRegistrationStatus.NotRegistered;
    public bool Fail;
    public int Creates;

    public Task<UserRegistrationStatus> CheckAsync(string email, CancellationToken token) =>
        Fail
            ? Task.FromException<UserRegistrationStatus>(new HttpRequestException())
            : Task.FromResult(Status);

    public Task<UserRegistrationStatus> CheckAuthenticatedAsync(
        string email,
        CancellationToken token
    ) => CheckAsync(email, token);

    public Task<string?> GetTemporaryUserIdAsync(string email, CancellationToken token) =>
        Task.FromResult<string?>(null);

    public Task<string?> CreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    )
    {
        Creates++;
        return Task.FromResult<string?>("42");
    }
}

sealed class PendingProfile(Task<string> result) : IProfileService
{
    public Task<bool> IsNicknameAvailableAsync(string nickname, CancellationToken token) =>
        Task.FromResult(true);

    public Task<string> CreateAsync(NewProfile profile, CancellationToken token) => result;

    public Task UploadPhotoAsync(string userId, byte[] photo, CancellationToken token) =>
        Task.CompletedTask;
}
