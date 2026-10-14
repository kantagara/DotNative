using DotNative;

namespace FaliNamIgrac;

// One route slot with a retained outgoing occurrence; no parallel navigation stack.
public sealed class RouteHost : ScreenComponent, IDisposable
{
    private readonly SessionNotifier session;
    private readonly CatalogNotifier catalog;
    private readonly IReadOnlyDictionary<ApplicationScreen, ScreenComponent> auth;
    private readonly HomeScreen home;
    private readonly GameDetailsScreen details;
    private readonly Action<RegistrationIdentity>? beginOnboarding;
    private readonly IDisposable sessionFollow;
    private readonly AnimationController pageAnimation;
    private SessionState? followed;
    private ScreenComponent? current;
    private string? currentKey;
    private (ScreenComponent Screen, string Key, bool Back)? outgoing;
    private ApplicationScreen lastAuthPage;
    private readonly State<int> completion = new(0);
    private int navigationRevision;

    private readonly State<ScreenComponent?> feature = new(null);
    private int featureSerial;
    public Func<ScreenComponent?>? Guard { get; set; }
    public GameDetailsScreen Details => details;
    public bool CanShowAppOpen =>
        feature.Value is null && catalog.State.Details is null && !Transitioning;

    public void OpenFeature(ScreenComponent screen)
    {
        CloseFeature();
        featureSerial++;
        feature.Value = screen;
    }

    public void CloseFeature()
    {
        feature.Value?.Leave();
        feature.Value = null;
    }

    public void LeaveCurrent() => current?.Leave();

    public bool Transitioning => outgoing is not null;

    public RouteHost(
        UiContext ui,
        SessionNotifier session,
        CatalogNotifier catalog,
        IReadOnlyDictionary<ApplicationScreen, ScreenComponent> auth,
        HomeScreen home,
        GameDetailsScreen details,
        Action<RegistrationIdentity>? beginOnboarding = null
    )
        : base(ui)
    {
        this.session = session;
        this.catalog = catalog;
        this.auth = auth;
        this.home = home;
        this.details = details;
        this.beginOnboarding = beginOnboarding;
        pageAnimation = new(ui.Dispatcher);
        sessionFollow = session.Listen(FollowSession);
    }

    // The host stays mounted for the whole session, so it applies sign-in, onboarding, and sign-out.
    private void FollowSession()
    {
        if (disposed || followed == session.State)
            return;
        var previous = followed;
        followed = session.State;
        catalog.SynchronizeSession(lifetime.Token);
        switch (session.State)
        {
            case SessionState.SignedIn:
                break;
            case SessionState.Onboarding value:
                beginOnboarding?.Invoke(value.Identity);
                presentation.CurrentScreen.Value = ApplicationScreen.Onboarding;
                break;
            case SessionState.SignedOut when previous is not null:
                presentation.CurrentScreen.Value = ApplicationScreen.Landing;
                break;
        }
    }

    private (ScreenComponent Screen, string Key) SelectRoute()
    {
        if (session.State is SessionState.SignedIn && Guard?.Invoke() is { } guard)
            return (guard, "SessionGuard");
        if (
            session.State is SessionState.SignedIn
            && feature.Value is { } featurePage
            && featurePage is not (GameEditorScreen or GameSavedPopup)
        )
            return (featurePage, "Feature:" + featureSerial);
        if (session.State is SessionState.SignedIn)
            return catalog.State.Details is { } value
                ? (details, "Game:" + value.Game.Id)
                : (home, "Home");
        var page = presentation.CurrentScreen.Value;
        return (auth[page], page.ToString());
    }

    public override Element Build()
    {
        _ = completion.Value;
        var page = BuildPageTransition();
        return new VStack([
            page,
            .. session.State is SessionState.SignedIn
            && feature.Value is { } dialog
            && dialog is GameEditorScreen or GameSavedPopup
                ? new Element[]
                {
                    new VStack(dialog)
                        .WidthPercent(100)
                        .HeightPercent(100)
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0)
                        .WithKey("appointment-modal"),
                }
                : [],
        ])
            .WidthPercent(100)
            .HeightPercent(100);
    }

    private async Task AnimatePageAsync(int revision)
    {
        try
        {
            await pageAnimation.AnimateToAsync(
                1,
                TimeSpan.FromMilliseconds(target.Platform == NativePlatform.IOS ? 500 : 450),
                cancellationToken: lifetime.Token
            );
            await dispatcher.InvokeAsync(() =>
            {
                if (!disposed && revision == navigationRevision)
                {
                    outgoing = null;
                    completion.Value++;
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        CloseFeature();
        home.Dispose();
        sessionFollow.Dispose();
        pageAnimation.Dispose();
    }

    private Element BuildPageTransition()
    {
        var selected = SelectRoute();
        var starting = false;
        if (currentKey != selected.Key)
        {
            var prior = current;
            if (prior is not null)
            {
                starting = true;
                var back =
                    selected.Key == "Home"
                        ? currentKey!.StartsWith("Game:")
                        : currentKey!.StartsWith("Game:")
                            || currentKey == "Home"
                            || lastAuthPage != ApplicationScreen.Splash
                                && (
                                    selected.Key == "Landing"
                                    || (int)presentation.CurrentScreen.Value < (int)lastAuthPage
                                );
                outgoing = (prior, currentKey!, back);
                var revision = ++navigationRevision;
                dispatcher.Post(() =>
                {
                    if (disposed || revision != navigationRevision)
                        return;
                    prior.Leave();
                    pageAnimation.SnapTo(0);
                    _ = AnimatePageAsync(revision);
                });
            }
            current = selected.Screen;
            currentKey = selected.Key;
            lastAuthPage = presentation.CurrentScreen.Value;
        }
        var child = current!;
        if (presentation.Dimensions.Value is not { } size)
            return child;
        VStack Layer(Element child, string key, float offset, float opacity) =>
            new VStack(child)
                .Width(size.Width)
                .Height(size.Height)
                .Translate(offset * size.Width, 0)
                .Opacity(opacity)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .WithKey("route:" + key);
        if (outgoing is not { } from)
            return new VStack(Layer(child, currentKey!, 0, 1))
                .Width(size.Width)
                .Height(size.Height)
                .BackgroundColor(theme.Background)
                .ClipToBounds(true);
        var ios = target.Platform == NativePlatform.IOS;
        var progress = pageAnimation.Value; // Always subscribe, including the first transition frame.
        var frame = PageMotion.Evaluate(starting ? 0 : progress, from.Back, ios);
        var previous = Layer(from.Screen, from.Key, frame.OutgoingX, frame.OutgoingOpacity);
        var incoming = Layer(child, currentKey!, frame.IncomingX, frame.IncomingOpacity);
        var shadow = new VStack()
            .Width(size.Width * .05f)
            .Height(size.Height)
            .BackgroundGradient(
                Gradient.Linear(
                    new(0, 0),
                    new(1, 0),
                    new(0, new Color(0, 0, 0, 0)),
                    new(1, new Color(0, 0, 0, (byte)Math.Round(4 * frame.Shadow)))
                )
            )
            .Position(PositionMode.Absolute)
            .Left(((from.Back ? frame.OutgoingX : frame.IncomingX) - .05f) * size.Width)
            .Top(0);
        return new VStack([
            .. from.Back
                ? new Element[] { incoming, shadow, previous }
                : [previous, shadow, incoming],
            // As in an active route transition, both screens ignore new navigation taps.
            new Button("", () => { })
                .Width(size.Width)
                .Height(size.Height)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .WithKey("route-transition-barrier"),
        ]).Width(size.Width).Height(size.Height).BackgroundColor(theme.Background).ClipToBounds(true);
    }
}
