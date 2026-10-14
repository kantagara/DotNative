using System.Buffers.Binary;
using System.Net;
using System.Threading.Channels;
using DotNative;
using DotNative.Cognito;
using DotNative.Firebase;
using DotNative.GoogleMobileAds;
using DotNative.Plugins;
using DotNative.Preferences;
using DotNative.SecureStorage;
using DotNative.Svg;
using DotNative.Toast;
using DotNative.WheelDatePicker;
using FaliNamIgrac;
using Microsoft.Extensions.DependencyInjection;

internal static class DirectServicesTests
{
    public static async Task RunAsync()
    {
        // Production root activation works without registering any UI graph objects.
        var uiDispatcher = new Dispatcher();
        using (var appNative = new NativeProbe(uiDispatcher))
        {
            var appChannels = appNative.Channels;
            var services = DotNativeApplication.CreateBuilder().Services;
            services.AddSingleton<IUiDispatcher>(uiDispatcher);
            services.AddSingleton<IPlatformChannels>(appChannels);
            services.AddSingleton(new PresentationTarget(NativePlatform.IOS));
            services.AddSingleton<IOverlayPresenter>(new OverlayProbe());
            services.AddSingleton<IToast>(new SilentToast());
            services.AddSingleton<ICognitoHostedUi>(new UnusedHostedUi());
            services.AddSingleton<ISessionService>(new FakeSession());
            services.AddSingleton<IRegistrationService>(new FakeRegistration());
            services.AddSingleton<IGamesApi>(new FakeGames());
            services.AddSingleton<IProfileService>(new FakeProfile());
            services.AddSingleton<IPlacesService>(new FakePlaces());
            // Tests must never resolve a production URL or require a local .env.
            services.AddSingleton<IApiClient>(p => new ApiClient(
                p.GetRequiredKeyedService<HttpClient>("features"),
                p.GetRequiredService<ISessionService>()
            ));
            services.AddSingleton<IAccountService>(new FakeAccount());
            services.AddSingleton<IChatService>(new FakeChat());
            services.AddSingleton<CatalogSearchApi>();
            services.AddSingleton<IOnboardingDraftStore>(
                new OnboardingDraftStore(
                    DotNative.SecureStorage.SecureStorage.Create(
                        appChannels,
                        new PresentationTarget(NativePlatform.IOS)
                    )
                )
            );
            services.AddSingleton<IRecoveryProgressStore>(
                new RecoveryProgressStore(
                    DotNative.SecureStorage.SecureStorage.Create(
                        appChannels,
                        new PresentationTarget(NativePlatform.IOS)
                    )
                )
            );
            services.AddKeyedSingleton<HttpClient>(
                "features",
                (_, _) =>
                    new HttpClient(new HttpProbe())
                    {
                        BaseAddress = new Uri("https://example.invalid/"),
                    }
            );
            services.AddSingleton<IGameEditorApi>(p => new GameEditorApi(
                p.GetRequiredKeyedService<HttpClient>("features"),
                p.GetRequiredService<ISessionService>(),
                (_, _) => Task.CompletedTask
            ));
            services.AddSingleton<IMyAppointmentsService>(p => new MyAppointmentsService(
                p.GetRequiredKeyedService<HttpClient>("features"),
                p.GetRequiredService<ISessionService>()
            ));
            services.AddSingleton<AccountPhotoUpload>(p => new AccountPhotoUpload(
                p.GetRequiredKeyedService<HttpClient>("features"),
                p.GetRequiredService<ISessionService>(),
                new CognitoProfilePhotoStorage(p.GetRequiredService<ISessionService>())
            ));
            services.AddSingleton<IReportingApi>(p => new ReportingApi(
                p.GetRequiredKeyedService<HttpClient>("features")
            ));
            services.AddSingleton<INotificationDevice>(new NoNotificationDevice());
            services.AddSingleton<IAppointmentLocationSource, AppointmentLocationSource>();
            services.AddSingleton<IAppointmentMaps>(
                new AppointmentMapsAdapter(
                    _ => Task.FromResult<IReadOnlyList<AppointmentMap>>([]),
                    (_, _, _) => Task.CompletedTask
                )
            );
            services.AddSingleton<IPlatformShare>(
                new PlatformShareAdapter((_, _) => Task.CompletedTask)
            );
            DotNative.GoogleMobileAds.PlacementAdsServices.AddPlacementMobileAds(services);
            DotNative.PdfView.PdfViewServices.AddPdfViewer(services);
            DotNative.WheelTimePicker.WheelTimePickerServices.AddWheelTimePicker(services);
            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true }
            );
            using var app = ActivatorUtilities.CreateInstance<App>(provider);
            Check(app.Build() is Theme, "Production root activation failed.");
            Check(provider.GetService<UiContext>() is null, "UI state leaked into DI.");
            app.Dispose();
            app.Dispose();
        }

        using var handler = new HttpProbe();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using (var api = new ApiClient(http, new FakeSession()))
        {
            using var authorized = await api.GetAsync("private", default, authenticated: true);
            Check(
                handler.Authorization == "Bearer unused",
                "Authenticated request lost its token."
            );
            using var anonymous = await api.GetAsync("public", default);
            Check(handler.Authorization is null, "Authorization leaked into an anonymous request.");
            handler.Block = true;
            await Canceled(
                api.GetAsync("timeout", default, timeout: TimeSpan.FromMilliseconds(20))
            );
            using var canceled = new CancellationTokenSource();
            var pending = api.GetAsync(
                "cancel",
                canceled.Token,
                timeout: TimeSpan.FromSeconds(100)
            );
            canceled.Cancel();
            await Canceled(pending);
            handler.Block = false;
        }
        using var stillOwnedByCaller = await http.GetAsync("still-alive");

        var dispatcher = new ImmediateDispatcher();
        var overlay = new OverlayProbe();
        using var native = new NativeProbe(dispatcher);
        var target = new PresentationTarget(NativePlatform.IOS);
        var first = WheelDatePicker.ShowAsync(overlay, dispatcher, native.Channels, target);
        var firstCall = await native.ReadAsync();
        using var queuedCancellation = new CancellationTokenSource();
        var queued = WheelDatePicker.ShowAsync(
            overlay,
            dispatcher,
            native.Channels,
            target,
            cancellationToken: queuedCancellation.Token
        );
        queuedCancellation.Cancel();
        await Canceled(queued);
        Check(native.CallCount == 1, "Canceled queued picker reached the native host.");
        var second = WheelDatePicker.ShowAsync(overlay, dispatcher, native.Channels, target);
        Check(native.CallCount == 1, "Concurrent static picker calls did not serialize.");
        using (var other = new NativeProbe(dispatcher))
        {
            var separate = WheelDatePicker.ShowAsync(overlay, dispatcher, other.Channels, target);
            var otherCall = await other.ReadAsync();
            other.Reply(otherCall, "2024-07-12");
            Check(
                await separate == new DateOnly(2024, 7, 12),
                "Separate hosts blocked each other."
            );
        }
        native.Reply(firstCall, "2024-07-10");
        Check(await first == new DateOnly(2024, 7, 10), "First date result changed.");
        var secondCall = await native.ReadAsync();
        native.Reply(secondCall, null);
        Check(await second is null, "Dismissal must return null.");
        await WheelDatePicker.ShowAsync(
            overlay,
            dispatcher,
            native.Channels,
            target,
            new DatePickerOptions { Renderer = DatePickerRenderer.SharedWheel }
        );
        Check(overlay.Calls == 1, "Explicit shared renderer bypassed the application overlay.");

        foreach (var platform in new[] { NativePlatform.Android, NativePlatform.IOS })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPlatformChannels>(native.Channels);
            services.AddSingleton(new PresentationTarget(platform));
            services.AddFirebase(
                host => new FirebaseAppOptions(
                    "test-api-key",
                    host.Platform.ToString(),
                    "project",
                    "sender",
                    "bucket"
                ),
                startup: new FirebaseStartupOptions(
                    LogAppOpen: true,
                    RequestNotificationPermission: true
                )
            );
            services.AddAdMobAds(host => new AdMobAdsOptions(
                host.Platform == NativePlatform.IOS
                    ? "ca-app-pub-0000000000000000~1111111111"
                    : "ca-app-pub-0000000000000000~2222222222"
            ));
            using var provider = services.BuildServiceProvider();
            var firebase = provider.GetRequiredService<IFirebaseApp>();
            Check(
                ReferenceEquals(firebase, provider.GetRequiredService<IFirebaseAnalytics>())
                    && ReferenceEquals(
                        firebase,
                        provider.GetRequiredService<IFirebaseCrashlytics>()
                    )
                    && ReferenceEquals(firebase, provider.GetRequiredService<IFirebaseMessaging>())
                    && ReferenceEquals(
                        firebase,
                        provider.GetRequiredService<IFirebaseRemoteConfig>()
                    ),
                "Firebase products were registered as separate instances."
            );
            Check(
                provider.GetServices<IApplicationInitializer>().Count() == 2,
                "Configured SDK registration did not register automatic initialization."
            );
            var hooks = provider.GetServices<IApplicationInitializer>().ToArray();
            var initialize = hooks[0].InitializeAsync(default);
            var firebaseCall = await native.ReadAsync();
            Check(
                (string?)firebaseCall.Arguments["applicationId"] == platform.ToString(),
                "Firebase ignored the configuration selected at registration."
            );
            Check(
                (string?)firebaseCall.Arguments["apiKey"] == "test-api-key",
                "Firebase lost configured credentials."
            );
            native.Reply(firebaseCall, null);
            var appOpen = await native.ReadAsync();
            Check(
                (string?)appOpen.Arguments["name"] == "app_open",
                "Configured app-open event was not logged."
            );
            native.Reply(appOpen, null);
            var permission = await native.ReadAsync();
            native.Reply(permission, "authorized");
            await initialize;
            var ads = provider.GetRequiredService<IAdMobAds>();
            Check(
                ReferenceEquals(ads, provider.GetRequiredService<IMobileAds>()),
                "Compatibility alias created a second AdMob service."
            );
            var startAds = hooks[1].InitializeAsync(default);
            var adsCall = await native.ReadAsync();
            Check(
                (string?)adsCall.Arguments["applicationId"]
                    == (
                        platform == NativePlatform.IOS
                            ? "ca-app-pub-0000000000000000~1111111111"
                            : "ca-app-pub-0000000000000000~2222222222"
                    ),
                "AdMob ignored the platform-specific application ID."
            );
            native.Reply(adsCall, null);
            await startAds;
            var mismatch = ads.InitializeAsync();
            var mismatchCall = await native.ReadAsync();
            native.Reply(
                mismatchCall,
                new Dictionary<string, object?>
                {
                    ["code"] = "configuration_mismatch",
                    ["message"] = "Wrong packaged app ID",
                },
                1
            );
            try
            {
                await mismatch;
                throw new Exception("Native configuration mismatch was hidden.");
            }
            catch (PluginException error) when (error.Code == "configuration_mismatch") { }
        }
        try
        {
            new ServiceCollection().AddAdMobAds(
                new AdMobAdsOptions("ca-app-pub-0000000000000000/1111111111")
            );
            throw new Exception("Ad-unit ID accepted as an application ID.");
        }
        catch (ArgumentException) { }
        var desktopServices = new ServiceCollection();
        desktopServices.AddSingleton<IPlatformChannels>(native.Channels);
        desktopServices.AddSingleton(new PresentationTarget(NativePlatform.MacOS));
        desktopServices.AddFirebase(_ =>
            throw new Exception("Desktop evaluated mobile Firebase configuration.")
        );
        desktopServices.AddAdMobAds(_ =>
            throw new Exception("Desktop evaluated mobile AdMob configuration.")
        );
        using (var desktop = desktopServices.BuildServiceProvider())
        {
            await desktop.GetRequiredService<IFirebaseApp>().InitializeAsync();
            await desktop.GetRequiredService<IAdMobAds>().InitializeAsync();
        }

        var preferences = Preferences.Create(native.Channels, target);
        var read = preferences.GetAsync("theme", "system");
        var readCall = await native.ReadAsync();
        Check(
            !readCall.Arguments.ContainsKey("applicationId"),
            "Default preferences changed the native namespace."
        );
        native.Reply(readCall, "dark");
        Check(await read == "dark", "Direct preferences did not decode the native result.");
        var themeDispatcher = new Dispatcher();
        var themeNative = new NativeProbe(themeDispatcher);
        var themes = new ThemeController(
            themeNative.Channels,
            target,
            new DarkAppearance(),
            themeDispatcher
        );
        var loadTheme = themes.LoadAsync(default);
        var themeReadTask = themeNative.ReadAsync();
        themeDispatcher.Finish(themeReadTask);
        var themeRead = themeReadTask.GetAwaiter().GetResult();
        themeNative.Reply(themeRead, "dark");
        themeDispatcher.Finish(loadTheme);
        Check(themes.Current.Value.Mode == ThemeMode.Dark, "Saved dark theme was not restored.");
        var saveTheme = themes.SetAsync(ThemeMode.Light, default);
        var themeWriteTask = themeNative.ReadAsync();
        themeDispatcher.Finish(themeWriteTask);
        var themeWrite = themeWriteTask.GetAwaiter().GetResult();
        themeNative.Reply(themeWrite, null);
        themeDispatcher.Finish(saveTheme);
        Check(
            themes.Current.Value.Mode == ThemeMode.Light,
            "Light theme was not applied after persistence."
        );
        var storage = SecureStorage.Create(native.Channels, target, "test.explicit");
        var secret = storage.ReadAsync("token");
        var secretCall = await native.ReadAsync();
        Check(
            (string?)secretCall.Arguments["applicationId"] == "test.explicit",
            "Explicit secure namespace was lost."
        );
        native.Reply(secretCall, "secret");
        Check(await secret == "secret", "Direct secure storage did not decode the native result.");
        byte[] svg =
            "<svg viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>"u8.ToArray();
        Check(
            ReferenceEquals(Svg.Picture(svg, 10, 10), Svg.Picture(svg, 10, 10)),
            "Static SVG cache was bypassed."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static async Task Canceled(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new Exception("Expected cancellation.");
    }

    private sealed class HttpProbe : HttpMessageHandler
    {
        public string? Authorization;
        public bool Block;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Authorization = request.Headers.Authorization?.ToString();
            if (Block)
                await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class DarkAppearance : ISystemAppearance
    {
        public ThemeMode Theme => ThemeMode.Dark;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }

    private sealed class OverlayProbe : IOverlayPresenter
    {
        public int Calls;

        public Task<T> ShowAsync<T>(
            Func<Action<T>, Element> content,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(default(T)!);
        }
    }

    private sealed record Call(long Id, Dictionary<string, object?> Arguments);

    private sealed class NativeProbe : IPluginTransport, IDisposable
    {
        public PlatformChannels Channels { get; }
        private readonly Channel<Call> calls = Channel.CreateUnbounded<Call>();
        public int CallCount;

        public NativeProbe(IUiDispatcher dispatcher) => Channels = new(dispatcher, this);

        public void SendPlugin(ReadOnlySpan<byte> message)
        {
            Check(
                BinaryPrimitives.ReadUInt32LittleEndian(message[12..]) == 1,
                "Unexpected native cancellation packet."
            );
            var payload = (Dictionary<string, object?>)PluginCodec.Decode(message[16..])!;
            Interlocked.Increment(ref CallCount);
            calls.Writer.TryWrite(
                new(
                    BinaryPrimitives.ReadInt64LittleEndian(message),
                    (Dictionary<string, object?>)payload["arguments"]!
                )
            );
        }

        public async Task<Call> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await calls.Reader.ReadAsync(timeout.Token);
        }

        public void Reply(Call call, object? value, uint status = 0)
        {
            var payload = PluginCodec.Encode(value);
            var packet = new byte[12 + payload.Length];
            BinaryPrimitives.WriteInt64LittleEndian(packet, call.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), status);
            payload.CopyTo(packet, 12);
            Channels.AcceptReply(packet);
        }

        public void Dispose() => Channels.Dispose();
    }
}

internal sealed class NoNotificationDevice : INotificationDevice
{
    public Task<NotificationPermission> RequestPermissionAsync(CancellationToken token) =>
        Task.FromResult(NotificationPermission.Denied);

    public Task<string> GetTokenAsync(CancellationToken token) =>
        throw new InvalidOperationException();
}
