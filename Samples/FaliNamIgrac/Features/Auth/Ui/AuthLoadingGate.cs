using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class AuthLoadingGate : ScreenComponent, IDisposable
{
    private readonly SessionNotifier session;
    private readonly RegistrationFormNotifier form;
    private readonly Element child;
    private readonly Func<bool>? preparingHome;
    private readonly AnimationController fade;
    private readonly State<bool> retained = new(false);
    private bool requested;
    private int fadeRevision;

    public AuthLoadingGate(
        UiContext ui,
        SessionNotifier session,
        RegistrationFormNotifier form,
        Element child,
        Func<bool>? preparingHome = null
    )
        : base(ui)
    {
        this.session = session;
        this.form = form;
        this.child = child;
        this.preparingHome = preparingHome;
        fade = new(ui.Dispatcher);
    }

    private bool IsAuthLoading =>
        (session.Busy && session.State is not SessionState.SignedIn)
        || form.RegistrationAuthenticating
        || preparingHome?.Invoke() == true;

    private Element Wrap(Element child)
    {
        // The reference gate blocks the whole route. Its AppLoading uses an
        // opaque scaffold background inside SafeArea, over a 45% black scrim.
        var busy = IsAuthLoading;
        if (requested != busy)
        {
            requested = busy;
            var revision = ++fadeRevision;
            dispatcher.Post(() =>
            {
                if (revision == fadeRevision)
                    _ = UpdateFadeAsync(busy, revision);
            });
        }
        var loading = busy || retained.Value ? presentation.Dimensions.Value : null;
        var opacity = busy ? 1 : fade.Value;
        return new VStack([
            child,
            .. loading is { } dimensions
                ? new Element[]
                {
                    new VStack(
                        new Button("", () => { })
                            .Width(dimensions.Width)
                            .Height(dimensions.Height)
                            .Position(PositionMode.Absolute)
                            .Left(0)
                            .Top(0),
                        new VStack(AppLoading(Loc.Loading))
                            .Width(dimensions.Width)
                            .Height(Math.Max(0, dimensions.Height - Insets.Top - Insets.Bottom))
                            .Position(PositionMode.Absolute)
                            .Left(0)
                            .Top(Insets.Top)
                    )
                        .Width(dimensions.Width)
                        .Height(dimensions.Height)
                        .BackgroundColor(new Color(0, 0, 0, 115))
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0)
                        .Opacity(opacity)
                        .WithKey("auth-loading-gate"),
                }
                : [],
        ])
            .HeightPercent(100)
            .WithKey("auth-gate-root");
    }

    private async Task UpdateFadeAsync(bool busy, int revision)
    {
        if (busy)
        {
            retained.Value = true;
            fade.SnapTo(1);
            return;
        }
        try
        {
            await fade.AnimateToAsync(
                0,
                TimeSpan.FromMilliseconds(320),
                easing: value => value * value * (3 - 2 * value),
                cancellationToken: lifetime.Token
            );
            await dispatcher.InvokeAsync(() =>
            {
                if (revision == fadeRevision)
                    retained.Value = false;
            });
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => fade.Dispose();

    public override Element Build() => Wrap(child);
}
