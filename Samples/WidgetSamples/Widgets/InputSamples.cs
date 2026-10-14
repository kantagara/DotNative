using DotNative;

namespace WidgetSamples.Widgets;

public sealed class InputSamples : Component
{
    private readonly State<string> _name = new("DotNative");
    private readonly State<bool> _enabled = new(true);
    private readonly State<float> _volume = new(0.5f);
    private readonly FocusNode _focus = new();
    private readonly MobileFormsSample _forms = new();

    public override Element Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        return new VStack(
            new Text("Inputs").FontSize(22),
            new Text($"Hello, {_name.Value}!"),
            new TextField(_name.Value, value => _name.Value = value)
            {
                Placeholder = "Your name",
                Focus = _focus,
            }
                .Padding(14)
                .BackgroundColor(
                    theme.Background == Color.White
                        ? new Color(242, 244, 248)
                        : new Color(45, 47, 54)
                )
                .CornerRadius(12),
            new HStack(
                new Button("Focus", _focus.RequestFocus).Padding(12).TextColor(theme.Primary),
                new Button("Dismiss", _focus.Unfocus).Padding(12).TextColor(theme.Primary)
            ).Spacing(12),
            new HStack(
                new Text(_enabled.Value ? "Notifications on" : "Notifications off"),
                new Spacer(),
                new Switch(_enabled.Value, value => _enabled.Value = value)
            ).CrossAlignment(CrossAxisAlignment.Center),
            new Text($"Volume · {_volume.Value:P0}"),
            new Slider(_volume.Value, value => _volume.Value = value),
            _forms
        ).Spacing(12);
    }
}
