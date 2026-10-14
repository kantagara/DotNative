using DotNative;
using WidgetSamples.Services;
using WidgetSamples.Widgets;

namespace WidgetSamples.Screens;

public sealed class CartScreen(
    DemoStore store,
    CartVisit visit,
    Navigator navigator,
    NavigationActions actions
) : Component, INavigationScreen
{
    private readonly AdaptiveDockSample _dock = new(navigator, store, actions);

    public ScreenOptions BuildOptions(BuildContext context) =>
        new()
        {
            Title = "Your cart",
            TitleDisplay = NavigationTitleDisplay.Large,
            Transition = new NavigationTransition
            {
                Kind = NavigationTransitionKind.Fade,
                Duration = TimeSpan.FromMilliseconds(130),
                Easing = TransitionEasing.EaseOutCubic,
            },
        };

    public override Element Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        var body = new ScrollView(new VStack(
#if !DOTNATIVE_IOS && !DOTNATIVE_ANDROID && !DOTNATIVE_MACOS && !DOTNATIVE_WINDOWS && !DOTNATIVE_LINUX
                new Button(
                    "← Back to samples",
                    () =>
                        actions.Run(async () =>
                        {
                            await navigator.PopAsync();
                        })
                ).Padding(12).TextColor(theme.Primary),
                new Text("Your cart").FontSize(30),
#endif
                new Text("The same cart, wherever you are.").FontSize(16),
                new VStack(
                    new Text(actions.Error.Value).TextColor(Color.Red).FontSize(14),
                    new Text($"{store.Count.Value} items").FontSize(36),
                    new PrimaryButton("Add item", store.Add),
                    new Button("Remove item", store.Remove).Padding(12).TextColor(theme.Primary)
                )
                    .Padding(20)
                    .Spacing(16)
                    .BackgroundColor(store.Surface)
                    .CornerRadius(16),
                new Text("A note for this visit").FontSize(22),
                new TextField(visit.Note.Value, value => visit.Note.Value = value)
                {
                    Placeholder = "Type something…",
                }
                    .Padding(14)
                    .BackgroundColor(store.Surface)
                    .CornerRadius(12),
                new Text(
                    "Your cart stays when you go back. This note starts fresh each time you open the cart."
                ).FontSize(15),
                new ThemePicker(store)
            ).Padding(24).Spacing(16)).Grow().Shrink().BackgroundColor(theme.Background);
#if DOTNATIVE_WINDOWS
        return body;
#else
        return _dock.Wrap(body, context);
#endif
    }
}
