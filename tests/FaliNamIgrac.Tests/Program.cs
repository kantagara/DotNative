using System.Collections.Concurrent;
using DotNative;
using DotNative.Cognito;
using DotNative.Plugins;
using DotNative.Svg;
using DotNative.Toast;
using FaliNamIgrac;

// Always use a synthetic endpoint; HTTP/socket tests supply in-memory transports.
Environment.SetEnvironmentVariable(
    "FNI_CHAT_URL",
    "https://fixture.appsync-api.example.invalid/graphql"
);

Run(
    "Direct plugin APIs, picker host isolation and HTTP cancellation",
    () => DirectServicesTests.RunAsync().GetAwaiter().GetResult()
);
Run(
    "Chat HTTP, AppSync realtime protocol and account contracts",
    () => ChatTransportTests.RunAsync().GetAwaiter().GetResult()
);
Run("Profile, language, chat drafts, retry and stale responses", ProfileChatTests.Run);
Run("Durable chat restart, account isolation, failed writes and media cache", MediaCacheTests.Run);
Run("Session union, auth exclusion, failure and restore", Session);
Run("Catalog isolation and stale response after sign-out", Catalog);
Run(
    "My appointments HTTP contract and mapping",
    () => MyGamesApiTests.RunAsync().GetAwaiter().GetResult()
);
Run("My appointments filters, failure, queued load and session isolation", MyAppointments);
Run("Onboarding saves a profile without loading games", Onboarding);
Run("Real screen builds isolate login, catalog and registration", Screens);
Run("Profile navigation, admin visibility, sports, chat list and conversation render", NewScreens);
Run("Root watches session, not forms or catalog", Root);
Run("Outgoing route retains the live component", Routes);
Run("Orbit frames update only six image transforms", OrbitFrames);
Run("Chat warms without blocking home and clears on sign-out", PreloadHome);
Run("Rating route renders eligible targets and replaces star selection", RatingScreen);

static void RatingScreen()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.Android);
    f.Presentation.Dimensions.Value = (375, 812);
    f.SignIn();
    const string gameId = "abcdefab-1234-1234-1234-abcdefabcdef";
    int? current = 42;
    using var model = new MyAppointmentsNotifier(f.Dispatcher, new RatingService(), () => current);
    f.Dispatcher.Finish(model.OpenAsync(gameId, default));
    var actions = new AppointmentActions(model, new RatingText(), new(_ => { }, _ => { }), default);
    var screen = new GameDetailsScreen(ui, f.Catalog, actions);
    f.Catalog.ShowResolvedDetails(new(FakeGames.Game with { Id = gameId }, []), false);
    screen.OpenRatings(gameId);
    static IEnumerable<Element> Walk(Element element)
    {
        yield return element;
        if (element is ContainerNode container)
            foreach (var child in container.Children)
            foreach (var value in Walk(child))
                yield return value;
    }
    Button[] Stars() =>
        Walk(screen.Build()).OfType<Button>().Where(b => b.Title is "\ue838" or "\ue83a").ToArray();
    Require(Stars().Length == 5, "Only one eligible other participant should be offered");
    Stars()[1].OnClick();
    Stars()[4].OnClick();
    Walk(screen.Build()).OfType<Button>().Single(b => b.Title == ui.Text.AppointmentRate).OnClick();
    Require(
        model.State.Confirmation?.Ratings is [{ RatedUserId: 7, Rating: 5 }],
        "Changed stars lost the rating"
    );
    current = null;
    Require(Stars().Length == 0, "Logged-out user retained rating controls");
}

static void PreloadHome()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.Android);
    f.Presentation.Dimensions.Value = (375, 812);
    f.SignIn();
    var pending = new TaskCompletionSource<IReadOnlyList<ChatRoom>>();
    var api = new FakeChat { Rooms = _ => pending.Task };
    using var home = new HomeScreen(ui, f.Catalog, f.Session, new FakeAccount(), api);
    Require(
        api.ListCalls == 1 && !home.PreparingHome.Value,
        "Chat preload blocked home or did not start"
    );
    pending.SetResult([FakeChat.Room]);
    f.Dispatcher.Drain();
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var chat = (ChatNotifier)typeof(HomeScreen).GetField("chat", flags)!.GetValue(home)!;
    Require(
        chat.State.Rooms?.Count == 1 && !home.PreparingHome.Value,
        "Background chat preload did not populate the inbox"
    );
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    Require(
        chat.State.Rooms is null && !home.PreparingHome.Value,
        "Signed-out user retained inbox"
    );
}

static void Run(string name, Action test)
{
    test();
    Console.WriteLine("PASS " + name);
}
static void Require(bool condition, string message = "Assertion failed")
{
    if (!condition)
        throw new Exception(message);
}

static void Session()
{
    using var f = new Fixture();
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Auth.SignIn = () => completion.Task;
    var first = f.Session.SignInAsync("one@example.com", "secret", default);
    Require(f.Session.Busy && f.Session.State is SessionState.SignedOut);
    f.Session.SignInAsync("two@example.com", "secret", default).GetAwaiter().GetResult();
    Require(f.Auth.SignIns == 1, "A second sign-in waits for the first");
    Task.Run(() => completion.SetResult()).GetAwaiter().GetResult();
    f.Dispatcher.Finish(first);
    Require(f.Session.State is SessionState.SignedIn && !f.Session.Busy);
    Require(f.Api.ListCalls == 0, "Session cannot load catalog");
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    Require(f.Session.State is SessionState.SignedOut);
    f.Registration.Identity = new("new@example.com", "42", new(GivenName: "New"));
    f.Auth.SignIn = () => Task.CompletedTask;
    f.Dispatcher.Finish(f.Session.SignInAsync("new@example.com", "secret", default));
    Require(f.Session.State is SessionState.Onboarding && f.Api.ProfileCalls == 1);
    f.Auth.SignIn = () => Task.FromException(new InvalidOperationException("failure"));
    try
    {
        f.Dispatcher.Finish(f.Session.SignInAsync("bad", "bad", default));
        throw new Exception("Expected failure");
    }
    catch (InvalidOperationException) { }
    Require(!f.Session.Busy);
    f.Dispatcher.Finish(f.Session.RestoreAsync(default));
    Require(f.Session.State is SessionState.Onboarding && f.Api.ListCalls == 0);
}

static void Catalog()
{
    using var f = new Fixture();
    f.SignIn();
    var signedIn = f.Session.State;
    f.Dispatcher.Finish(f.Catalog.LoadAsync(2, 3, default));
    Require(ReferenceEquals(signedIn, f.Session.State));
    Require(f.Catalog.State is { Sport: 2, Page: 3, Games.Count: 1 });
    f.Dispatcher.Finish(f.Catalog.OpenDetailsAsync("game", default));
    f.Api.FailDetails = true;
    f.Dispatcher.Finish(f.Catalog.ApplyAsync(default));
    Require(
        f.Catalog.State.Applied
            && f.Catalog.State.Message == f.Presentation.Text.ApplicationDetailsStale
    );
    f.Catalog.BackToList();
    Require(f.Catalog.State.Details is null);
    var stale = new TaskCompletionSource<IReadOnlyList<Game>>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    f.Api.List = () => stale.Task;
    var request = f.Catalog.LoadAsync(7, 1, default);
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    f.Catalog.SynchronizeSession(default);
    Require(f.Catalog.State.Games is null);
    // Another login while the old request is still in flight must get its own initial load.
    f.SignIn(FakeGames.Profile with { Id = "second" });
    f.Catalog.SynchronizeSession(default);
    f.Api.List = () => Task.FromResult<IReadOnlyList<Game>>([FakeGames.Game with { Id = "fresh" }]);
    stale.SetResult([FakeGames.Game with { Id = "stale" }]);
    f.Dispatcher.Finish(request);
    Require(
        f.Catalog.State.Games is null,
        "Old account response must be discarded; feed is owned by CatalogSearchNotifier."
    );
    f.Dispatcher.Finish(f.Catalog.LoadAsync(0, 1, default));
    Require(f.Catalog.State.Games?[0].Id == "fresh" && f.Catalog.State.Sport == 0);
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    f.Catalog.SynchronizeSession(default);
    f.Dispatcher.Finish(f.Catalog.LoadAsync(0, 1, default));
    Require(f.Catalog.State.Games is null && f.Catalog.State.Details is null);
}

static void MyAppointments()
{
    using var f = new Fixture();
    f.SignIn();
    f.Dispatcher.Finish(f.Catalog.LoadAsync(2, 1, default));
    var available = f.Catalog.State.Games;
    f.Dispatcher.Finish(f.Catalog.LoadMyGamesAsync(3, 2, true, default));
    Require(f.Api.MyRequest == ("player", 3, 2, true));
    Require(f.Catalog.State is { MySport: 3, MyPage: 2, MyPassed: true, MyGames.Count: 1 });
    Require(
        ReferenceEquals(available, f.Catalog.State.Games),
        "My appointments replaced available games"
    );
    f.Dispatcher.Finish(f.Catalog.OpenDetailsAsync("game", default, canApply: false));
    f.Dispatcher.Finish(f.Catalog.ApplyAsync(default));
    Require(f.Api.ApplyCalls == 0, "My appointments must not submit another application");
    f.Catalog.BackToList();
    f.Api.List = () => Task.FromException<IReadOnlyList<Game>>(new HttpRequestException());
    f.Dispatcher.Finish(f.Catalog.LoadMyGamesAsync(4, 1, false, default));
    Require(f.Catalog.State.MyGames is null && f.Catalog.State.Message.Length > 0);
    Require(f.Catalog.State is { MySport: 4, MyPassed: false }, "Failed filter kept stale rows");

    var pending = new TaskCompletionSource<IReadOnlyList<Game>>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    f.Api.List = () => pending.Task;
    var request = f.Catalog.LoadMyGamesAsync(4, 1, false, default);
    f.Catalog.LoadMyGamesAsync(5, 1, true, default).GetAwaiter().GetResult();
    f.Api.List = () => Task.FromResult<IReadOnlyList<Game>>([]);
    pending.SetResult([FakeGames.Game]);
    f.Dispatcher.Finish(request);
    Require(f.Api.MyRequest == ("player", 5, 1, true));
    Require(f.Catalog.State is { MyGames.Count: 0, MySport: 5, MyPassed: true });

    pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Api.List = () => pending.Task;
    request = f.Catalog.LoadMyGamesAsync(5, 1, true, default);
    f.Catalog.OpenDetailsAsync("game", default, canApply: false).GetAwaiter().GetResult();
    pending.SetResult([FakeGames.Game]);
    f.Dispatcher.Finish(request);
    Require(
        f.Catalog.State.Details?.Game.Id == "game",
        "Details tap during a list refresh was lost"
    );
    f.Catalog.BackToList();

    pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Api.List = () => pending.Task;
    request = f.Catalog.LoadMyGamesAsync(5, 1, true, default);
    f.Catalog.LoadMyGamesAsync(6, 1, false, default).GetAwaiter().GetResult();
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    f.Catalog.SynchronizeSession(default);
    pending.SetResult([FakeGames.Game]);
    f.Dispatcher.Finish(request);
    Require(f.Catalog.State.MyGames is null && f.Catalog.State.MySport == 0);
    Require(f.Api.MyRequest.Sport == 5, "Queued request survived sign-out");
}

static void Onboarding()
{
    using var f = new Fixture();
    var onboarding = new OnboardingController(
        f.Dispatcher,
        new FakeProfile(),
        new FakePlaces(),
        f.Registration,
        f.Api
    );
    onboarding.Initialize(new("test@example.com", "42", new(GivenName: "A", FamilyName: "B")));
    onboarding.Nickname = "player";
    onboarding.NicknameAvailable = true;
    onboarding.SelectedPlace = new("Belgrade", 44, 20);
    var save = onboarding.SaveAsync(default);
    f.Dispatcher.Finish(save);
    Require(save.Result == FakeGames.Profile && f.Api.ListCalls == 0);
    Require(f.Session.State is SessionState.SignedOut && !onboarding.Busy);
}

static void OrbitFrames()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.IOS);
    f.Presentation.Dimensions.Value = (375, 812);
    var landing = new LandingScreen(ui);
    var root = new Probe(landing.Build);
    var sink = new OrbitSink();
    using var host = new RenderHost(root, f.Dispatcher, sink);
    host.RenderNow();
    sink.Operations.Clear();
    ui.OrbitAnimation.SnapTo(0.1f);
    f.Dispatcher.Drain();
    Require(root.Builds == 1, "Orbit rebuilt the entire landing screen");
    Require(
        sink.Operations.Count == 6 && sink.Operations.All(op => op == 17),
        "Orbit frame emitted layout/image commands instead of six transforms"
    );
}

static void Screens()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.IOS);
    f.Presentation.Dimensions.Value = (375, 812);
    f.SignIn();
    f.Dispatcher.Finish(f.Catalog.LoadAsync(0, 1, default));
    var login = new LoginScreen(ui, f.Login, f.Session);
    var home = new HomeScreen(ui, f.Catalog, f.Session, new FakeAccount(), new FakeChat());
    var registration = new RegistrationScreen(ui, f.Form);
    var loginProbe = new Probe(login.Build);
    var homeProbe = new Probe(home.Build);
    var registrationProbe = new Probe(registration.Build);
    var root = new Probe(() => new VStack(loginProbe, homeProbe, registrationProbe));
    using var host = new RenderHost(root, f.Dispatcher, new Sink());
    host.RenderNow();
    f.Login.Email = "typed@example.com";
    f.Dispatcher.Drain();
    Require(
        loginProbe.Builds == 2
            && homeProbe.Builds == 1
            && registrationProbe.Builds == 1
            && root.Builds == 1
    );
    f.Dispatcher.Finish(f.Catalog.LoadAsync(2, 1, default));
    Require(homeProbe.Builds == 2 && loginProbe.Builds == 2 && registrationProbe.Builds == 1);
    f.Form.SetEmail("registration@example.com");
    f.Dispatcher.Drain();
    Require(registrationProbe.Builds == 2 && loginProbe.Builds == 2 && homeProbe.Builds == 2);
}

static void NewScreens()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.IOS);
    f.Presentation.Dimensions.Value = (375, 812);
    f.SignIn();
    using var home = new HomeScreen(ui, f.Catalog, f.Session, new FakeAccount(), new FakeChat());
    using var host = new RenderHost(new Probe(home.Build), f.Dispatcher, new Sink());
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var selectTab = typeof(HomeScreen).GetMethod("SelectTab", flags)!;
    selectTab.Invoke(home, [3]);
    host.RenderNow();
    static IEnumerable<Element> Walk(Element element)
    {
        yield return element;
        if (element is ContainerNode container)
            foreach (var child in container.Children)
            foreach (var item in Walk(child))
                yield return item;
    }
    var isAdmin = new State<bool>(false);
    home.ProfileAdminAccess = () => isAdmin.Value;
    var adminOpened = false;
    home.OpenModeration = () => adminOpened = true;
    Require(
        !Walk(home.Build())
            .OfType<Text>()
            .Any(text => text.Value == ui.Text.Reporting08ReportsTitle)
    );
    isAdmin.Value = true;
    host.RenderNow();
    var overview = Walk(home.Build()).ToArray();
    Require(
        !overview.OfType<Text>().Any(text => text.Value == ui.Text.ProfileSettings),
        "Redundant Settings entry returned"
    );
    var adminRow = overview
        .OfType<ContainerNode>()
        .Single(node =>
            node.Children.OfType<Text>().Any(text => text.Value == ui.Text.Reporting08ReportsTitle)
        );
    adminRow.Children.OfType<Button>().Single().OnClick();
    Require(adminOpened, "Admin menu did not open moderation");
    isAdmin.Value = false;
    Require(
        !Walk(home.Build())
            .OfType<Text>()
            .Any(text => text.Value == ui.Text.Reporting08ReportsTitle),
        "Admin menu survived revoked visibility"
    );
    var page = typeof(HomeScreen).GetField("profilePage", flags)!.GetValue(home)!;
    var value = page.GetType().GetProperty("Value")!;
    foreach (var name in new[] { "Edit", "Sports", "Overview" })
    {
        value.SetValue(page, Enum.Parse(value.PropertyType, name));
        f.Dispatcher.Drain();
        host.RenderNow();
    }
    selectTab.Invoke(home, [2]);
    f.Dispatcher.Drain();
    host.RenderNow();
    var chat = (ChatNotifier)typeof(HomeScreen).GetField("chat", flags)!.GetValue(home)!;
    f.Dispatcher.Finish(chat.OpenAsync(FakeChat.Room, ui.Lifetime.Token));
    host.RenderNow();
    f.Dispatcher.Finish(chat.SendAsync(default));
    chat.SetDraft("Hello");
    f.Dispatcher.Finish(chat.SendAsync(default));
    host.RenderNow();
    chat.SearchUsers();
    host.RenderNow();
    chat.SearchUsers(true);
    chat.SetGroupName("Team");
    chat.ToggleGroupUser(new("other", "Other", "Player"));
    host.RenderNow();
    f.Dispatcher.Finish(chat.CreateGroupAsync(ui.Lifetime.Token));
    host.RenderNow();
    var info = (State<bool>)typeof(HomeScreen).GetField("showChatInfo", flags)!.GetValue(home)!;
    info.Value = true;
    host.RenderNow();
    selectTab.Invoke(home, [3]);
    f.Dispatcher.Drain();
}

static void Root()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.Linux);
    var recovery = new RecoveryController(f.Auth, f.Dispatcher);
    var onboarding = new OnboardingController(
        f.Dispatcher,
        new FakeProfile(),
        new FakePlaces(),
        f.Registration,
        f.Api
    );
    var routes = new AppRoutes(
        ui,
        f.Session,
        f.Catalog,
        f.Login,
        f.Form,
        onboarding,
        recovery,
        new FakeAccount(),
        new FakeChat()
    );
    using var app = new App(ui, f.Router, f.Session, f.Form, routes);
    var root = new Probe(app.Build);
    using var host = new RenderHost(root, f.Dispatcher, new Sink());
    host.RenderNow();
    f.Dispatcher.Drain();
    f.Login.Email = "test@example.com";
    f.Form.SetEmail("new@example.com");
    f.Dispatcher.Drain();
    Require(root.Builds == 1);
    f.SignIn();
    f.Dispatcher.Drain();
    Require(f.Api.ListCalls == 0 && root.Builds == 2);
    f.Dispatcher.Finish(f.Catalog.LoadAsync(2, 1, default));
    Require(root.Builds == 2);
    f.Dispatcher.Finish(f.Session.SignOutAsync(default));
    Require(f.Catalog.State.Games is null);
}

static void Routes()
{
    using var f = new Fixture();
    using var ui = f.Ui(NativePlatform.IOS);
    f.Presentation.Dimensions.Value = (375, 812);
    var splash = new RouteProbe(ui);
    var login = new RouteProbe(ui);
    using var routes = new RouteHost(
        ui,
        f.Session,
        f.Catalog,
        new Dictionary<ApplicationScreen, ScreenComponent>
        {
            [ApplicationScreen.Splash] = splash,
            [ApplicationScreen.Login] = login,
        },
        new HomeScreen(ui, f.Catalog, f.Session, new FakeAccount(), new FakeChat()),
        new GameDetailsScreen(ui, f.Catalog)
    );
    using var host = new RenderHost(routes, f.Dispatcher, new Sink());
    host.RenderNow();
    f.Presentation.CurrentScreen.Value = ApplicationScreen.Login;
    f.Dispatcher.Drain();
    Require(routes.Transitioning && splash.Leaves == 1 && login.Builds == 1);
    var before = splash.Builds;
    splash.Text.Value = "outgoing is live";
    f.Dispatcher.Drain();
    Require(splash.Builds == before + 1, "Outgoing must remain a mounted reactive component");
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (routes.Transitioning && DateTime.UtcNow < deadline)
    {
        f.Dispatcher.Drain();
        Thread.Sleep(5);
    }
    Require(!routes.Transitioning, "Outgoing route must unmount after the transition");
    before = splash.Builds;
    splash.Text.Value = "unmounted";
    f.Dispatcher.Drain();
    Require(splash.Builds == before);
}

sealed class Fixture : IDisposable
{
    public Dispatcher Dispatcher { get; } = new();
    public FakeSession Auth { get; } = new();
    public FakeRegistration Registration { get; } = new();
    public FakeGames Api { get; } = new();
    public PresentationState Presentation { get; } = new();
    public PlatformChannels Channels { get; }
    public SessionNotifier Session { get; }
    public CatalogNotifier Catalog { get; }
    public LoginFormNotifier Login { get; }
    public RegistrationFormNotifier Form { get; }
    public RecoveryController Recovery { get; }
    public AuthRouter Router { get; }

    public void SignIn(PlayerProfile? profile = null)
    {
        Api.CurrentProfile = profile ?? FakeGames.Profile;
        Registration.Identity = null;
        Dispatcher.Finish(Session.SignInAsync("fixture@example.com", "fixture", default));
    }

    public Fixture()
    {
        Channels = new(Dispatcher, null);
        Session = new(Dispatcher, Auth, Registration, Api);
        Catalog = new(Dispatcher, Session, Api, Presentation);
        Login = new(Dispatcher);
        Form = new(Dispatcher, Registration, Session);
        Recovery = new(Auth, Dispatcher);
        Router = new(
            Presentation,
            Session,
            Auth,
            new SocialAuthentication(new UnusedHostedUi()),
            new SilentToast(),
            Login,
            Form,
            Recovery
        );
    }

    public UiContext Ui(NativePlatform platform) =>
        new(
            Presentation,
            new FakeTheme(),
            Dispatcher,
            new(platform),
            Channels,
            null!,
            new SilentToast(),
            Router
        );

    public void Dispose() => Channels.Dispose();
}

sealed class SilentToast : IToast
{
    public Task ShowAsync(string message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

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

sealed class Sink : ICommandTransport
{
    public void Flush(ReadOnlySpan<byte> buffer) { }
}

sealed class Probe(Func<Element> build) : Component
{
    public int Builds { get; private set; }

    public override Element Build()
    {
        Builds++;
        return build();
    }
}

sealed class RouteProbe(UiContext ui) : ScreenComponent(ui)
{
    public State<string> Text { get; } = new("route");
    public int Builds { get; private set; }
    public int Leaves { get; private set; }

    public override Element Build()
    {
        Builds++;
        return new Text(Text.Value);
    }

    public override void Leave() => Leaves++;
}

sealed class FakeTheme : IThemeController
{
    public State<IThemeData> Current { get; } = new(ThemeData.Light);

    public Task LoadAsync(CancellationToken token) => Task.CompletedTask;

    public Task SetAsync(ThemeMode mode, CancellationToken token) => Task.CompletedTask;
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
    public RegistrationIdentity? Identity;

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
    public bool FailDetails;

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

sealed class FakeProfile : IProfileService
{
    public Task<bool> IsNicknameAvailableAsync(string nickname, CancellationToken token) =>
        Task.FromResult(true);

    public Task<string> CreateAsync(NewProfile profile, CancellationToken token) =>
        Task.FromResult("player");

    public Task UploadPhotoAsync(string userId, byte[] photo, CancellationToken token) =>
        Task.CompletedTask;
}

sealed class FakePlaces : IPlacesService
{
    public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string text, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<PlaceSuggestion>>([]);

    public Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token) =>
        Task.FromResult(new SelectedPlace("Belgrade", 44, 20));
}

sealed class UnusedHostedUi : ICognitoHostedUi
{
    public Task<CognitoTokens> SignInAsync(
        CognitoProvider provider,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Social sign-in is not part of this test.");
}

sealed class OrbitSink : ICommandTransport
{
    public List<byte> Operations { get; } = [];

    public void Flush(ReadOnlySpan<byte> buffer)
    {
        var cursor = 16;
        while (cursor < buffer.Length)
        {
            Operations.Add(buffer[cursor]);
            cursor +=
                5
                + checked(
                    (int)
                        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                            buffer[(cursor + 1)..]
                        )
                );
        }
    }
}

sealed class RatingService : IMyAppointmentsService
{
    public Task<AppointmentSnapshot> LoadAsync(string id, int user, CancellationToken token) =>
        Task.FromResult(
            new AppointmentSnapshot(
                id,
                42,
                "Organizer",
                new DateTime(2020, 1, 1),
                TimeSpan.Zero,
                TimeSpan.FromHours(1),
                1,
                false,
                false,
                [new(7, "Accepted", "ACCEPTED"), new(8, "Pending", "PENDING")]
            )
        );

    public Task ExecuteAsync(
        string id,
        int user,
        AppointmentCommand command,
        CancellationToken token
    ) => throw new Exception("Rendering must not submit ratings");
}

sealed class RatingText : IAppointmentText
{
    public string Action(AppointmentAction value) => value.ToString();

    public string Status(AppointmentRole value) => value.ToString();

    public string Confirm(AppointmentAction value) => value.ToString();

    public string Error(AppointmentProblem value) => value.ToString();

    public string Loading => "Loading";
    public string Retry => "Retry";
    public string Cancel => "Cancel";
    public string ConfirmButton => "Confirm";
    public string Edit => "Edit";
    public string Chat => "Chat";
    public string Deleted => "Deleted";
    public string Rated => "Rated";
}
