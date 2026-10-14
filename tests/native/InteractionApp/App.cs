using System.Threading.Tasks;
using DotNative;

namespace InteractionApp;

public static class Program
{
    public static void Main() => DotNativeApplication.CreateBuilder().Build().Run<App>();
}

public sealed class App : Component
{
    private readonly State<bool> _selected = new(false);
    private readonly State<bool> _enabled = new(true);
    private readonly State<int> _selections = new(0);
    private readonly State<int> _clicks = new(0);

    public override Element Build() => new VStack(
            new Text($"Selections: {_selections.Value}").FontSize(20),
            new Text($"Clicks: {_clicks.Value}").FontSize(20),
            new RadioButton(
                "Test radio",
                _selected.Value,
                () =>
                {
                    _selected.Value = true;
                    _selections.Value++;
                }
            ).Height(60).LetterSpacing(1).LineHeight(24).MaxLines(2).TextOverflow(TextOverflow.EllipsisMiddle).Underline().BackgroundColor(new Color(255, 0, 0)).OnState(InteractionState.Hovered, new(Opacity: .8f)).OnState(InteractionState.Selected, new(Opacity: .65f)).OnState(InteractionState.Pressed, new(Opacity: .4f)).OnState(InteractionState.Disabled, new(Opacity: .2f)).Enabled(_enabled.Value),
            new Button("Test button", () => _clicks.Value++)
                .Height(60)
                .LetterSpacing(1)
                .LineHeight(24)
                .MaxLines(2)
                .TextOverflow(TextOverflow.EllipsisMiddle)
                .Strikethrough()
                .Shadow(Color.Black, 8)
                .ClipToBounds(false)
                .BackgroundColor(Color.Blue)
                .Border(4, Color.Blue)
                .OnState(InteractionState.Hovered, new(Opacity: .8f))
                .OnState(InteractionState.Pressed, new(Opacity: .4f)),
            new TextField("Test field", _ => { })
                .Height(50)
                .BackgroundColor(new Color(0, 128, 0))
                .Border(4, new Color(0, 128, 0))
                .OnState(InteractionState.Focused, new(Opacity: .55f))
                .OnState(InteractionState.Hovered, new(Opacity: .75f)),
            new Switch(false, _ => { })
                .Height(44)
                .BackgroundColor(new Color(0, 128, 0))
                .Border(4, new Color(0, 128, 0))
                .OnState(InteractionState.Focused, new(Opacity: .55f))
                .OnState(InteractionState.Hovered, new(Opacity: .7f)),
            new Slider(.5f, _ => { })
                .Height(44)
                .BackgroundColor(new Color(0, 128, 0))
                .Border(4, new Color(0, 128, 0))
                .OnState(InteractionState.Focused, new(Opacity: .55f))
                .OnState(InteractionState.Hovered, new(Opacity: .7f)),
            new Button(
                "Reset",
                () =>
                {
                    _selected.Value = false;
                    _enabled.Value = true;
                    _selections.Value = 0;
                    _clicks.Value = 0;
                }
            ).Height(50),
            new Button(
                "Disable soon",
                async () =>
                {
#if DOTNATIVE_IOS
                    // XCTest needs time to synthesize the following hold and capture its pressed pixels.
                    await Task.Delay(4000);
#else
                    await Task.Delay(500);
#endif
                    _enabled.Value = false;
                }
            ).Height(50),
            new VStack(
                new Text("Overflow marker")
                    .Width(80)
                    .Height(20)
                    .Position(PositionMode.Absolute)
                    .Left(-30)
                    .BackgroundColor(new Color(255, 0, 255))
            )
                .Width(20)
                .Height(24)
                .Position(PositionMode.Absolute)
                .Left(300)
                .Top(10)
                .ClipToBounds(false)
                .BackgroundColor(Color.Black),
            new Text("Transform anchor")
                .Width(80)
                .Height(30)
                .Position(PositionMode.Absolute)
                .Left(100)
                .Top(10)
                .Opacity(0),
            new VStack()
                .Width(80)
                .Height(30)
                .Position(PositionMode.Absolute)
                .Left(100)
                .Top(10)
                .BackgroundColor(new Color(255, 128, 0))
                .Scale(1.5f)
                .Rotate(15)
        ).Padding(24).Spacing(12).BackgroundColor(Color.White);
}
