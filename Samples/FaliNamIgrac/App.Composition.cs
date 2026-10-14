using DotNative;
using DotNative.Cognito;
using DotNative.ImagePicker;
using DotNative.Plugins;
using DotNative.Toast;
using Microsoft.Extensions.DependencyInjection;

namespace FaliNamIgrac;

public sealed partial class App
{
    private readonly UiContext? ownedUi;

    [ActivatorUtilitiesConstructor]
    public App(IServiceProvider services)
        : this(new Composition(services)) { }

    private App(Composition composition)
        : this(
            composition.Ui,
            composition.Ui.Router,
            composition.Session,
            composition.Registration,
            composition.Routes
        )
    {
        ownedUi = composition.Ui;
    }

    // UI state belongs to this root instance, not to the application DI container.
    // Only this composition boundary resolves services; screens receive concrete dependencies.
    private sealed class Composition
    {
        public UiContext Ui { get; }
        public SessionNotifier Session { get; }
        public RegistrationFormNotifier Registration { get; }
        public AppRoutes Routes { get; }

        public Composition(IServiceProvider services)
        {
            var dispatcher = services.GetRequiredService<IUiDispatcher>();
            var channels = services.GetRequiredService<IPlatformChannels>();
            var target = services.GetRequiredService<PresentationTarget>();
            var toast = services.GetRequiredService<IToast>();
            var sessions = services.GetRequiredService<ISessionService>();
            var registration = services.GetRequiredService<IRegistrationService>();
            var games = services.GetRequiredService<IGamesApi>();
            var presentation = new PresentationState();
            var themes = new ThemeController(
                channels,
                target,
                services.GetRequiredService<ISystemAppearance>(),
                dispatcher
            );
            Session = new(dispatcher, sessions, registration, games);
            var catalog = new CatalogNotifier(
                dispatcher,
                Session,
                games,
                presentation,
                services.GetRequiredService<CatalogSearchApi>()
            );
            var login = new LoginFormNotifier(dispatcher);
            Registration = new(dispatcher, registration, Session);
            var recovery = new RecoveryController(
                sessions,
                dispatcher,
                services.GetRequiredService<IRecoveryProgressStore>()
            );
            var onboarding = new OnboardingController(
                dispatcher,
                services.GetRequiredService<IProfileService>(),
                services.GetRequiredService<IPlacesService>(),
                registration,
                games,
                services.GetRequiredService<IOnboardingDraftStore>()
            );
            var social = new SocialAuthentication(services.GetRequiredService<ICognitoHostedUi>());
            var router = new AuthRouter(
                presentation,
                Session,
                sessions,
                social,
                toast,
                login,
                Registration,
                recovery
            );
            Ui = new(
                presentation,
                themes,
                dispatcher,
                target,
                channels,
                services.GetRequiredService<IOverlayPresenter>(),
                toast,
                router
            );
            onboarding.Texts = () => AuthFlowText.From(Ui.Text);
            Ui.ChatCache = services.GetService<IChatCache>();
            if (services.GetService<IImagePicker>() is { } picker)
            {
                Ui.PickPhoto = async token =>
                {
                    await using var image = await picker.PickImageAsync(token);
                    if (image is null)
                        return null;
                    await using var stream = await image.OpenReadAsync(token);
                    var bytes = await ImageRepository.ReadBoundedAsync(stream, token);
                    _ = PhotoFormat.Detect(bytes);
                    return bytes;
                };
                Ui.UploadPhoto = services.GetRequiredService<IProfileService>().UploadPhotoAsync;
            }
            if (services.GetService<ImageRepository>() is { } images)
                Ui.Images = new(images, dispatcher, Ui.Lifetime.Token);
            Routes = new(
                Ui,
                Session,
                catalog,
                login,
                Registration,
                onboarding,
                recovery,
                services.GetRequiredService<IAccountService>(),
                services.GetRequiredService<IChatService>(),
                services.GetRequiredService<IApplicationLifecycle>(),
                services.GetRequiredService<CatalogSearchApi>(),
                services.GetRequiredService<IGameEditorApi>(),
                services.GetRequiredService<IMyAppointmentsService>(),
                services.GetRequiredService<IPlacesService>(),
                services.GetRequiredService<DotNative.WheelTimePicker.IWheelTimePicker>(),
                services.GetRequiredService<AccountPhotoUpload>()
            );
            Routes.ConnectNotifications(
                sessions,
                services.GetRequiredService<INotificationDevice>(),
                services.GetRequiredKeyedService<HttpClient>("features"),
                services.GetRequiredService<IApplicationLifecycle>()
            );
            Routes.ConnectActivations(
                services.GetRequiredService<IApplicationActivations>(),
                services.GetRequiredService<IChatService>(),
                games,
                services.GetRequiredService<IMyAppointmentsService>()
            );
            Routes.ConnectAds(
                services.GetRequiredService<DotNative.GoogleMobileAds.IPlacementMobileAds>(),
                services.GetRequiredService<DotNative.GoogleMobileAds.IAdConsent>(),
                services.GetRequiredService<IApplicationLifecycle>()
            );
            Routes.ConnectReporting(
                sessions,
                services.GetRequiredService<IReportingApi>(),
                services.GetRequiredService<IApiClient>()
            );
            Routes.ConnectPlatformActions(
                services.GetRequiredService<IAppointmentMaps>(),
                services.GetRequiredService<IPlatformShare>(),
                services.GetRequiredService<DotNative.PdfView.IPdfViewer>(),
                services.GetRequiredService<IAppointmentLocationSource>()
            );
        }
    }
}
