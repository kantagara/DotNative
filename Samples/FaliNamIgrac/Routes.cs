using DotNative;

namespace FaliNamIgrac;

// Route table. Screens are created once and retained; the host swaps which one is visible.
public sealed partial class AppRoutes : IDisposable
{
    public RouteHost Host { get; }
    public RegistrationErrorGate Gates { get; }
    private readonly AuthLoadingGate loadingGate;

    public AppRoutes(
        UiContext ui,
        SessionNotifier session,
        CatalogNotifier catalog,
        LoginFormNotifier login,
        RegistrationFormNotifier registrationForm,
        IOnboardingController onboarding,
        IRecoveryController recovery,
        IAccountService accountApi,
        IChatService chatApi,
        DotNative.IApplicationLifecycle? lifecycle = null,
        CatalogSearchApi? searchApi = null,
        IGameEditorApi? editorApi = null,
        IMyAppointmentsService? appointmentApi = null,
        IPlacesService? places = null,
        DotNative.WheelTimePicker.IWheelTimePicker? timePicker = null,
        AccountPhotoUpload? photos = null
    )
    {
        this.ui = ui;
        this.session = session;
        this.catalog = catalog;
        home = new HomeScreen(ui, catalog, session, accountApi, chatApi, lifecycle, searchApi);
        if (places is not null)
            home.ConfigureProfileFeatures(
                places,
                () =>
                    new ProfileTexts(
                        ui.Text.ProfileSportsNotifications,
                        ui.Text.ProfileSearchLocation,
                        ui.Text.ProfileNoLocations,
                        ui.Text.ProfileDeleteAccount,
                        ui.Text.ProfileDeleteConfirmation,
                        ui.Text.ProfileDeleteFailed,
                        ui.Text.ProfileDeletePartial,
                        ui.Text.ProfileDeleteUnavailable,
                        ui.Text.ProfileSkillLevel
                    ),
                null,
                photos is null ? null : photos.UploadAsync
            );
        AppointmentActions? actions = null;
        if (appointmentApi is not null)
        {
            appointments = new(ui.Dispatcher, appointmentApi, UserId);
            actions = new(
                appointments,
                new AppointmentLocalization(ui),
                new(
                    id => home.OpenEditor?.Invoke(id),
                    home.OpenGameChat,
                    (user, game) => openReport?.Invoke(user, ReportOrigin.GameDetails, game)
                ),
                ui.Lifetime.Token
            );
            appointments.Mutated += mutation =>
                QueueRefresh(mutation.GameId, mutation.Action == AppointmentAction.Delete);
            subscriptions.Add(catalog.Listen(FollowDetails));
        }
        var onboardingScreen = new OnboardingScreen(ui, onboarding, registrationForm, session);
        Host = new(
            ui,
            session,
            catalog,
            new Dictionary<ApplicationScreen, ScreenComponent>
            {
                [ApplicationScreen.Splash] = new SplashScreen(ui),
                [ApplicationScreen.Landing] = new LandingScreen(ui),
                [ApplicationScreen.Login] = new LoginScreen(ui, login, session),
                [ApplicationScreen.Registration] = new RegistrationScreen(ui, registrationForm),
                [ApplicationScreen.RegistrationCode] = new RegistrationCodeScreen(
                    ui,
                    registrationForm
                ),
                [ApplicationScreen.Onboarding] = onboardingScreen,
                [ApplicationScreen.ResetPassword] = new RecoveryHost(
                    ui,
                    recovery,
                    ApplicationScreen.ResetPassword
                ),
                [ApplicationScreen.PasswordCode] = new RecoveryHost(
                    ui,
                    recovery,
                    ApplicationScreen.PasswordCode
                ),
                [ApplicationScreen.NewPassword] = new RecoveryHost(
                    ui,
                    recovery,
                    ApplicationScreen.NewPassword
                ),
            },
            home,
            new GameDetailsScreen(ui, catalog, actions),
            onboardingScreen.Initialize
        );
        if (editorApi is not null && places is not null && timePicker is not null)
            home.OpenEditor = id =>
            {
                if (UserId() is not { } userId)
                    return;
                var model = new GameEditorNotifier(
                    ui.Dispatcher,
                    editorApi,
                    places,
                    () => UserId() is { } current ? $"{generation}:{current}" : null,
                    userId,
                    id
                );
                if (
                    id is null
                    && catalog.State.Sport > 0
                    && home.EditorSports.Any(s => s.Id == catalog.State.Sport)
                )
                    model.Change(draft => draft with { SportId = catalog.State.Sport });
                var page = new GameEditorScreen(
                    ui,
                    model,
                    new GameEditorLocalization(ui),
                    timePicker,
                    Host.CloseFeature,
                    saved =>
                    {
                        var success = new GameSavedPopup(ui, saved.IsEdit, Host.CloseFeature);
                        Host.OpenFeature(success);
                        success.Enter();
                        QueueRefresh(saved.Id, false);
                    }
                );
                page.AvailableSports = () => home.EditorSports;
                Host.OpenFeature(page);
                _ = page.InitializeAsync();
            };
        string? previousUser = null;
        subscriptions.Add(
            session.Listen(() =>
            {
                var next = (session.State as SessionState.SignedIn)?.Profile.Id;
                if (next == previousUser)
                    return;
                previousUser = next;
                generation++;
                Host.CloseFeature();
                appointments?.Clear();
                lastGame = null;
            })
        );
        loadingGate = new AuthLoadingGate(
            ui,
            session,
            registrationForm,
            Host,
            () => home.PreparingHome.Value
        );
        Gates = new(ui, registrationForm, new ConsentDialogGate(ui, registrationForm, loadingGate));
    }

    public void Dispose()
    {
        activationsDisposed = true;
        if (activationSource is not null && activationHandler is not null)
            activationSource.Opened -= activationHandler;
        notificationNavigation?.Dispose();
        foreach (var subscription in subscriptions)
            subscription.Dispose();
        DisposeIntegrations();
        appointments?.Dispose();
        loadingGate.Dispose();
        Host.Dispose();
    }
}
