using DotNative;

namespace WidgetSamples.Services;

// Native button callbacks are synchronous. Observe asynchronous navigation
// failures here and marshal completion back to the UI thread.
public sealed class NavigationActions(IUiDispatcher dispatcher)
{
    private bool _busy;
    public State<string> Error { get; } = new("");

    public async void Run(Func<Task> operation)
    {
        if (_busy)
            return;
        _busy = true;
        Error.Value = "";
        string message = "";
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            message = error.Message;
        }
        finally
        {
            dispatcher.Post(() =>
            {
                Error.Value = message;
                _busy = false;
            });
        }
    }
}
