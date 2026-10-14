namespace DotNative;

/// <summary>Presents a component above the application tree, on every renderer.</summary>
public interface IOverlayPresenter
{
    Task<T> ShowAsync<T>(
        Func<Action<T>, Element> content,
        CancellationToken cancellationToken = default
    );
}

internal sealed class OverlayPresenter(IUiDispatcher dispatcher) : IOverlayPresenter, IDisposable
{
    internal readonly State<Element?> Content = new(null);
    private readonly SemaphoreSlim gate = new(1, 1);
    private Action? cancel;
    private bool disposed;
    private readonly CancellationTokenSource stopping = new();

    public async Task<T> ShowAsync<T>(
        Func<Action<T>, Element> content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stopping.Token
        );
        cancellationToken = linked.Token;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Element? entry = null;
        bool finished = false;
        void Finish(T value, bool cancelled = false)
        {
            if (finished)
                return;
            finished = true;
            Content.Value = null;
            (entry as IDisposable)?.Dispose();
            cancel = null;
            if (cancelled)
                completion.TrySetCanceled(
                    cancellationToken.IsCancellationRequested
                        ? cancellationToken
                        : new CancellationToken(true)
                );
            else
                completion.TrySetResult(value);
        }
        dispatcher.Post(() =>
        {
            if (disposed || cancellationToken.IsCancellationRequested)
            {
                Finish(default!, true);
                return;
            }
            try
            {
                cancel = () => Finish(default!, true);
                entry = content(value => Finish(value));
                if (finished)
                    (entry as IDisposable)?.Dispose();
                else
                    Content.Value = entry;
            }
            catch (Exception error)
            {
                cancel = null;
                completion.TrySetException(error);
            }
        });
        using var registration = cancellationToken.Register(() =>
            dispatcher.Post(() => Finish(default!, true))
        );
        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        disposed = true;
        stopping.Cancel();
        cancel?.Invoke();
    }
}

internal sealed class OverlayHost(Element root, OverlayPresenter presenter) : Component
{
    public override Element Build() =>
        new VStack(
            presenter.Content.Value is { } content
                ?
                [
                    root,
                    new Container(content)
                        .WidthPercent(100)
                        .HeightPercent(100)
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0),
                ]
                : [root]
        )
            .WidthPercent(100)
            .HeightPercent(100);
}
