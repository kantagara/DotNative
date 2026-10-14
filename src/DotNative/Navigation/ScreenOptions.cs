namespace DotNative;

/// <summary>One toolbar command. Callbacks run on the application UI thread.</summary>
public sealed record ToolbarAction(string Title, Action Execute);

/// <summary>Native navigation title presentation.</summary>
public enum NavigationTitleDisplay
{
    Inline,
    Large,
}

/// <summary>Native screen transition presented by the navigation host.</summary>
public enum NavigationTransitionKind
{
    PlatformDefault,
    Slide,
    Fade,
    None,
}

/// <summary>Navigation animation kind and duration.</summary>
public sealed record NavigationTransition
{
    public NavigationTransitionKind Kind { get; init; } = NavigationTransitionKind.PlatformDefault;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(180);
    public TransitionEasing Easing { get; init; } = TransitionEasing.EaseInOut;
}

/// <summary>Screen header configuration. Null Title uses the navigation entry title;
/// an empty string hides only the title. Hiding the header does not hide system chrome.</summary>
public sealed record ScreenOptions
{
    internal bool CanGoBack { get; init; }
    public string? Title { get; init; }

    /// <summary>Large uses an iOS native title that collapses with the screen's root ScrollView. Other platforms use Inline.</summary>
    public NavigationTitleDisplay TitleDisplay { get; init; } = NavigationTitleDisplay.Inline;

    /// <summary>iOS-only background opacity, from 0 (transparent) to 1 (opaque). Titles and actions stay opaque.</summary>
    public float HeaderBackgroundOpacity { get; init; } = 1;
    public bool HeaderVisible { get; init; } = true;
    public bool BackButtonVisible { get; init; } = true;
    public NavigationTransition Transition { get; init; } = new();
    public IReadOnlyList<ToolbarAction> Leading { get; init; } = [];
    public IReadOnlyList<ToolbarAction> Trailing { get; init; } = [];

    internal ScreenOptions Snapshot(string title)
    {
        if (!Enum.IsDefined(TitleDisplay))
            throw new ArgumentOutOfRangeException(nameof(TitleDisplay));
        ArgumentNullException.ThrowIfNull(Transition);
        if (!Enum.IsDefined(Transition.Kind))
            throw new ArgumentOutOfRangeException(nameof(Transition));
        if (!Enum.IsDefined(Transition.Easing))
            throw new ArgumentOutOfRangeException(nameof(Transition));
        if (
            Transition.Duration < TimeSpan.Zero
            || Transition.Duration > TimeSpan.FromSeconds(10)
            || (
                Transition.Kind != NavigationTransitionKind.None
                && Transition.Duration == TimeSpan.Zero
            )
        )
            throw new ArgumentOutOfRangeException(nameof(Transition));
        if (
            !float.IsFinite(HeaderBackgroundOpacity)
            || HeaderBackgroundOpacity < 0
            || HeaderBackgroundOpacity > 1
        )
            throw new ArgumentOutOfRangeException(nameof(HeaderBackgroundOpacity));
        ArgumentNullException.ThrowIfNull(Leading);
        ArgumentNullException.ThrowIfNull(Trailing);
        if (Leading.Count + Trailing.Count > 32)
            throw new InvalidOperationException("A header supports at most 32 actions.");
        var actions = Leading.Concat(Trailing).ToArray();
        foreach (var action in actions)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentException.ThrowIfNullOrWhiteSpace(action.Title);
            ArgumentNullException.ThrowIfNull(action.Execute);
        }
        return this with
        {
            Title = Title ?? title,
            Transition = Transition with { },
            Leading = Leading.ToArray(),
            Trailing = Trailing.ToArray(),
        };
    }
}

/// <summary>Evaluated during rendering, so State reads update the native header.</summary>
public interface INavigationScreen
{
    ScreenOptions BuildOptions(BuildContext context);
}

internal sealed record NativeHeaderNode(ScreenOptions Options, Action Back) : Node, ILayoutable;
