using DotNative;
using DotNative.GoogleMobileAds;

namespace FaliNamIgrac;

// Own native leases at the application boundary; never load ads from a row's Build.
public sealed class MobileAdPlacements : IDisposable
{
    private readonly UiContext ui;
    private readonly SessionNotifier session;
    private readonly IPlacementMobileAds sdk;
    private readonly IAdsConsent consent;
    private readonly IApplicationLifecycle lifecycle;
    private readonly IDisposable follow;
    private readonly IBannerAd?[] leases = new IBannerAd?[7];
    private readonly State<Element?[]> views = new(new Element?[7]);
    private readonly SemaphoreSlim serial = new(1, 1);
    private CancellationTokenSource epoch = new();
    private string? account;
    private bool disposed,
        gathering,
        showing;
    private IAppOpenAd? appOpen;
    private readonly Func<bool> canShow;

    public MobileAdPlacements(
        UiContext ui,
        SessionNotifier session,
        IPlacementMobileAds sdk,
        IAdConsent ump,
        IApplicationLifecycle lifecycle,
        Func<bool> canShow
    )
    {
        this.ui = ui;
        this.session = session;
        this.sdk = sdk;
        this.lifecycle = lifecycle;
        this.canShow = canShow;
        consent = new NativeAdsConsentAdapter(
            ct => sdk.ConfigureAsync(new(), ct),
            async ct => (await ump.GatherAsync(ct)).CanRequestAds,
            async ct => (await ump.GetStatusAsync(ct)).CanRequestAds,
            sdk.InitializeAsync,
            async ct =>
            {
                await ump.ShowPrivacyOptionsAsync(ct);
            }
        );
        follow = session.Listen(FollowSession);
        lifecycle.Changed += ActivityChanged;
        FollowSession();
    }

    public Element Banner(int slot)
    {
        if (slot is < 0 or >= 7)
            return new VStack();
        // Reserve placement height while consent/loading is pending or inventory is unavailable.
        var view = views.Value[slot];
        return new VStack(view is null ? [] : new[] { view })
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center)
            .Height(120)
            .WidthPercent(100)
            .Margin(0, 8);
    }

    private void FollowSession()
    {
        var next = (session.State as SessionState.SignedIn)?.Profile.Id;
        if (next == account)
            return;
        account = next;
        Restart(next is not null);
    }

    private void ActivityChanged(ApplicationActivity state) =>
        ui.Dispatcher.Post(() =>
        {
            if (disposed || gathering || showing)
                return;
            if (state == ApplicationActivity.Active && account is not null)
                _ = ResumeAsync();
            else
                Restart(false);
        });

    private void Restart(bool load)
    {
        epoch.Cancel();
        epoch.Dispose();
        epoch = new();
        views.Value = new Element?[7];
        _ = ReloadAsync(load, epoch.Token);
    }

    private async Task ResumeAsync()
    {
        // Native background cleanup destroys hidden leases; refresh instead of reusing stale views.
        Restart(account is not null);
        await Task.CompletedTask;
    }

    private async Task ReloadAsync(bool load, CancellationToken token)
    {
        await serial.WaitAsync();
        try
        {
            for (var i = 0; i < leases.Length; i++)
            {
                if (leases[i] is { } old)
                {
                    await old.DisposeAsync();
                    leases[i] = null;
                }
            }
            if (appOpen is { } previous)
            {
                await previous.DisposeAsync();
                appOpen = null;
            }
            if (!load || disposed || token.IsCancellationRequested)
                return;
            gathering = true;
            bool allowed;
            try
            {
                allowed = await consent.CanRequestAdsAsync(token);
            }
            finally
            {
                gathering = false;
            }
            if (!allowed || token.IsCancellationRequested)
                return;
            // This validation build deliberately uses Google's demo units on physical devices too.
            var ios = ui.Target.Platform == NativePlatform.IOS;
            var bannerUnit = ios
                ? "ca-app-pub-3940256099942544/2934735716"
                : "ca-app-pub-3940256099942544/6300978111";
            for (var i = 0; i < leases.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var banner = await sdk.LoadBannerAsync(bannerUnit, timeout.Token);
                    if (token.IsCancellationRequested)
                    {
                        await banner.DisposeAsync();
                        token.ThrowIfCancellationRequested();
                    }
                    var slot = i;
                    leases[slot] = banner;
                    await ui.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;
                        var next = (Element?[])views.Value.Clone();
                        next[slot] = banner.View;
                        views.Value = next;
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(
                        "Banner load: "
                            + (
                                error is DotNative.Plugins.PluginException plugin
                                    ? plugin.Code + ": " + plugin.Message
                                    : error.GetType().Name
                            )
                    );
                }
            }
            // App-open only at a safe home boundary, never over an editor, chat or native form.
            using var adTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            adTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            appOpen = await sdk.LoadAppOpenAdAsync(
                ios
                    ? "ca-app-pub-3940256099942544/5575463023"
                    : "ca-app-pub-3940256099942544/9257395921",
                adTimeout.Token
            );
            var day = DateTime.Today.ToString("yyyy-MM-dd");
            var key = "fni.admob.foreground." + account + "." + day;
            var raw = await ui.Preferences.GetAsync(key, "0", token);
            _ = int.TryParse(raw, out var count);
            await ui.Preferences.SetAsync(key, (count + 1).ToString(), token);
            var safe = false;
            await ui.Dispatcher.InvokeAsync(() =>
                safe =
                    !token.IsCancellationRequested
                    && canShow()
                    && lifecycle.Activity == ApplicationActivity.Active
            );
            if (safe && (count == 0 || count % 2 == 1))
            {
                showing = true;
                try
                {
                    await appOpen.ShowAsync(token);
                }
                finally
                {
                    showing = false;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                "Ads initialization: "
                    + (
                        error is DotNative.Plugins.PluginException plugin
                            ? plugin.Code + ": " + plugin.Message
                            : error.GetType().Name
                    )
            );
        }
        finally
        {
            serial.Release();
        }
    }

    public async Task ShowPrivacyAsync()
    {
        gathering = true;
        try
        {
            await consent.ShowPrivacyOptionsAsync(ui.Lifetime.Token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                "Ads privacy: "
                    + (
                        error is DotNative.Plugins.PluginException plugin
                            ? plugin.Code + ": " + plugin.Message
                            : error.GetType().Name
                    )
            );
        }
        finally
        {
            gathering = false;
        }
        await ui.Dispatcher.InvokeAsync(() => Restart(account is not null));
    }

    public void Dispose()
    {
        disposed = true;
        follow.Dispose();
        lifecycle.Changed -= ActivityChanged;
        Restart(false);
    }
}
