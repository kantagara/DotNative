namespace DotNative.Hosting;

// Await continuations are queued; never block the UI thread with Send/Wait.
public sealed class DispatcherSynchronizationContext : SynchronizationContext
{
    private readonly IUiDispatcher dispatcher;
    private readonly ApplicationSession? session;

    public DispatcherSynchronizationContext(IUiDispatcher dispatcher)
        : this(dispatcher, ApplicationSession.Ambient) { }

    internal DispatcherSynchronizationContext(IUiDispatcher dispatcher, ApplicationSession? session)
    {
        this.dispatcher = dispatcher;
        this.session = session;
    }

    public override void Post(SendOrPostCallback callback, object? state) =>
        dispatcher.Post(() => Run(() => callback(state)));

    public override SynchronizationContext CreateCopy() => this;

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (!dispatcher.CheckAccess())
            throw new NotSupportedException("Synchronous cross-thread dispatch is not supported.");
        Run(() => callback(state));
    }

    public void Run(Action action)
    {
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("UI thread required.");
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            if (session is null) action();
            else session.RunInContext(action);
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

public static class MainEntry
{
    public static Task Invoke(Action main)
    {
        main();
        return Task.CompletedTask;
    }

    public static Task Invoke(Func<Task> main) =>
        main() ?? throw new InvalidOperationException("Main returned a null Task.");
}
