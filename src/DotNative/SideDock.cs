namespace DotNative;

public sealed record DockItem(string Title, Action OnClick, string? Icon = null)
{
    /// <summary>SF Symbols name for the native AppKit sidebar; null uses Icon text.</summary>
    public string? MacOSSymbol { get; init; }

    /// <summary>Segoe Fluent Icons glyph for the native WinUI sidebar; null uses Icon text.</summary>
    public string? WindowsGlyph { get; init; }
}

/// <summary>A native desktop sidebar, with a composed presentation for custom styling.</summary>
public sealed class SideDock : Component
{
    private readonly Element _content;
    private readonly DockItem[] _items;
    private readonly bool _open;
    private readonly Action<bool> _setOpen;

    public SideDock(
        Element content,
        IEnumerable<DockItem> items,
        bool isOpen,
        Action<bool> onOpenChanged
    )
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        foreach (var item in _items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Title);
            ArgumentNullException.ThrowIfNull(item.OnClick);
        }
        if (_items.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(items));
        _open = isOpen;
        _setOpen = onOpenChanged ?? throw new ArgumentNullException(nameof(onOpenChanged));
    }

    public DesktopNavigationPresentation Presentation { get; init; } =
        DesktopNavigationPresentation.Automatic;

    /// <summary>Null follows the application theme. System follows the OS; Light/Dark override it.</summary>
    public ThemeMode? Appearance { get; init; }
    public string Title { get; init; } = "Navigation";
    public int CurrentIndex { get; init; } = -1;
    public float ExpandedWidth { get; init; } = 260;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(260);
    public TransitionEasing Easing { get; init; } = TransitionEasing.EaseOutCubic;
    public float BottomInset { get; init; }

    public override Element Build(BuildContext context)
    {
        ArgumentNullException.ThrowIfNull(Title);
        if (Appearance is { } appearance && !Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(Appearance));
        if (!Enum.IsDefined(Presentation))
            throw new ArgumentOutOfRangeException(nameof(Presentation));
        Styling.Nonnegative(BottomInset);
        if (!float.IsFinite(ExpandedWidth) || ExpandedWidth < 180 || ExpandedWidth > 1000)
            throw new ArgumentOutOfRangeException(nameof(ExpandedWidth));
        if (CurrentIndex < -1 || CurrentIndex >= _items.Length)
            throw new ArgumentOutOfRangeException(nameof(CurrentIndex));
        var target = context.TryGet<PresentationTarget>(out var renderer)
            ? renderer
            : PresentationTarget.Local;
        if (
            Presentation == DesktopNavigationPresentation.Automatic
            && target.Platform is NativePlatform.MacOS or NativePlatform.Windows
        )
            return new NativeDesktopNavigation(
                _content,
                _items
                    .Select(item =>
                        item with
                        {
                            Icon =
                                target.Platform == NativePlatform.MacOS
                                && item.MacOSSymbol is { } symbol
                                    ? "sf:" + symbol
                                : target.Platform == NativePlatform.Windows
                                    ? item.WindowsGlyph ?? item.Icon
                                : item.Icon,
                        }
                    )
                    .ToArray(),
                Title,
                CurrentIndex,
                _open,
                ExpandedWidth,
                Appearance ?? Theme.Of(context).Mode,
                _setOpen
            ).Grow().Shrink().Padding(_open ? ExpandedWidth + 1 : 52, 0, 0, 0);
        var theme = Theme.Of(context);
        Color Shade(float amount) =>
            new(
                (byte)(theme.Background.R + (theme.Foreground.R - theme.Background.R) * amount),
                (byte)(theme.Background.G + (theme.Foreground.G - theme.Background.G) * amount),
                (byte)(theme.Background.B + (theme.Foreground.B - theme.Background.B) * amount)
            );
        var surface = Shade(0.04f);
        var selection = Shade(0.14f);
        var transition = new StyleTransition(
            Duration,
            TransitionProperties.Transform | TransitionProperties.Opacity,
            Easing
        );
        var rows = _items
            .Select(
                (item, index) =>
                    (Element)
                        new Button(
                            string.IsNullOrEmpty(item.Icon)
                                ? item.Title
                                : $"{item.Icon}   {item.Title}",
                            item.OnClick
                        )
                            .Padding(12, 10)
                            .FontSize(theme.FontSize)
                            .TextColor(theme.Foreground)
                            .BackgroundColor(index == CurrentIndex ? selection : Color.Transparent)
                            .CornerRadius(6)
                            .Enabled(_open)
                            .OnState(InteractionState.Hovered, new(Background: selection))
                            .OnState(
                                InteractionState.Focused,
                                new(BorderWidth: 2, BorderColor: theme.Primary)
                            )
                            .OnState(InteractionState.Pressed, new(Opacity: 0.7f))
                            .WithKey($"dock-item-{index}")
            )
            .ToArray();
        var panel = new VStack(
            new Text(Title).FontSize(20).TextColor(theme.Foreground).Padding(12),
            new ScrollView(new VStack(rows).Spacing(4)).Grow().Shrink()
        )
            .Padding(12)
            .BackgroundColor(surface)
            .Position(PositionMode.Absolute)
            .Left(0)
            .Top(56)
            .Bottom(BottomInset)
            .Width(ExpandedWidth)
            .Translate(_open ? 0 : -ExpandedWidth, 0)
            .Opacity(_open ? 1 : 0)
            .Transition(transition)
            .WithKey("dock-panel");
        var toggle = new Button("☰", () => _setOpen(!_open))
            .Padding(8)
            .Width(44)
            .Height(44)
            .TextColor(theme.Foreground)
            .CornerRadius(6)
            .OnState(InteractionState.Hovered, new(Background: selection))
            .Position(PositionMode.Absolute)
            .Left(4)
            .Top(6)
            .WithKey("dock-toggle");
        return new HStack(
            new Container(new SizedBox())
                .Width(_open ? ExpandedWidth : 52)
                .BackgroundColor(surface),
            panel,
            new Container(_content).Grow().Shrink().ClipToBounds(),
            toggle
        )
            .Grow()
            .Shrink()
            .BackgroundColor(theme.Background)
            .ClipToBounds();
    }
}
