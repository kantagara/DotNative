using DotNative;
using DotNative.Cognito;
using DotNative.Firebase;
using DotNative.GoogleMobileAds;
using DotNative.Hive;
using DotNative.ImagePicker;
using DotNative.MapLauncher;
using DotNative.PdfView;
using DotNative.Plugins;
using DotNative.Share;
using DotNative.Toast;
using DotNative.WheelTimePicker;
using Microsoft.Extensions.DependencyInjection;

namespace FaliNamIgrac;

public static class Program
{
    public static void Main()
    {
        DotNetEnv.Env.NoClobber().TraversePath().Load();
        var builder = DotNativeApplication.CreateBuilder();
        var services = builder.Services;
        services.AddToast();
        services.AddImagePicker();
        services.AddFirebase(
            target => new FirebaseAppOptions(
                ApiKey: target.Platform == NativePlatform.IOS
                    ? BackendConfiguration.IosFirebaseApiKey
                    : BackendConfiguration.AndroidFirebaseApiKey,
                ApplicationId: target.Platform == NativePlatform.IOS
                    ? BackendConfiguration.IosFirebaseAppId
                    : BackendConfiguration.AndroidFirebaseAppId,
                ProjectId: BackendConfiguration.FirebaseProjectId,
                MessagingSenderId: BackendConfiguration.FirebaseSenderId,
                StorageBucket: BackendConfiguration.FirebaseStorageBucket,
                BundleId: target.Platform == NativePlatform.IOS
                    ? BackendConfiguration.IosBundleId
                    : ""
            ),
            startup: new FirebaseStartupOptions(
                LogAppOpen: true,
                RequestNotificationPermission: false
            )
        );
        services.AddPlacementMobileAds();
        services.AddMapLauncher();
        services.AddShare();
        services.AddPdfViewer();
        services.AddSingleton<IAppointmentMaps>(p =>
        {
            var maps = p.GetRequiredService<IMapLauncher>();
            return new AppointmentMapsAdapter(
                async ct =>
                    (await maps.InstalledMapsAsync(ct))
                        .Select(m =>
                            m.App == MapApp.Apple ? AppointmentMap.Apple : AppointmentMap.Google
                        )
                        .ToArray(),
                (map, location, ct) =>
                    maps.ShowMarkerAsync(
                        map == AppointmentMap.Apple ? MapApp.Apple : MapApp.Google,
                        location.Latitude,
                        location.Longitude,
                        location.Title,
                        ct
                    )
            );
        });
        services.AddSingleton<IPlatformShare>(p => new PlatformShareAdapter(
            (uri, ct) =>
                p.GetRequiredService<IShare>()
                    .ShareTextAsync(uri.AbsoluteUri, cancellationToken: ct)
        ));
        services.AddSingleton<IAppointmentLocationSource, AppointmentLocationSource>();
        services.AddSingleton<IReportingApi>(p => new ReportingApi(
            p.GetRequiredKeyedService<HttpClient>("features")
        ));
        services.AddWheelTimePicker();
        services.AddSingleton<INotificationDevice, FirebaseNotificationDevice>();
        services.AddSingleton<IOnboardingDraftStore>(p => new OnboardingDraftStore(
            DotNative.SecureStorage.SecureStorage.Create(
                p.GetRequiredService<IPlatformChannels>(),
                p.GetRequiredService<PresentationTarget>()
            )
        ));
        services.AddSingleton<IRecoveryProgressStore>(p => new RecoveryProgressStore(
            DotNative.SecureStorage.SecureStorage.Create(
                p.GetRequiredService<IPlatformChannels>(),
                p.GetRequiredService<PresentationTarget>()
            )
        ));
        services.AddKeyedSingleton<HttpClient>(
            "features",
            (_, _) =>
                new HttpClient
                {
                    BaseAddress = new Uri(BackendConfiguration.ApiUrl.TrimEnd('/') + "/"),
                    Timeout = Timeout.InfiniteTimeSpan,
                }
        );
        services.AddSingleton<CatalogSearchApi>();
        services.AddSingleton<IMyAppointmentsService>(p => new MyAppointmentsService(
            p.GetRequiredKeyedService<HttpClient>("features"),
            p.GetRequiredService<ISessionService>()
        ));
        services.AddSingleton<IGameEditorApi>(p => new GameEditorApi(
            p.GetRequiredKeyedService<HttpClient>("features"),
            p.GetRequiredService<ISessionService>(),
            new CognitoExecuteApiSigner(
                new Uri(BackendConfiguration.ApiUrl),
                BackendConfiguration.Region,
                BackendConfiguration.IdentityPoolId,
                $"cognito-idp.{BackendConfiguration.Region}.amazonaws.com/{BackendConfiguration.PoolId}",
                p.GetRequiredService<ISessionService>().IdTokenAsync
            ).SignAsync
        ));
        services.AddSingleton<AccountPhotoUpload>(p => new AccountPhotoUpload(
            p.GetRequiredKeyedService<HttpClient>("features"),
            p.GetRequiredService<ISessionService>(),
            new CognitoProfilePhotoStorage(p.GetRequiredService<ISessionService>())
        ));
        services.AddCognitoHostedUi(
            new CognitoHostedUiConfiguration(
                "falinamigracauth-dev.auth.us-east-1.amazoncognito.com",
                BackendConfiguration.ClientId,
                "falinamigrac://oauth/"
            )
        );
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddHive(
            Path.Combine(FniStoragePaths.DataDirectory, "hive"),
            recoverCorruptBoxes: true
        );
        services.AddSingleton<IChatCache>(provider => new ChatCache(
            provider.GetRequiredService<IHive>(),
            Path.Combine(FniStoragePaths.DataDirectory, "chat-v1")
        ));
        services.AddSingleton(_ => new ImageRepository(
            Path.Combine(FniStoragePaths.DataDirectory, "images-v1")
        ));
        services.AddSingleton<IPlacesService, PlacesService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IGamesApi, GamesApi>();
        services.AddSingleton<IRegistrationService, RegistrationService>();
        builder.Build().Run<App>();
    }
}
