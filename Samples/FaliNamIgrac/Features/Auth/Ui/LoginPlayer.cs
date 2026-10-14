using DotNative;

namespace FaliNamIgrac;

internal static class LoginPlayer
{
    public static void Restart(UiContext ui)
    {
        ui.LoginAnimation.SnapTo(0);
        _ = Run(ui);
    }

    private static async Task Run(UiContext ui)
    {
        try
        {
            await ui.LoginAnimation.AnimateToAsync(
                1,
                TimeSpan.FromSeconds(2),
                cancellationToken: ui.Lifetime.Token
            );
        }
        catch (OperationCanceledException) when (ui.Lifetime.IsCancellationRequested) { }
    }
}
