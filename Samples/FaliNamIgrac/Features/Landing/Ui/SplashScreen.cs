using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class SplashScreen : ScreenComponent
{
    public SplashScreen(UiContext ui)
        : base(ui) { }

    public override Element Build() =>
        SafeArea(
            AppLoading(
                string.IsNullOrEmpty(presentation.StartupMessage.Value)
                    ? Loc.Loading
                    : presentation.StartupMessage.Value,
                presentation.StartupRetry.Value
            )
        );
}
