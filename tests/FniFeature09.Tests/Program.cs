using System.Collections.Concurrent;
using System.Net;
using DotNative;
using DotNative.Firebase;
using DotNative.Plugins;
using DotNative.Preferences;
using FaliNamIgrac;

var tests = new (string, Action)[]
{
    ("native adapters order consent/init, retry, PDF validation and lease cleanup", NativeAdapters),
    ("links and coordinates reject unsafe/missing values", Validation),
    ("map chooser, browser fallback, permission and unavailable", Maps),
    ("released plugin errors are mapped to fallback/denial", PluginErrors),
    ("share exact public link, error/retry and no double submission", Sharing),
    ("PDF unavailable uses explicit external fallback, never fake viewer", Documents),
    (
        "cancellation, logout, same-account replacement and disposal suppress stale results",
        StaleActions
    ),
    ("authenticated location route, source-generated JSON and backend errors", LocationContract),
    ("remote config key, empty/malformed payload and cancellation", RemoteContract),
    ("sponsor dates, invalid links, reservation, visible cap, persistence and rollover", Sponsors),
    ("preferences ledger source-generated round trip and corrupt storage", LedgerPersistence),
    ("foreground frequency matches Flutter", Frequency),
    ("sponsor first, image failure releases reservation, retry and visibility", SponsorPlacement),
    ("consent denial, test-unit guard, unsupported native service and retry", AdsGates),
    ("native disposal on background, cancellation and delayed completion", AdDisposal),
    ("failed disposal is observable and cleanup can be retried", CleanupRetry),
    ("sponsor modal countdown, close failure/retry and timer disposal", SponsorDismissal),
    ("app open expiry, full screen completion and privacy options", AppOpen),
};
foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"Passed {tests.Length} feature 09 test groups.");

static void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}
static void Throws<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new Exception("Expected " + typeof(T).Name);
}
static void NativeAdapters()
{
    var order = new List<string>();
    var allow = false;
    var failInit = true;
    var adapter = new NativeAdsConsentAdapter(
        _ =>
        {
            order.Add("configure");
            return Task.CompletedTask;
        },
        _ =>
        {
            order.Add("gather");
            return Task.FromResult(allow);
        },
        _ =>
        {
            order.Add("status");
            return Task.FromResult(allow);
        },
        _ =>
        {
            order.Add("init");
            return failInit ? Task.FromException(new IOException()) : Task.CompletedTask;
        },
        _ =>
        {
            order.Add("privacy");
            return Task.CompletedTask;
        }
    );
    Check(!adapter.CanRequestAdsAsync(default).GetAwaiter().GetResult(), "denial ignored");
    Check(order.SequenceEqual(new[] { "configure", "gather" }), "init before consent");
    allow = true;
    Throws<IOException>(() => adapter.CanRequestAdsAsync(default).GetAwaiter().GetResult());
    failInit = false;
    Check(adapter.CanRequestAdsAsync(default).GetAwaiter().GetResult(), "init retry");
    adapter.CanRequestAdsAsync(default).GetAwaiter().GetResult();
    Check(
        order.Count(x => x == "init") == 2 && order.Count(x => x == "gather") == 1,
        "duplicate init/gather"
    );
    adapter.ShowPrivacyOptionsAsync(default).GetAwaiter().GetResult();
    var pdfCalls = 0;
    var pdf = new AppointmentPdfViewerAdapter(
        (uri, title, ct) =>
        {
            pdfCalls++;
            return Task.FromException(new PluginException("unavailable", "missing"));
        }
    );
    Throws<ArgumentException>(() =>
        pdf.PresentAsync(new Uri("http://example.com/a"), "PDF", default).GetAwaiter().GetResult()
    );
    Throws<PlatformNotSupportedException>(() =>
        pdf.PresentAsync(new Uri("https://example.com/a"), "PDF", default).GetAwaiter().GetResult()
    );
    Check(pdfCalls == 1, "invalid PDF reached native");
    var disposed = 0;
    var failDispose = true;
    var lease = new NativePlacementAd(
        null,
        _ => Task.CompletedTask,
        () =>
        {
            disposed++;
            return failDispose
                ? ValueTask.FromException(new IOException())
                : ValueTask.CompletedTask;
        }
    );
    Throws<IOException>(() => lease.DisposeAsync().AsTask().GetAwaiter().GetResult());
    failDispose = false;
    lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    Check(disposed == 2, "disposal retry/idempotency");
    using var cts = new CancellationTokenSource();
    var late = new TaskCompletionSource<IPlacementAd>();
    var lateDisposed = 0;
    var loader = new NativePlacementAdsAdapter((_, _) => late.Task);
    var request = new TestAdRequest(AdPlacement.Feed, "ca-app-pub-3940256099942544/6300978111");
    var pending = loader.LoadAsync(request, cts.Token);
    cts.Cancel();
    late.SetResult(
        new NativePlacementAd(
            null,
            _ => Task.CompletedTask,
            () =>
            {
                lateDisposed++;
                return ValueTask.CompletedTask;
            }
        )
    );
    Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
    Check(lateDisposed == 1, "late native lease leaked");
}
static void Validation()
{
    foreach (
        var url in new[]
        {
            "javascript:alert(1)",
            "http://example.com/a.pdf",
            "file:///x",
            "https://user:pass@example.com/a",
            "https://localhost/a",
            "https://example.com:8080/a",
            "\nhttps://example.com",
        }
    )
        Throws<ArgumentException>(() => PlatformActionLinks.Https(url));
    foreach (var id in new[] { "", "..", "one/two", "x?token=1", "x#frag", "a%2fb" })
        Throws<ArgumentException>(() => PlatformActionLinks.Game(id));
    Check(
        PlatformActionLinks.Game("test 1").AbsoluteUri
            == "https://app.falinamigrac.com/game/test%201",
        "wrong public URL"
    );
    foreach (
        var point in new[]
        {
            new AppointmentLocation(double.NaN, 0, "x"),
            new(0, double.PositiveInfinity, "x"),
            new(91, 0, "x"),
            new(0, 181, "x"),
            new(0, 0, ""),
        }
    )
        Throws<ArgumentException>(point.Validate);
    new AppointmentLocation(0, 0, "Equator").Validate();
}
static void Maps()
{
    var f = new Fixture();
    using var n = f.Actions();
    f.Maps.Installed = [AppointmentMap.Apple, AppointmentMap.Google];
    f.Ui.Finish(n.OpenMapAsync(new AppointmentLocation(44.1, 20.2, "Hall")));
    Check(n.State.Status == PlatformActionStatus.ChooseMap && f.Maps.Opens == 0, "chooser missing");
    f.Ui.Finish(n.SelectMapAsync(AppointmentMap.Google));
    Check(f.Maps.Opens == 1, "selection missing");
    f.Maps.Installed = [];
    f.Ui.Finish(n.OpenMapAsync(new AppointmentLocation(44.1, 20.2, "Hall")));
    Check(f.Links.Last!.AbsoluteUri.Contains("44.1%2C20.2"), "map browser fallback invalid");
    f.Maps.Installed = [AppointmentMap.Google];
    f.Maps.Error = new UnauthorizedAccessException();
    f.Ui.Finish(n.OpenMapAsync(new AppointmentLocation(44, 20, "Hall")));
    Check(n.State.Status == PlatformActionStatus.PermissionDenied, "permission denial swallowed");
    f.Maps.Error = new PlatformNotSupportedException();
    f.Links.Error = new PlatformNotSupportedException();
    f.Ui.Finish(n.RetryAsync());
    Check(n.State.Status == PlatformActionStatus.Unavailable, "unavailable reported success");
}
static void PluginErrors()
{
    var adapter = new AppointmentMapsAdapter(
        _ =>
            Task.FromException<IReadOnlyList<AppointmentMap>>(
                new PluginException("unavailable", "missing")
            ),
        (_, _, _) => Task.FromException(new PluginException("map_unavailable", "gone"))
    );
    Throws<PlatformNotSupportedException>(() =>
        adapter.InstalledAsync(default).GetAwaiter().GetResult()
    );
    Throws<PlatformNotSupportedException>(() =>
        adapter
            .OpenAsync(AppointmentMap.Google, new(0, 0, "Hall"), default)
            .GetAwaiter()
            .GetResult()
    );
    var share = new PlatformShareAdapter(
        (_, _) => Task.FromException(new PluginException("permission_denied", "denied"))
    );
    Throws<UnauthorizedAccessException>(() =>
        share.ShareAsync(PlatformActionLinks.Game("a"), default).GetAwaiter().GetResult()
    );
}
static void Sharing()
{
    var f = new Fixture();
    using var n = f.Actions();
    f.Share.Hold = new();
    var first = n.ShareGameAsync("uuid-1");
    var second = n.ShareGameAsync("uuid-1");
    Check(f.Share.Calls == 1 && n.State.Status == PlatformActionStatus.Loading, "duplicate share");
    f.Share.Hold.SetResult();
    f.Ui.Finish(Task.WhenAll(first, second));
    Check(f.Share.Last!.AbsoluteUri == "https://app.falinamigrac.com/game/uuid-1", "wrong URL");
    f.Share.Error = new IOException();
    f.Ui.Finish(n.ShareGameAsync("uuid-2"));
    Check(n.State.Status == PlatformActionStatus.Error, "share failure swallowed");
    f.Share.Error = null;
    f.Ui.Finish(n.RetryAsync());
    Check(n.State.Status == PlatformActionStatus.Completed, "retry failed");
}
static void Documents()
{
    var f = new Fixture();
    using var n = f.Actions();
    f.Ui.Finish(n.OpenDocumentAsync("https://docs.example.com/policy.pdf", "Policy"));
    Check(
        n.State.Status == PlatformActionStatus.Unavailable
            && n.State.CanOpenExternally
            && f.Links.Calls == 0,
        "fake PDF success or silent substitution"
    );
    f.Ui.Finish(n.OpenDocumentExternallyAsync());
    Check(f.Links.Calls == 1, "external fallback missing");
    f.Ui.Finish(n.OpenDocumentAsync("file:///etc/passwd", "Policy"));
    Check(n.State.Status == PlatformActionStatus.InvalidInput, "invalid document URL");
    f.Links.Error = new PlatformNotSupportedException();
    f.Ui.Finish(n.OpenExternalAsync("https://docs.example.com/policy.pdf"));
    Check(n.State.Status == PlatformActionStatus.Unavailable, "missing handler reported success");
}
static void StaleActions()
{
    var f = new Fixture();
    var n = f.Actions();
    f.Locations.Hold = new();
    var task = n.OpenMapAsync("a");
    n.Invalidate();
    f.Locations.Hold.SetResult(new(0, 0, "Late"));
    f.Ui.Finish(task);
    Check(
        n.State.Status == PlatformActionStatus.Idle && f.Maps.Opens == 0,
        "late location crossed session"
    );
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    f.Ui.Finish(n.ShareGameAsync("a", cts.Token));
    Check(n.State.Status == PlatformActionStatus.Cancelled, "cancel missing");
    f.Share.Hold = new();
    task = n.ShareGameAsync("b");
    n.Dispose();
    f.Share.Hold.SetResult();
    f.Ui.Finish(task);
    Throws<ObjectDisposedException>(() => n.ShareGameAsync("a"));
}
static void LocationContract()
{
    var api = new LocationApi();
    var source = new AppointmentLocationSource(api);
    api.Body =
        """{"appointment":{"location_lat":"44.5","location_lon":20.2,"appointment_address":"Hall"}}""";
    var point = source.LoadAsync("id-1", default).GetAwaiter().GetResult();
    Check(
        point.Latitude == 44.5 && api.Path == "games/id-1" && api.Authenticated,
        "backend contract mismatch"
    );
    api.Body = "{}";
    Throws<InvalidDataException>(() => source.LoadAsync("a", default).GetAwaiter().GetResult());
    api.Body = """{"appointment":{"appointment_address":"Hall"}}""";
    Throws<System.Text.Json.JsonException>(() =>
        source.LoadAsync("a", default).GetAwaiter().GetResult()
    );
    api.Status = HttpStatusCode.Forbidden;
    Throws<HttpRequestException>(() => source.LoadAsync("a", default).GetAwaiter().GetResult());
}
static void RemoteContract()
{
    var remote = new Remote();
    var source = new FirebaseSponsorConfigSource(remote);
    Check(
        source.FetchAsync(default).Result.Feed.Length == 0
            && remote.Key == "InGamePartnershipAds"
            && remote.Fetches == 1,
        "remote contract mismatch"
    );
    remote.Json = "{}";
    Throws<System.Text.Json.JsonException>(() =>
        source.FetchAsync(default).GetAwaiter().GetResult()
    );
    remote.Json = """{"interstitial":[],"feed":[],"game_details":[],"chat":null}""";
    Throws<InvalidDataException>(() => source.FetchAsync(default).GetAwaiter().GetResult());
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Throws<OperationCanceledException>(() => source.FetchAsync(cts.Token).GetAwaiter().GetResult());
}
static SponsorAd Sponsor(Clock clock, int cap = 1) =>
    new(
        "Campaign",
        "https://ads.example.com/image.png",
        "https://ads.example.com/offer",
        clock.Now.AddDays(-1),
        3,
        cap,
        99
    );
static void Sponsors()
{
    var clock = new Clock();
    var source = new ConfigSource();
    var store = new LedgerStore();
    var ad = Sponsor(clock);
    source.Config = SponsorConfig.Empty with
    {
        Feed =
        [
            ad,
            ad with
            {
                Name = "Bad",
                RedirectUrl = "javascript:x",
            },
            ad with
            {
                Name = "Expired",
                Starts = clock.Now.AddDays(-4),
            },
        ],
    };
    using var repo = new SponsorRepository("one", source, store, clock, _ => 0);
    var first = repo.ReserveAsync(AdPlacement.Feed).Result!;
    Check(
        first.Ad.Name == "Campaign" && repo.ReserveAsync(AdPlacement.Feed).Result is null,
        "invalid selection or oversubscribed cap"
    );
    repo.ReleaseAsync(first.Id).GetAwaiter().GetResult();
    var second = repo.ReserveAsync(AdPlacement.Feed).Result!;
    Check(
        repo.MarkVisibleAsync(second.Id).Result && !repo.MarkVisibleAsync(second.Id).Result,
        "duplicate impression"
    );
    Check(repo.ReserveAsync(AdPlacement.Feed).Result is null, "cap ignored");
    using (var fresh = new SponsorRepository("one", source, store, clock))
        Check(fresh.ReserveAsync(AdPlacement.Feed).Result is null, "restart lost cap");
    clock.Now = clock.Now.AddDays(1);
    Check(
        repo.ReserveAsync(AdPlacement.Feed).Result is not null && source.Calls == 2,
        "daily rollover missing"
    );
    using var other = new SponsorRepository("two", source, store, clock);
    Check(other.ReserveAsync(AdPlacement.Feed).Result is not null, "accounts mixed");
    repo.Dispose();
    Throws<ObjectDisposedException>(() =>
        repo.ReserveAsync(AdPlacement.Feed).GetAwaiter().GetResult()
    );
}
static void LedgerPersistence()
{
    var preferences = new MemoryPreferences();
    var store = new PreferencesSponsorLedgerStore(preferences);
    var clock = new Clock();
    var config = SponsorConfig.Empty with { Feed = [Sponsor(clock)] };
    var ledger = new SponsorLedger(
        new DateOnly(2026, 10, 13),
        config,
        new() { ["Campaign"] = 2 },
        4
    );
    store.WriteAsync("a", ledger, default).GetAwaiter().GetResult();
    var read = store.ReadAsync("a", default).Result!;
    Check(
        read.Counts["Campaign"] == 2
            && read.Config!.Feed[0] == config.Feed[0]
            && read.Foregrounds == 4,
        "ledger serialization lost data"
    );
    Check(store.ReadAsync("b", default).Result is null, "account key collision");
    preferences.Values["fni.sponsors.v1.a"] = "{";
    Throws<System.Text.Json.JsonException>(() =>
        store.ReadAsync("a", default).GetAwaiter().GetResult()
    );
}
static void Frequency()
{
    using var repo = new SponsorRepository("a", new ConfigSource(), new LedgerStore(), new Clock());
    Check(
        Enumerable
            .Range(0, 6)
            .Select(_ => repo.NextForegroundAsync().Result)
            .SequenceEqual(new[] { true, true, false, true, false, true }),
        "foreground cadence mismatch"
    );
}
static void SponsorPlacement()
{
    var f = new AdFixture();
    f.Source.Config = SponsorConfig.Empty with { Feed = [Sponsor(f.Clock)] };
    var n = f.Notifier();
    f.Ui.Finish(n.LoadAsync());
    Check(
        n.State.Status == AdPlacementStatus.SponsorReady && f.Ads.Calls == 0,
        "sponsor first missing"
    );
    f.Ui.Finish(n.MarkSponsorVisibleAsync());
    f.Ui.Finish(n.MarkSponsorVisibleAsync());
    Check(f.Store.Rows["one"].Counts["Campaign"] == 1, "build/visible duplicates");
    f.Ui.Finish(n.DisposeAsync().AsTask());
    var g = new AdFixture();
    g.Source.Config = SponsorConfig.Empty with { Feed = [Sponsor(g.Clock)] };
    g.ImageFails = true;
    var retry = g.Notifier();
    g.Ui.Finish(retry.LoadAsync());
    Check(retry.State.Status == AdPlacementStatus.Error, "missing image error");
    g.ImageFails = false;
    g.Ui.Finish(retry.RetryAsync());
    Check(retry.State.Status == AdPlacementStatus.SponsorReady, "failed image consumed cap");
    g.Ui.Finish(retry.DisposeAsync().AsTask());
}
static void AdsGates()
{
    var f = new AdFixture();
    f.Consent.Allowed = false;
    var n = f.Notifier();
    f.Ui.Finish(n.LoadAsync());
    Check(
        n.State.Status == AdPlacementStatus.ConsentDenied && f.Ads.Calls == 0,
        "ad request without consent"
    );
    f.Consent.Allowed = true;
    f.Ads.Error = new PlatformNotSupportedException();
    f.Ui.Finish(n.RetryAsync());
    Check(n.State.Status == AdPlacementStatus.Unavailable, "unsupported success");
    f.Ads.Error = null;
    f.Ui.Finish(n.RetryAsync());
    Check(n.State.Status == AdPlacementStatus.AdReady, "ad retry missing");
    f.Ui.Finish(n.DisposeAsync().AsTask());
    Throws<ArgumentException>(() =>
        new TestAdRequest(AdPlacement.Feed, "ca-app-pub-3613334758684848/4359141314").Validate()
    );
}
static void AdDisposal()
{
    var f = new AdFixture();
    f.Ads.Hold = new();
    var n = f.Notifier();
    var load = n.LoadAsync();
    f.Ui.PumpUntil(() => f.Ads.Calls == 1);
    var duplicate = n.LoadAsync();
    Check(f.Ads.Calls == 1, "duplicate load");
    var suspend = n.SuspendAsync();
    var ad = new NativeAd();
    f.Ads.Hold.SetResult(ad);
    f.Ui.Finish(Task.WhenAll(load, duplicate, suspend));
    Check(
        ad.Disposals == 1 && n.State.Status == AdPlacementStatus.Idle,
        "late native object leaked or stale state"
    );
    f.Ads.Hold = null;
    n.Resume();
    f.Ui.Finish(n.LoadAsync());
    var current = f.Ads.Last!;
    f.Ui.Finish(n.DisposeAsync().AsTask());
    Check(current.Disposals == 1, "dispose missing");
    Throws<ObjectDisposedException>(() => n.LoadAsync());
}
static void CleanupRetry()
{
    var f = new AdFixture();
    var n = f.Notifier();
    f.Ui.Finish(n.LoadAsync());
    var ad = f.Ads.Last!;
    ad.FailDispose = true;
    Throws<IOException>(() => f.Ui.Finish(n.SuspendAsync()));
    Throws<InvalidOperationException>(n.Resume);
    ad.FailDispose = false;
    f.Ui.Finish(n.SuspendAsync());
    n.Resume();
    f.Ui.Finish(n.DisposeAsync().AsTask());
    Check(ad.Disposals == 2, "cleanup could not retry");
}
static void SponsorDismissal()
{
    var ui = new Dispatcher();
    var clock = new Clock();
    using var dismissal = new SponsorDismissalNotifier(ui, clock);
    dismissal.Start(3);
    var calls = 0;
    ui.Finish(
        dismissal.CloseAsync(() =>
        {
            calls++;
            return Task.CompletedTask;
        })
    );
    Check(calls == 0 && !dismissal.State.CanClose, "sponsor closed before countdown");
    clock.Now = clock.Now.AddSeconds(3);
    dismissal.Refresh();
    Check(dismissal.State.CanClose, "countdown did not expire");
    ui.Finish(dismissal.CloseAsync(() => Task.FromException(new IOException())));
    Check(
        dismissal.State.Error && dismissal.State.CanClose,
        "close failure swallowed or not retryable"
    );
    ui.Finish(
        dismissal.CloseAsync(() =>
        {
            calls++;
            return Task.CompletedTask;
        })
    );
    Check(calls == 1 && dismissal.State.Closed, "close retry failed");
    dismissal.Dispose();
    Throws<ObjectDisposedException>(() => dismissal.Start(2));
}
static void AppOpen()
{
    var f = new AdFixture();
    var n = f.Notifier(AdPlacement.AppOpen);
    f.Ui.Finish(n.LoadAsync());
    var ad = f.Ads.Last!;
    f.Clock.Now = f.Clock.Now.AddHours(4);
    f.Ui.Finish(n.ShowAppOpenAsync());
    Check(ad.Shows == 0 && ad.Disposals == 1, "expired app-open shown");
    f.Ui.Finish(n.LoadAsync());
    var second = f.Ads.Last!;
    f.Ui.Finish(n.ShowAppOpenAsync());
    Check(second.Shows == 1 && second.Disposals == 1, "fullscreen disposal missing");
    var calls = f.Ads.Calls;
    f.Ui.Finish(n.LoadAsync());
    Check(
        f.Ads.Calls == calls && n.State.Status == AdPlacementStatus.Suppressed,
        "same foreground reloaded an ad"
    );
    f.Ui.Finish(n.ShowPrivacyOptionsAsync());
    Check(f.Consent.PrivacyCalls == 1, "privacy hook missing");
    f.Ui.Finish(n.DisposeAsync().AsTask());
}

sealed class Dispatcher : IUiDispatcher
{
    readonly int thread = Environment.CurrentManagedThreadId;
    readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var a))
                a();
            Thread.Sleep(1);
        }
        if (!ready())
            throw new TimeoutException();
    }

    public void Finish(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        while (queue.TryDequeue(out var a))
            a();
    }
}

sealed class Fixture
{
    public Dispatcher Ui = new();
    public MapsFake Maps = new();
    public ShareFake Share = new();
    public LinksFake Links = new();
    public LocationsFake Locations = new();

    public PlatformActionsNotifier Actions() =>
        new(Ui, Maps, Share, Links, new UnavailablePdfViewer(), Locations);
}

sealed class MapsFake : IAppointmentMaps
{
    public IReadOnlyList<AppointmentMap> Installed = [AppointmentMap.Google];
    public Exception? Error;
    public int Opens;

    public Task<IReadOnlyList<AppointmentMap>> InstalledAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Installed);
    }

    public Task OpenAsync(AppointmentMap map, AppointmentLocation location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Opens++;
        return Error is null ? Task.CompletedTask : Task.FromException(Error);
    }
}

sealed class ShareFake : IPlatformShare
{
    public TaskCompletionSource? Hold;
    public Exception? Error;
    public Uri? Last;
    public int Calls;

    public Task ShareAsync(Uri uri, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Calls++;
        Last = uri;
        return Error is not null ? Task.FromException(Error) : Hold?.Task ?? Task.CompletedTask;
    }
}

sealed class LinksFake : IExternalLinks
{
    public Uri? Last;
    public int Calls;
    public Exception? Error;

    public Task OpenAsync(Uri uri, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Calls++;
        Last = uri;
        return Error is null ? Task.CompletedTask : Task.FromException(Error);
    }
}

sealed class LocationsFake : IAppointmentLocationSource
{
    public TaskCompletionSource<AppointmentLocation>? Hold;

    public Task<AppointmentLocation> LoadAsync(string id, CancellationToken token) =>
        Hold?.Task ?? Task.FromResult(new AppointmentLocation(44, 20, "Hall"));
}

sealed class Clock : TimeProvider
{
    public DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

sealed class ConfigSource : ISponsorConfigSource
{
    public SponsorConfig Config = SponsorConfig.Empty;
    public int Calls;

    public Task<SponsorConfig> FetchAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Config);
    }
}

sealed class LedgerStore : ISponsorLedgerStore
{
    public Dictionary<string, SponsorLedger> Rows = new();

    public Task<SponsorLedger?> ReadAsync(string account, CancellationToken token) =>
        Task.FromResult(Rows.GetValueOrDefault(account));

    public Task WriteAsync(string account, SponsorLedger ledger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Rows[account] = ledger;
        return Task.CompletedTask;
    }
}

sealed class Remote : IFirebaseRemoteConfig
{
    public string Json = "";
    public string? Key;
    public int Fetches;

    public Task FetchRemoteConfigAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Fetches++;
        return Task.CompletedTask;
    }

    public Task<string> GetRemoteStringAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        Key = key;
        return Task.FromResult(Json);
    }
}

sealed class Consent : IAdsConsent
{
    public bool Allowed = true;
    public int PrivacyCalls;

    public Task<bool> CanRequestAdsAsync(CancellationToken token) => Task.FromResult(Allowed);

    public Task ShowPrivacyOptionsAsync(CancellationToken token)
    {
        PrivacyCalls++;
        return Task.CompletedTask;
    }
}

sealed class NativeAd : IPlacementAd
{
    public int Disposals;
    public int Shows;
    public bool FailDispose;
    public Element? View => new VStack();

    public Task ShowAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Shows++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        if (FailDispose)
            throw new IOException("disposal failed");
        return ValueTask.CompletedTask;
    }
}

sealed class AdsFake : IPlacementAds
{
    public TaskCompletionSource<IPlacementAd>? Hold;
    public Exception? Error;
    public int Calls;
    public NativeAd? Last;

    public Task<IPlacementAd> LoadAsync(TestAdRequest request, CancellationToken token)
    {
        Calls++;
        return Error is not null
            ? Task.FromException<IPlacementAd>(Error)
            : Hold?.Task ?? Task.FromResult<IPlacementAd>(Last = new());
    }
}

sealed class AdFixture
{
    public Dispatcher Ui = new();
    public ConfigSource Source = new();
    public LedgerStore Store = new();
    public Clock Clock = new();
    public Consent Consent = new();
    public AdsFake Ads = new();
    public bool ImageFails;
    public SponsorRepository? Repo;

    public AdPlacementNotifier Notifier(AdPlacement placement = AdPlacement.Feed) =>
        new(
            Ui,
            placement,
            Repo ??= new("one", Source, Store, Clock),
            Consent,
            Ads,
            new(placement, "ca-app-pub-3940256099942544/6300978111"),
            (_, _) => Task.FromResult(ImageFails ? null : ImageSource.FromBytes([1, 2, 3])),
            Clock
        );
}

sealed class LocationApi : IApiClient
{
    public string Body = "{}";
    public string? Path;
    public bool Authenticated;
    public HttpStatusCode Status = HttpStatusCode.OK;

    public Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    )
    {
        token.ThrowIfCancellationRequested();
        Path = path;
        Authenticated = authenticated;
        return Task.FromResult(
            new HttpResponseMessage(Status) { Content = new StringContent(Body) }
        );
    }

    public Task<HttpResponseMessage> GetResponseAsync<T>(
        string p,
        T q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> GetAsync<T>(
        string p,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<R> GetAsync<Q, R>(
        string p,
        Q q,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<HttpResponseMessage> PostJsonAsync<T>(
        string p,
        T b,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<HttpResponseMessage> PutJsonAsync<T>(
        string p,
        T b,
        CancellationToken t,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();
}

sealed class MemoryPreferences : IPreferences
{
    public Dictionary<string, object> Values = new();

    public Task<T> GetAsync<T>(
        string key,
        T fallback,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Values.TryGetValue(key, out var value) ? (T)value : fallback);

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Values[key] = value!;
        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.ContainsKey(key));

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Values.Clear();
        return Task.CompletedTask;
    }
}
