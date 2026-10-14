namespace DotNative;

/// <summary>A screen body with optional native mobile bottom navigation.</summary>
public sealed class Scaffold(Element body) : Component
{
    public Element Body { get; } = body ?? throw new ArgumentNullException(nameof(body));
    public BottomNavigationBar? BottomNavigationBar { get; init; }
    public bool BottomBarVisible { get; init; } = true;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(240);
    public TransitionEasing Easing { get; init; } = TransitionEasing.EaseOutCubic;
    public float BottomInset { get; init; }

    public override Element Build(BuildContext context)
    {
        Styling.Nonnegative(BottomInset);
        var theme = Theme.Of(context);
        if (BottomNavigationBar is not { } bar)
            return new Container(Body).Grow().BackgroundColor(theme.Background);
        // Native measurement supplies the platform's own bar height. Keeping it
        // in flex layout reserves exactly that space without guessing pixels.
        return new VStack(
            new Container(Body).Grow().Shrink(),
            bar.Enabled(BottomBarVisible)
                .Translate(0, BottomBarVisible ? 0 : 160 + BottomInset)
                .Opacity(BottomBarVisible ? 1 : 0)
                .Transition(
                    new StyleTransition(
                        Duration,
                        TransitionProperties.Transform | TransitionProperties.Opacity,
                        Easing
                    )
                )
                .WithKey("scaffold-bottom-navigation"),
            new SizedBox(height: BottomInset)
        )
            .Grow()
            .BackgroundColor(theme.Background)
            .ClipToBounds();
    }
}
