namespace DotNative;

/// <summary>Automatic uses the native desktop navigation host where supported.</summary>
public enum DesktopNavigationPresentation
{
    Automatic,
    Composed,
}

internal sealed record NativeDesktopNavigation : ContainerNode, IInteractive
{
    internal DockItem[] Items { get; }
    internal string Title { get; }
    internal int CurrentIndex { get; }
    internal bool IsOpen { get; }
    internal float PaneWidth { get; }
    internal ThemeMode ThemeMode { get; }
    internal Action<bool> OnOpenChanged { get; }

    internal NativeDesktopNavigation(
        Element body,
        DockItem[] items,
        string title,
        int currentIndex,
        bool isOpen,
        float width,
        ThemeMode themeMode,
        Action<bool> onOpenChanged
    )
        : base(body)
    {
        Items = items;
        Title = title;
        CurrentIndex = currentIndex;
        IsOpen = isOpen;
        PaneWidth = width;
        ThemeMode = themeMode;
        OnOpenChanged = onOpenChanged;
    }
}
