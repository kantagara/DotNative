using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

/// <summary>Serial UI operations, explicit cancellation and account-epoch suppression, including A/logout/A.</summary>
public abstract class ReportingNotifier<T> : Notifier<T>, IDisposable
{
    protected readonly ReportingSession Session;
    protected readonly TimeProvider Clock;
    private readonly IUiDispatcher dispatcher;
    private readonly T initial;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? active;
    private bool disposed;
    protected bool IsDisposed => disposed;

    protected ReportingNotifier(
        IUiDispatcher dispatcher,
        ReportingSession session,
        T initial,
        TimeProvider? clock = null
    )
        : base(dispatcher, initial)
    {
        this.dispatcher = dispatcher;
        Session = session;
        this.initial = initial;
        Clock = clock ?? TimeProvider.System;
        Session.Changed += Reset;
    }

    protected abstract T WithIssue(T state, ReportingIssue? issue, DateTimeOffset? retryAt);
    protected abstract DateTimeOffset? RetryAt(T state);

    protected virtual void Reset()
    {
        active?.Cancel();
        SetState(initial);
    }

    public void Cancel() => active?.Cancel();

    protected Task<bool> Execute(
        Func<ReportingAccount, CancellationToken, Task<T>> work,
        CancellationToken token
    ) =>
        RunExclusive(async () =>
        {
            if (disposed || token.IsCancellationRequested)
                return false;
            if (RetryAt(State) is { } retry && retry > Clock.GetUtcNow())
                return false;
            var version = Session.Version;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                lifetime.Token,
                Session.Token
            );
            active = linked;
            SetState(WithIssue(State, null, null));
            try
            {
                var account = await Session.CaptureAsync(linked.Token);
                var next = await work(account, linked.Token);
                var accepted = false;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested && version == Session.Version && !disposed)
                    {
                        SetState(next);
                        accepted = true;
                    }
                });
                return accepted;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception error)
            {
                var failure = error as ReportingException;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested && version == Session.Version && !disposed)
                        SetState(
                            WithIssue(
                                State,
                                failure?.Issue ?? ReportingIssue.Network,
                                failure?.Issue == ReportingIssue.RateLimited
                                    ? failure.RetryAt ?? Clock.GetUtcNow().AddSeconds(30)
                                    : null
                            )
                        );
                });
                return false;
            }
            finally
            {
                if (ReferenceEquals(active, linked))
                    active = null;
            }
        });

    protected Task PublishAsync(T state, CancellationToken token) =>
        dispatcher.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested && !disposed)
                SetState(state);
        });

    public virtual void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        active?.Cancel();
        Session.Changed -= Reset;
        lifetime.Dispose();
        SetState(initial);
    }
}
