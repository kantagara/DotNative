using DotNative;
using DotNative.Hosting;

namespace FaliNamIgrac;

internal static class UiDispatch
{
    public static void Run(this IUiDispatcher dispatcher, Action action)
    {
        void Invoke() => new DispatcherSynchronizationContext(dispatcher).Run(action);
        if (dispatcher.CheckAccess())
            Invoke();
        else
            dispatcher.Post(Invoke);
    }

    // Await the write itself, not merely its enqueueing, before releasing Busy.
    public static Task InvokeAsync(this IUiDispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dispatcher.Post(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        });
        return completion.Task;
    }
}
