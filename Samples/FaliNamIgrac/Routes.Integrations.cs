using DotNative;
using DotNative.PdfView;

namespace FaliNamIgrac;

public sealed partial class AppRoutes
{
    private MobileAdPlacements? mobileAds;

    public void ConnectAds(
        DotNative.GoogleMobileAds.IPlacementMobileAds sdk,
        DotNative.GoogleMobileAds.IAdConsent consent,
        IApplicationLifecycle activity
    )
    {
        mobileAds = new(
            ui,
            session,
            sdk,
            consent,
            activity,
            () => !home.PreparingHome.Value && Host.CanShowAppOpen && home.CanShowAppOpen
        );
        home.FeedAd = mobileAds.Banner;
        home.OpenAdPrivacy = () => _ = mobileAds.ShowPrivacyAsync();
        Host.Details.Advertisement = () => mobileAds.Banner(6);
    }

    private DeviceTokenNotifier? deviceTokens;
    private ReportingSession? reportingSession;
    private BanGuardNotifier? banGuard;
    private AdminAccessNotifier? admin;
    private PlatformActionsNotifier? platformActions;
    private IApplicationLifecycle? lifecycle;
    private Action<ApplicationActivity>? activityFollow;
    private string? notificationUser;
    private long notificationEpoch;

    public void ConnectNotifications(
        ISessionService sessions,
        INotificationDevice device,
        HttpClient http,
        IApplicationLifecycle activity
    )
    {
        deviceTokens = new(ui.Dispatcher, device, new DeviceTokenApi(http));
        async Task FollowAsync(bool resume = false)
        {
            var user = UserId();
            var epoch = notificationEpoch;
            try
            {
                if (user is null)
                {
                    await deviceTokens.SetAccountAsync(null, ui.Lifetime.Token);
                    return;
                }
                var bearer = await sessions.AccessTokenAsync(ui.Lifetime.Token);
                Task pending = Task.CompletedTask;
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch == notificationEpoch && user == UserId())
                        pending = deviceTokens.SetAccountAsync(
                            new(user.Value, bearer),
                            ui.Lifetime.Token
                        );
                });
                await pending;
                if (resume && epoch == notificationEpoch)
                {
                    await ui.Dispatcher.InvokeAsync(() =>
                        pending = deviceTokens.ResumeAsync(ui.Lifetime.Token)
                    );
                    await pending;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch == notificationEpoch)
                        catalog.ReportError(FniErrors.UserMessage(ui.Text, error));
                });
            }
        }
        subscriptions.Add(
            session.Listen(() =>
            {
                var current = (session.State as SessionState.SignedIn)?.Profile.Id;
                if (notificationUser == current)
                    return;
                notificationUser = current;
                notificationEpoch++;
                _ = FollowAsync();
            })
        );
        lifecycle = activity;
        activityFollow = state =>
            ui.Dispatcher.Post(() =>
            {
                if (state == ApplicationActivity.Active)
                    _ = FollowAsync(true);
            });
        lifecycle.Changed += activityFollow;
        home.ProfileStatus = IntegrationStatus;
        // The released Firebase stream loses event kind/data; never interpret receive as a tap.
    }

    public void ConnectReporting(
        ISessionService sessions,
        IReportingApi api,
        IApiClient? profiles = null
    )
    {
        reportingSession = new(session, sessions);
        banGuard = new(ui.Dispatcher, reportingSession, api);
        admin = new(ui.Dispatcher, reportingSession, api);
        home.ProfileAdminAccess = () => admin.State is { Checked: true, IsAdmin: true };
        home.OpenModeration = () => openModeration?.Invoke();
        home.ProfileStatus = IntegrationStatus;
        var text = new ReportingLocalization(ui);
        var banned = new ReportingBanScreen(
            ui,
            banGuard,
            text,
            () => _ = session.SignOutAsync(ui.Lifetime.Token)
        );
        Host.Guard = () => banGuard.State.Status?.Banned == true ? banned : null;
        string? previous = null;
        subscriptions.Add(
            session.Listen(() =>
            {
                var current = (session.State as SessionState.SignedIn)?.Profile.Id;
                if (current == previous)
                    return;
                previous = current;
                platformActions?.Invalidate();
                if (current is null)
                    return;
                _ = banGuard.RefreshAsync(ui.Lifetime.Token);
                _ = admin.RefreshAsync(ui.Lifetime.Token);
            })
        );
        openReport = (user, origin, context) =>
        {
            if (UserId() is not { } me || me == user || user <= 0)
                return;
            var model = new ReportUserNotifier(
                ui.Dispatcher,
                reportingSession,
                api,
                banGuard,
                new(user, origin, context)
            );
            Host.OpenFeature(new ReportUserScreen(ui, model, text, Host.CloseFeature));
        };
        if (UserId() is not null)
        {
            previous = (session.State as SessionState.SignedIn)?.Profile.Id;
            _ = banGuard.RefreshAsync(ui.Lifetime.Token);
            _ = admin.RefreshAsync(ui.Lifetime.Token);
        }
        home.ReportPlayer = (user, room) => openReport(user, ReportOrigin.Chat, room);
        Host.Details.ProfileSource = profiles;
        Host.Details.OpenPlayerChat = home.OpenPlayerChat;
        openModeration = () =>
        {
            if (admin.State is not { Checked: true, IsAdmin: true })
                return;
            var screen = new ModerationScreen(
                ui,
                new(ui.Dispatcher, reportingSession, api),
                text,
                Host.CloseFeature,
                profiles: profiles
            );
            Host.OpenFeature(screen);
            _ = screen.InitializeAsync();
        };
    }

    private Action? openModeration;
    private Action<int, ReportOrigin, string?>? openReport;

    private Element IntegrationStatus()
    {
        var elements = new List<Element>();
        if (
            notificationNavigation is
            { State.Status: NotificationNavigationStatus.Error } navigation
        )
        {
            elements.Add(new Text(ui.Text.RequestFailed));
            elements.Add(
                new Button(ui.Text.Retry, () => _ = navigation.DrainAsync(ui.Lifetime.Token))
            );
        }
        if (deviceTokens is { } tokens)
        {
            var status = tokens.State.Status switch
            {
                DeviceTokenStatus.Loading => ui.Text.NotificationsLoading,
                DeviceTokenStatus.PermissionDenied => ui.Text.NotificationsPermissionDenied,
                DeviceTokenStatus.Unsupported => ui.Text.NotificationsUnsupported,
                DeviceTokenStatus.Error => tokens.State.CleanupPending
                    ? ui.Text.NotificationsCleanupPending
                    : ui.Text.NotificationsTokenError,
                _ => "",
            };
            if (status.Length > 0)
                elements.Add(new Text(status));
            if (
                tokens.State.Status is DeviceTokenStatus.Error or DeviceTokenStatus.PermissionDenied
            )
                elements.Add(
                    new Button(ui.Text.Retry, () => _ = tokens.RetryAsync(ui.Lifetime.Token))
                );
        }
        if (admin?.State.Issue is { } adminIssue)
        {
            elements.Add(new Text(new ReportingLocalization(ui).Error(adminIssue)));
            elements.Add(
                new Button(ui.Text.Retry, () => _ = admin.RefreshAsync(ui.Lifetime.Token))
            );
        }
        if (banGuard?.State.Issue is { } issue)
        {
            elements.Add(new Text(new ReportingLocalization(ui).Error(issue)));
            elements.Add(
                new Button(ui.Text.Retry, () => _ = banGuard.RefreshAsync(ui.Lifetime.Token))
            );
        }
        return new VStack(elements.ToArray()).Spacing(8);
    }

    public void ConnectPlatformActions(
        IAppointmentMaps maps,
        IPlatformShare share,
        IPdfViewer pdf,
        IAppointmentLocationSource locations
    )
    {
        // Mobile external URLs require a new Launcher release; fail explicitly until available.
        var links = new ExternalLinksAdapter(
            (_, _) => Task.FromException(new NotSupportedException())
        );
        var documents = new AppointmentPdfViewerAdapter(
            (uri, title, ct) =>
                pdf.PresentAsync(
                    uri,
                    new PdfViewOptions(
                        title,
                        Labels: new PdfViewLabels(
                            ui.Text.PdfViewerClose,
                            ui.Text.PdfViewerPreviousPage,
                            ui.Text.PdfViewerNextPage
                        )
                    ),
                    cancellationToken: ct
                )
        );
        platformActions = new(ui.Dispatcher, maps, share, links, documents, locations);
        home.OpenProfileDocument = privacy =>
            _ = platformActions.OpenDocumentAsync(
                privacy
                    ? "https://fali-nam-igrac-documents-new.s3.amazonaws.com/FNI_Politika_privatnosti.pdf"
                    : "https://fali-nam-igrac-documents-new.s3.amazonaws.com/FNI_Uslovi_koriscenja.pdf",
                privacy ? ui.Text.PrivacyPolicyLabel : ui.Text.TermsOfService,
                ui.Lifetime.Token
            );
        var text = new PlatformActionsLocalization(ui);
        Host.Details.ShareGame = id => _ = platformActions.ShareGameAsync(id, ui.Lifetime.Token);
        Host.Details.ShowMap = id => _ = platformActions.OpenMapAsync(id, ui.Lifetime.Token);
        Host.Details.ExtraActions = _ => new PlatformActionsPanel(ui, platformActions, text);
    }

    private void DisposeIntegrations()
    {
        if (lifecycle is not null && activityFollow is not null)
            lifecycle.Changed -= activityFollow;
        mobileAds?.Dispose();
        deviceTokens?.Dispose();
        platformActions?.Dispose();
        admin?.Dispose();
        banGuard?.Dispose();
        reportingSession?.Dispose();
    }
}
