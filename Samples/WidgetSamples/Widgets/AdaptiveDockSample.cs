using DotNative;
using WidgetSamples.Screens;
using WidgetSamples.Services;

namespace WidgetSamples.Widgets;

public sealed class AdaptiveDockSample(
    Navigator navigator,
    DemoStore store,
    NavigationActions actions
)
{
    private readonly State<bool> _open = new(true);
    private readonly State<int> _selected = new(0);

    private void Select(int index)
    {
        _selected.Value = index;
        switch (index)
        {
            case 0:
                if (navigator.CanPop)
                    actions.Run(async () =>
                    {
                        await navigator.PopAsync();
                    });
                break;
            case 1:
                actions.Run(() => navigator.PushAsync<CartScreen>("Your cart"));
                break;
            case 2:
                store.Mode.Value =
                    store.Mode.Value == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
                break;
            case 3:
                _open.Value = false;
                break;
        }
    }

    public Element Wrap(Element body, BuildContext context)
    {
#if DOTNATIVE_ANDROID || DOTNATIVE_IOS
        var theme = Theme.Of(context);
        return new Scaffold(
            new VStack(
                new Container(body).Grow().Shrink(),
                new Button("Show dock", () => _open.Value = true)
                    .Padding(12)
                    .BackgroundColor(theme.Primary)
                    .TextColor(theme.OnPrimary)
                    .CornerRadius(12)
                    .Position(PositionMode.Absolute)
                    .Right(12)
                    .Bottom(12)
                    .Visibility(_open.Value ? Visibility.Hidden : Visibility.Visible)
                    .Enabled(!_open.Value)
            )
                .Grow()
                .Shrink()
        )
        {
            BottomNavigationBar = new BottomNavigationBar(
                [
                    new("Back", BottomNavigationIcon.Home),
                    new("Cart", BottomNavigationIcon.Favorites),
                    new("Theme", BottomNavigationIcon.Settings),
                    new("Hide", BottomNavigationIcon.Profile),
                ],
                _selected.Value,
                Select
            ),
            BottomBarVisible = _open.Value,
            BottomInset = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = TransitionEasing.EaseOutCubic,
        };
#else
        return new SideDock(
            body,
            [
                new("Home", () => Select(0), "⌂")
                {
                    MacOSSymbol = "house",
                    WindowsGlyph = "\uE80F",
                },
                new("Cart", () => Select(1), "☆") { MacOSSymbol = "cart", WindowsGlyph = "\uE7BF" },
                new("Add item", store.Add, "+") { MacOSSymbol = "plus", WindowsGlyph = "\uE710" },
                new("Appearance", () => Select(2), "◐")
                {
                    MacOSSymbol = "circle.lefthalf.filled",
                    WindowsGlyph = "\uE790",
                },
            ],
            _open.Value,
            open => _open.Value = open
        )
        {
            Title = "Widget samples",
            CurrentIndex = navigator.CanPop ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = TransitionEasing.EaseOutCubic,
        };
#endif
    }
}
