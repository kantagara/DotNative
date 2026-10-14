using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public interface IAdsConsent
{
    Task<bool> CanRequestAdsAsync(CancellationToken token);
    Task ShowPrivacyOptionsAsync(CancellationToken token);
}

public sealed class UnavailableAdsConsent : IAdsConsent
{
    public Task<bool> CanRequestAdsAsync(CancellationToken token) =>
        Task.FromException<bool>(
            new PlatformNotSupportedException("The sibling ads plugin must expose UMP consent.")
        );

    public Task ShowPrivacyOptionsAsync(CancellationToken token) =>
        Task.FromException(
            new PlatformNotSupportedException(
                "The sibling ads plugin must expose the privacy options form."
            )
        );
}

public sealed record TestAdRequest(AdPlacement Placement, string UnitId)
{
    public void Validate()
    {
        if (
            !Enum.IsDefined(Placement)
            || !System.Text.RegularExpressions.Regex.IsMatch(
                UnitId ?? "",
                @"^ca-app-pub-3940256099942544/[0-9]{10}$"
            )
        )
            throw new ArgumentException("Feature validation accepts only Google demo ad units.");
    }
}

public interface IPlacementAd : IAsyncDisposable
{
    // Banner implementations return an embeddable native view; full-screen ads return null.
    Element? View { get; }
    Task ShowAsync(CancellationToken token);
}

public interface IPlacementAds
{
    // Returns after native onAdLoaded. Implementations must destroy late results on cancellation.
    Task<IPlacementAd> LoadAsync(TestAdRequest request, CancellationToken token);
}

public sealed class UnavailablePlacementAds : IPlacementAds
{
    public Task<IPlacementAd> LoadAsync(TestAdRequest request, CancellationToken token) =>
        Task.FromException<IPlacementAd>(
            new PlatformNotSupportedException(
                "Placement view/consent/disposal APIs require a sibling plugin release."
            )
        );
}

public enum AdPlacementStatus
{
    Idle,
    Loading,
    SponsorReady,
    AdReady,
    Showing,
    Suppressed,
    ConsentDenied,
    Unavailable,
    Error,
    Cancelled,
}

public sealed record AdPlacementState(
    AdPlacementStatus Status = AdPlacementStatus.Idle,
    SponsorReservation? Sponsor = null,
    ImageSource? Image = null,
    Element? AdView = null
);

/// <summary>One instance per mounted placement. Call SuspendAsync before leaving/background/logout;
/// dispose the shared SponsorRepository only when the authenticated session ends.</summary>
public sealed class AdPlacementNotifier(
    IUiDispatcher dispatcher,
    AdPlacement placement,
    SponsorRepository sponsors,
    IAdsConsent consent,
    IPlacementAds ads,
    TestAdRequest request,
    Func<string, CancellationToken, Task<ImageSource?>> loadImage,
    TimeProvider? timeProvider = null
) : Notifier<AdPlacementState>(dispatcher, new()), IAsyncDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private CancellationTokenSource epoch = new();
    private Task? running;
    private IPlacementAd? native;
    private SponsorReservation? reservation;
    private DateTimeOffset loadedAt;
    private int revision;
    private bool disposed;
    private bool suspended;
    private bool foregroundAllowed;
    private bool foregroundEvaluated;
    private bool foregroundConsumed;
    private bool visible;
    private Task? cleanup;

    public Task LoadAsync(CancellationToken token = default) =>
        Schedule(
            async ct =>
            {
                if (
                    State.Status == AdPlacementStatus.Error
                    && (native is not null || reservation is not null)
                )
                    await ReleaseAsync();
                if (native is not null || reservation is not null)
                    return;
                if (placement == AdPlacement.AppOpen)
                {
                    if (!foregroundEvaluated)
                    {
                        foregroundAllowed = await sponsors.NextForegroundAsync(ct);
                        ct.ThrowIfCancellationRequested();
                        foregroundEvaluated = true;
                    }
                    if (!foregroundAllowed || foregroundConsumed)
                    {
                        await Publish(new(AdPlacementStatus.Suppressed));
                        return;
                    }
                }
                await Publish(new(AdPlacementStatus.Loading));
                reservation = await sponsors.ReserveAsync(placement, ct);
                ct.ThrowIfCancellationRequested();
                if (reservation is { } selected)
                {
                    var image = await loadImage(selected.Ad.ImageUrl, ct);
                    ct.ThrowIfCancellationRequested();
                    if (image is null)
                        throw new IOException("Sponsor image failed to load.");
                    await Publish(new(AdPlacementStatus.SponsorReady, selected, image));
                    return;
                }
                if (!await consent.CanRequestAdsAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    await Publish(new(AdPlacementStatus.ConsentDenied));
                    return;
                }
                ct.ThrowIfCancellationRequested();
                request.Validate();
                if (request.Placement != placement)
                    throw new ArgumentException("Ad unit placement mismatch.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var loaded = await ads.LoadAsync(request, timeout.Token);
                if (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    await loaded.DisposeAsync();
                    throw new TimeoutException("Ad loading timed out.");
                }
                if (ct.IsCancellationRequested)
                {
                    await loaded.DisposeAsync();
                    ct.ThrowIfCancellationRequested();
                }
                native = loaded;
                loadedAt = clock.GetUtcNow();
                if (placement != AdPlacement.AppOpen && native.View is null)
                    throw new PlatformNotSupportedException("An embedded banner view is required.");
                await Publish(new(AdPlacementStatus.AdReady, AdView: native.View));
            },
            token
        );

    public Task MarkSponsorVisibleAsync(CancellationToken token = default) =>
        Schedule(
            async ct =>
            {
                if (
                    visible
                    || reservation is null
                    || State.Status != AdPlacementStatus.SponsorReady
                )
                    return;
                if (await sponsors.MarkVisibleAsync(reservation.Id, ct))
                {
                    visible = true;
                    if (placement == AdPlacement.AppOpen)
                        foregroundConsumed = true;
                }
            },
            token
        );

    public Task ShowAppOpenAsync(CancellationToken token = default) =>
        Schedule(
            async ct =>
            {
                if (
                    placement != AdPlacement.AppOpen
                    || native is null
                    || State.Status != AdPlacementStatus.AdReady
                )
                    return;
                if (clock.GetUtcNow() - loadedAt >= TimeSpan.FromHours(4))
                {
                    await ReleaseAsync();
                    await Publish(new(AdPlacementStatus.Idle));
                    return;
                }
                if (!await consent.CanRequestAdsAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    await ReleaseAsync();
                    await Publish(new(AdPlacementStatus.ConsentDenied));
                    return;
                }
                ct.ThrowIfCancellationRequested();
                await Publish(new(AdPlacementStatus.Showing));
                // A failed/cancelled show may already have presented native content. Never re-show
                // during the same foreground epoch without a fresh lifecycle transition.
                foregroundConsumed = true;
                await native.ShowAsync(ct);
                ct.ThrowIfCancellationRequested();
                await ReleaseAsync();
                await Publish(new(AdPlacementStatus.Idle));
            },
            token
        );

    public Task ShowPrivacyOptionsAsync(CancellationToken token = default) =>
        Schedule(
            async ct =>
            {
                await Publish(new(AdPlacementStatus.Loading));
                await ReleaseAsync();
                await consent.ShowPrivacyOptionsAsync(ct);
                ct.ThrowIfCancellationRequested();
                await Publish(new(AdPlacementStatus.Idle));
            },
            token
        );

    public Task RetryAsync(CancellationToken token = default) => LoadAsync(token);

    private Task Schedule(Func<CancellationToken, Task> operation, CancellationToken caller)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (suspended)
            throw new InvalidOperationException("Resume the placement before loading.");
        if (running is { IsCompleted: false })
            return running;
        return running = ExecuteAsync(operation, revision, caller);
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        int version,
        CancellationToken caller
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(epoch.Token, caller);
        try
        {
            await operation(linked.Token);
        }
        catch (Exception error)
        {
            try
            {
                await ReleaseAsync();
            }
            catch
            { /* Cleanup failure is reported below, never success. */
            }
            var status = error switch
            {
                OperationCanceledException => AdPlacementStatus.Cancelled,
                UnauthorizedAccessException => AdPlacementStatus.ConsentDenied,
                PlatformNotSupportedException => AdPlacementStatus.Unavailable,
                _ => AdPlacementStatus.Error,
            };
            await uiDispatcher.InvokeAsync(() =>
            {
                if (!disposed && version == revision)
                    SetState(new(status));
            });
        }
    }

    private Task Publish(AdPlacementState state)
    {
        var version = revision;
        return uiDispatcher.InvokeAsync(() =>
        {
            if (!disposed && !suspended && version == revision)
                SetState(state);
        });
    }

    private async Task ReleaseAsync()
    {
        if (native is { } ad)
        {
            await ad.DisposeAsync();
            native = null;
        }
        if (reservation is { } sponsor)
        {
            try
            {
                await sponsors.ReleaseAsync(sponsor.Id);
            }
            catch (ObjectDisposedException) { }
            reservation = null;
        }
        visible = false;
    }

    public Task SuspendAsync()
    {
        if (suspended)
            return cleanup is { IsFaulted: true } or { IsCanceled: true }
                ? cleanup = CleanupAsync()
                : cleanup ?? Task.CompletedTask;
        suspended = true;
        revision++;
        epoch.Cancel();
        SetState(new());
        return cleanup = CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        if (running is { } task)
            await task;
        await ReleaseAsync();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!suspended)
            return;
        if (cleanup is { IsCompletedSuccessfully: false })
            throw new InvalidOperationException("Await successful cleanup before resume.");
        epoch.Dispose();
        epoch = new();
        suspended = false;
        foregroundAllowed = false;
        foregroundEvaluated = false;
        foregroundConsumed = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            if (cleanup is { } task)
                await task;
            return;
        }
        await SuspendAsync();
        disposed = true;
        epoch.Dispose();
    }
}
