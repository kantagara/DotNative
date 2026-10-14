namespace DotNative;

internal interface IStateDependency
{
    void Subscribe(Action invalidate);
    void Unsubscribe(Action invalidate);
}

internal static class BuildScope
{
    [ThreadStatic]
    internal static HashSet<IStateDependency>? Reads;

    [ThreadStatic]
    internal static Dictionary<Type, object?>? ContextReads;
}

// Provider/store integrations derive from this class. TrackRead registers a
// dependency; PublishChange invalidates readers. No renderer-specific API needed.
public abstract class ReactiveSource : IStateDependency
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly HashSet<Action> _subscribers = [];

    protected void TrackRead()
    {
        VerifyThread();
        BuildScope.Reads?.Add(this);
    }

    protected void VerifyCanWrite()
    {
        VerifyThread();
        if (BuildScope.Reads is not null)
            throw new InvalidOperationException("State mutation during Build.");
    }

    protected void PublishChange()
    {
        VerifyCanWrite();
        foreach (var invalidate in _subscribers.ToArray())
            invalidate();
    }

    private void VerifyThread()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("State access must run on its owning UI thread.");
    }

    void IStateDependency.Subscribe(Action invalidate)
    {
        VerifyThread();
        _subscribers.Add(invalidate);
    }

    void IStateDependency.Unsubscribe(Action invalidate)
    {
        VerifyThread();
        _subscribers.Remove(invalidate);
    }

    // External stores (for example a selected slice) observe this source without
    // reading it during Build. The callback runs on the owning UI thread.
    public IDisposable Listen(Action onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        VerifyCanWrite();
        ((IStateDependency)this).Subscribe(onChange);
        return new Listener(this, onChange);
    }

    private sealed class Listener(ReactiveSource source, Action onChange) : IDisposable
    {
        private ReactiveSource? _source = source;

        public void Dispose()
        {
            if (_source is not { } current)
                return;
            _source = null;
            ((IStateDependency)current).Unsubscribe(onChange);
        }
    }
}

// State belongs to the UI thread on which it was created. Workers must post
// updates to that thread. This prevents races between Build and subscription.
public sealed class State<T> : ReactiveSource
{
    private T _value;

    public State(T value) => _value = value;

    public T Value
    {
        get
        {
            TrackRead();
            return _value;
        }
        set
        {
            VerifyCanWrite();
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            PublishChange();
        }
    }
}

public interface IUiDispatcher
{
    bool CheckAccess();

    // Must enqueue (never invoke inline), execute on the OS main thread, and
    // surface callback exceptions to the host's error boundary.
    void Post(Action action);
}
