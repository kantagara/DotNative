using DotNative;
using DotNative.FilePicker;
using WidgetSamples.Services;
using WidgetSamples.Widgets;

namespace WidgetSamples.Screens;

public sealed class HomeScreen(
    ISampleInfo info,
    DemoStore store,
    Navigator navigator,
    NavigationActions actions,
    IFilePicker picker
) : Component, INavigationScreen
{
    private readonly State<float> _headerOpacity = new(1);
    private readonly State<bool> _headerVisible = new(true);
    private readonly State<string> _pickedFile = new("Choose a file using the system picker.");

    private async Task PickFile()
    {
        await using var file = await picker.PickAsync();
        if (file is null)
        {
            _pickedFile.Value = "Selection cancelled.";
            return;
        }
        await using var stream = await file.OpenReadAsync();
        var prefix = new byte[64 * 1024];
        int count = await stream.ReadAsync(prefix);
        _pickedFile.Value =
            $"{file.Name} · {file.Length?.ToString() ?? "unknown"} bytes · read {count} bytes";
    }

    private readonly AdaptiveDockSample _dock = new(navigator, store, actions);

    public ScreenOptions BuildOptions(BuildContext context) =>
        new()
        {
            Title = "Widget samples",
            Transition = new NavigationTransition
            {
                Kind = NavigationTransitionKind.Fade,
                Duration = TimeSpan.FromMilliseconds(130),
                Easing = TransitionEasing.EaseOutCubic,
            },
            TitleDisplay = NavigationTitleDisplay.Large,
            HeaderBackgroundOpacity = _headerOpacity.Value,
            HeaderVisible = _headerVisible.Value,
            Trailing =
            [
                new ToolbarAction("Add item", store.Add),
                new ToolbarAction(
                    "Cart",
                    () => actions.Run(() => navigator.PushAsync<CartScreen>("Your cart"))
                ),
            ],
        };

    private readonly InputSamples _inputs = new();
    private readonly LayoutSamples _layout = new();
    private readonly ImageSamples _images = new();
    private readonly ContentSamples _content = new();

    public override Element Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        var body = new ScrollView(
            new VStack(
                new Text(actions.Error.Value).TextColor(Color.Red).FontSize(14),
                new Text("TESTT").FontSize(12).TextColor(theme.Primary),
                new Text(info.Description).FontSize(16),
                new ThemePicker(store),
                new PrimaryButton(
                    "Virtual list · 10,000 items",
                    () => actions.Run(() => navigator.PushAsync<VirtualListScreen>("Virtual list"))
                ),
                new PrimaryButton("Choose a file", () => actions.Run(PickFile)),
                new Text(_pickedFile.Value).FontSize(14),
#if DOTNATIVE_IOS
                new Text($"Header background value · {_headerOpacity.Value:P0}").FontSize(16),
                new Slider(_headerOpacity.Value, value => _headerOpacity.Value = value),
#endif
                new Button(
                    _headerVisible.Value ? "Hide navigation bar" : "Show navigation bar",
                    () => _headerVisible.Value = !_headerVisible.Value
                ).Padding(8),
                new VStack(
                    new Text("One cart. Two screens. Two OS!").FontSize(22),
                    new Text($"Items in cart: {store.Count.Value}").FontSize(26),
                    new Text(
                        "Add items here or on the next screen. Both share the same cart."
                    ).FontSize(15),
                    new Button("Add item", store.Add).Padding(12).TextColor(theme.Primary),
                    new PrimaryButton(
                        "Open cart →",
                        () => actions.Run(() => navigator.PushAsync<CartScreen>("Your cart"))
                    ),
                    new Text($"Completed visits: {store.CompletedVisits.Value}").FontSize(14)
                )
                    .Padding(18)
                    .Spacing(12)
                    .BackgroundColor(store.Surface)
                    .CornerRadius(16),
                _inputs,
                _layout,
                _images,
                _content
            )
                .Padding(24)
                .Spacing(16)
        )
            .Grow()
            .Shrink()
            .BackgroundColor(theme.Background);
#if DOTNATIVE_WINDOWS
        return body;
#else
        return _dock.Wrap(body, context);
#endif
    }
}
