using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

/// <summary>Must fetch authenticated authoritative data and check access, not just payload fields.
/// Return null for missing/forbidden targets. The returned action commits navigation synchronously
/// on the UI dispatcher, after the notifier checks account epoch and router readiness again.</summary>
public interface INotificationDestinationResolver
{
    Task<Action?> ResolveAsync(int userId, NotificationTarget target, CancellationToken token);
}

public enum NotificationNavigationStatus
{
    Idle,
    Waiting,
    Loading,
    Error,
    AccessDenied,
}

public sealed record NotificationNavigationState(
    NotificationNavigationStatus Status = NotificationNavigationStatus.Idle,
    int Pending = 0
);

/// <summary>UI-dispatcher entry points. Capture Epoch when receiving a native event, then pass it
/// to Enqueue: delayed callbacks from a prior login are rejected. Cold-start events use initial epoch 0.</summary>
public sealed class NotificationNavigationNotifier(
    IUiDispatcher dispatcher,
    INotificationDestinationResolver resolver
) : Notifier<NotificationNavigationState>(dispatcher, new()), IDisposable
{
    private readonly Queue<NotificationIntent> pending = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> seenOrder = new();
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private readonly SemaphoreSlim serial = new(1, 1);
    private CancellationTokenSource session = new();
    private int? account;
    private bool ready,
        disposed,
        hasAuthenticated;
    public int Epoch { get; private set; }

    public void SetAccount(int? userId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (userId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (account == userId)
            return;
        // Preserve initial launch queue only for the first restored/sign-in session.
        if (account is not null || hasAuthenticated)
        {
            Epoch++;
            session.Cancel();
            session.Dispose();
            session = new();
            pending.Clear(); // Keep delivery tombstones: a late native duplicate cannot cross accounts.
        }
        account = userId;
        hasAuthenticated |= userId.HasValue;
        SetState(
            new(
                pending.Count == 0
                    ? NotificationNavigationStatus.Idle
                    : NotificationNavigationStatus.Waiting,
                pending.Count
            )
        );
    }

    // Explicit logout while restore is pending must invalidate even an unauthenticated cold-start queue.
    public void InvalidateSession()
    {
        Epoch++;
        session.Cancel();
        session.Dispose();
        session = new();
        pending.Clear();
        account = null;
        hasAuthenticated = true;
        SetState(new());
    }

    public void SetRouterReady(bool value) => ready = value;

    public bool Enqueue(NotificationIntent intent, int receivedEpoch)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (receivedEpoch != Epoch || pending.Count >= 32 || seen.Contains(intent.DeliveryId))
            return false;
        // Once logged out, unbound callbacks must never be assigned to a later account.
        if (account is null && hasAuthenticated)
            return false;
        seen.Add(intent.DeliveryId);
        seenOrder.Enqueue(intent.DeliveryId);
        while (seenOrder.Count > 512)
            seen.Remove(seenOrder.Dequeue());
        pending.Enqueue(intent);
        SetState(new(NotificationNavigationStatus.Waiting, pending.Count));
        return true;
    }

    // Call after enqueue, authenticated session publication, router mount, resume, or explicit retry.
    public async Task DrainAsync(CancellationToken caller = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var version = Epoch;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, session.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        var token = linked.Token;
        var entered = false;
        try
        {
            await serial.WaitAsync(token);
            entered = true;
            while (!token.IsCancellationRequested)
            {
                NotificationIntent? next = null;
                int user = 0;
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (version != Epoch || !ready || account is null || pending.Count == 0)
                        return;
                    next = pending.Peek();
                    user = account.Value;
                    SetState(new(NotificationNavigationStatus.Loading, pending.Count));
                });
                if (next is null)
                    return;
                var commit = await resolver.ResolveAsync(user, next.Target, token);
                token.ThrowIfCancellationRequested();
                var stop = false;
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (
                        disposed
                        || version != Epoch
                        || account != user
                        || token.IsCancellationRequested
                    )
                    {
                        stop = true;
                        return;
                    }
                    if (!ready)
                    {
                        SetState(new(NotificationNavigationStatus.Waiting, pending.Count));
                        stop = true;
                        return;
                    }
                    commit?.Invoke();
                    pending.Dequeue();
                    SetState(
                        new(
                            commit is null
                                ? NotificationNavigationStatus.AccessDenied
                                : NotificationNavigationStatus.Idle,
                            pending.Count
                        )
                    );
                });
                if (stop)
                    return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await PublishError(version);
        }
        catch (Exception)
        {
            await PublishError(version);
        }
        finally
        {
            if (entered)
                serial.Release();
        }
    }

    private Task PublishError(int version) =>
        uiDispatcher.InvokeAsync(() =>
        {
            if (!disposed && version == Epoch)
                SetState(new(NotificationNavigationStatus.Error, pending.Count));
        });

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Epoch++;
        session.Cancel();
        session.Dispose();
        pending.Clear();
    }
}
