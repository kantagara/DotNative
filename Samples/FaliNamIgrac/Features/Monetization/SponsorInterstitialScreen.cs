using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed record SponsorDismissalState(
    int RemainingSeconds = 0,
    bool Started = false,
    bool Closing = false,
    bool Closed = false,
    bool Error = false
)
{
    public bool CanClose => Started && RemainingSeconds == 0 && !Closing && !Closed;
}

/// <summary>Flutter compatibility: its sponsor modal reuses campaign `duration` as display seconds.
/// Keep this explicit until the remote config has a separate display-duration field.</summary>
public sealed class SponsorDismissalNotifier(
    IUiDispatcher dispatcher,
    TimeProvider? timeProvider = null
) : Notifier<SponsorDismissalState>(dispatcher, new()), IDisposable
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly CancellationTokenSource lifetime = new();
    private DateTimeOffset closeAt;
    private bool disposed;

    public void Start(int durationSeconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (State.Started)
            return;
        if (durationSeconds is < 0 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        closeAt = clock.GetUtcNow().AddSeconds(durationSeconds);
        SetState(new(durationSeconds, Started: true));
        if (durationSeconds > 0)
            _ = TickAsync();
    }

    private async Task TickAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), clock, lifetime.Token);
                var finished = false;
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (disposed)
                    {
                        finished = true;
                        return;
                    }
                    Refresh();
                    finished = State.RemainingSeconds == 0;
                });
                if (finished)
                    return;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!State.Started)
            return;
        var seconds = (int)Math.Max(0, Math.Ceiling((closeAt - clock.GetUtcNow()).TotalSeconds));
        SetState(State with { RemainingSeconds = seconds });
    }

    public async Task<bool> CloseAsync(Func<Task> close)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Refresh();
        if (!State.CanClose)
            return false;
        SetState(State with { Closing = true, Error = false });
        try
        {
            await close();
            await uiDispatcher.InvokeAsync(() =>
            {
                if (!disposed)
                    SetState(State with { Closing = false, Closed = true });
            });
            return !disposed;
        }
        catch (Exception)
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (!disposed)
                    SetState(State with { Closing = false, Error = true });
            });
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel(); // Tick may still observe the cancellation source.
    }
}

public interface ISponsorInterstitialText
{
    string Close { get; }
    string Closing { get; }
    string CloseError { get; }
    string CloseIn(int seconds);
}

/// <summary>Mount as a modal only for AppOpen SponsorReady. Invoke OnVisibleAsync after
/// the image is actually visible. Owner must await ads.SuspendAsync before unmount/logout.</summary>
public sealed class SponsorInterstitialScreen(
    UiContext ui,
    AdPlacementNotifier ads,
    AdPlacementPanel content,
    SponsorDismissalNotifier dismissal,
    ISponsorInterstitialText text,
    Action onClosed
) : ScreenComponent(ui)
{
    private bool left;

    public async Task OnVisibleAsync(CancellationToken token = default)
    {
        var sponsor = ads.State.Sponsor;
        if (sponsor is null)
            return;
        await ads.MarkSponsorVisibleAsync(token);
        await dispatcher.InvokeAsync(() =>
        {
            if (!left && !token.IsCancellationRequested)
                dismissal.Start(
                    ads.State.Status == AdPlacementStatus.SponsorReady ? sponsor.Ad.DurationDays : 0
                );
        });
    }

    public async Task RequestCloseAsync()
    {
        if (await dismissal.CloseAsync(ads.SuspendAsync))
            await dispatcher.InvokeAsync(() =>
            {
                if (!left)
                    onClosed();
            });
    }

    public override Element Build()
    {
        var state = dismissal.State;
        var label =
            state.Closing ? text.Closing
            : state.CanClose ? text.Close
            : text.CloseIn(state.RemainingSeconds);
        return SafeArea(
            new VStack(
                content.Build(),
                new Button(label, () => _ = RequestCloseAsync())
                    .Enabled(state.CanClose)
                    .TextStyle(TextStyleFor())
                    .ForegroundColor(Accent)
                    .Height(48),
                new Text(state.Error ? text.CloseError : "")
                    .TextStyle(TextStyleFor())
                    .ForegroundColor(Ink)
            )
                .Spacing(12)
                .Padding(16)
                .HeightPercent(100)
                .BackgroundColor(theme.Background)
        );
    }

    public override void Leave()
    {
        left = true;
        dismissal.Dispose();
    }
}
