namespace DotNative;

internal sealed record NativeNavigationNode(
    Element[] Screens,
    int Depth,
    ThemeMode Mode,
    NavigationTransition TransitionOptions,
    Action<int> Shown,
    Action<bool> Transition
) : ContainerNode(Screens);

internal sealed record NativeNavigationPage(Element Screen, ScreenOptions Options)
    : ContainerNode(Screen);

// Resolve the screen normally, then size its root to the remaining host space.
internal sealed record FillNavigationContent(Element Child, string Key) : Element;
