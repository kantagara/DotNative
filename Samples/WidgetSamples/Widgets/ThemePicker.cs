using DotNative;
using WidgetSamples.Services;

namespace WidgetSamples.Widgets;

public sealed class ThemePicker(DemoStore store) : Component
{
    public override Element Build(BuildContext context) =>
        new VStack(
            new Text("Appearance").FontSize(16),
            new RadioGroup<ThemeMode>(
                store.Mode.Value,
                mode => store.Mode.Value = mode,
                new(ThemeMode.System, "System"),
                new(ThemeMode.Light, "Light"),
                new(ThemeMode.Dark, "Dark")
            )
            {
                Orientation = Axis.Horizontal,
            }
        ).Spacing(8);
}
