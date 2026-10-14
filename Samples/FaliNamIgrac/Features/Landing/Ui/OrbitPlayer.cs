using DotNative;

namespace FaliNamIgrac;

internal sealed class OrbitPlayer : IDisposable
{
    private CancellationTokenSource? lifetime;

    public void Start(UiContext ui)
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(ui.Lifetime.Token);
        _ = Run(ui, lifetime.Token);
    }

    public void Dispose()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private static async Task Run(UiContext ui, CancellationToken token)
    {
        try
        {
            while (!ui.Lifetime.IsCancellationRequested && !token.IsCancellationRequested)
            {
                ui.OrbitAnimation.SnapTo(0);
                await ui.OrbitAnimation.AnimateToAsync(
                    1,
                    TimeSpan.FromSeconds(14),
                    cancellationToken: token
                );
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
