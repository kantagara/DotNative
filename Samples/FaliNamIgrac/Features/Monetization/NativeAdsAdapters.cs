using DotNative;

namespace FaliNamIgrac;

/// <summary>One consent coordinator per app lifetime, shared by all placements.
/// Delegates bind the released sibling plugin; configure/gather/initialize are ordered.
/// The caller supplies the screen/account lifetime token for every operation.</summary>
public sealed class NativeAdsConsentAdapter(
    Func<CancellationToken, Task> configure,
    Func<CancellationToken, Task<bool>> gather,
    Func<CancellationToken, Task<bool>> status,
    Func<CancellationToken, Task> initialize,
    Func<CancellationToken, Task> privacy
) : IAdsConsent
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private bool configured;
    private bool gathered;
    private bool initialized;

    public async Task<bool> CanRequestAdsAsync(CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            if (!configured)
            {
                await PlatformPluginErrors.InvokeAsync(() => configure(token));
                token.ThrowIfCancellationRequested();
                configured = true;
            }
            var allowed = await PlatformPluginErrors.InvokeAsync(() =>
                gathered ? status(token) : gather(token)
            );
            token.ThrowIfCancellationRequested();
            gathered = true;
            if (allowed && !initialized)
            {
                await PlatformPluginErrors.InvokeAsync(() => initialize(token));
                token.ThrowIfCancellationRequested();
                initialized = true;
            }
            return allowed;
        }
        finally
        {
            serial.Release();
        }
    }

    public async Task ShowPrivacyOptionsAsync(CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            if (!gathered)
                throw new InvalidOperationException(
                    "Gather consent before opening privacy options."
                );
            await PlatformPluginErrors.InvokeAsync(() => privacy(token));
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            serial.Release();
        }
    }
}

public sealed class NativePlacementAdsAdapter(
    Func<TestAdRequest, CancellationToken, Task<IPlacementAd>> load
) : IPlacementAds
{
    public async Task<IPlacementAd> LoadAsync(TestAdRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        request.Validate();
        var ad = await PlatformPluginErrors.InvokeAsync(() => load(request, token));
        if (token.IsCancellationRequested)
        {
            await ad.DisposeAsync();
            token.ThrowIfCancellationRequested();
        }
        return ad;
    }
}

/// <summary>Owns one banner/app-open handle from the sibling plugin. Disposal may be retried.</summary>
public sealed class NativePlacementAd(
    Element? view,
    Func<CancellationToken, Task> show,
    Func<ValueTask> dispose
) : IPlacementAd
{
    private Task? disposal;
    private bool disposed;
    public Element? View
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return view;
        }
    }

    public Task ShowAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return PlatformPluginErrors.InvokeAsync(() => show(token));
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        if (disposal is null || disposal.IsFaulted || disposal.IsCanceled)
            disposal = PlatformPluginErrors.InvokeAsync(() => dispose().AsTask());
        return new(disposal);
    }
}
