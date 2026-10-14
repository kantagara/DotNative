using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class RegistrationErrorGate : ScreenComponent
{
    private readonly RegistrationFormNotifier form;
    private readonly Element child;

    public RegistrationErrorGate(UiContext ui, RegistrationFormNotifier form, Element child)
        : base(ui)
    {
        this.form = form;
        this.child = child;
    }

    private Element Wrap(Element child)
    {
        var width = Math.Min((presentation.Dimensions.Value?.Width ?? 375) - 80, 560);
        var title = new Text(Loc.RegistrationErrorTitle)
            .FontSize(24)
            .FontWeight(FontWeight.Bold)
            .ForegroundColor(Ink)
            .Width(width - 48);
        var content = new Text(Loc.RegistrationErrorSupportMessage)
            .FontSize(16)
            .ForegroundColor(Ink)
            .Width(width - 48);
        Button Action(string label) =>
            new Button(label, () => form.RegistrationError = null)
                .FontSize(14)
                .FontWeight(FontWeight.Medium)
                .ForegroundColor(theme.Primary)
                .Height(48);

        Element Dialog() =>
            new VStack(
                new Button("", () => form.RegistrationError = null)
                    .WidthPercent(100)
                    .HeightPercent(100)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(0),
                new VStack(
                    new VStack(title, new VStack().Height(16), content).Padding(24),
                    new VStack(Action(Loc.Cancel))
                        .CrossAlignment(CrossAxisAlignment.End)
                        .Padding(24, 0, 24, 24)
                )
                    .Width(width)
                    .BackgroundColor(theme.Surface)
                    .CornerRadius(28)
                    .ClipToBounds(true)
            )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .BackgroundColor(new Color(0, 0, 0, 138))
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0));
        return new VStack([
            child,
            .. form.RegistrationSnackbar is { } message
                ? new Element[]
                {
                    new Text(message)
                        .FontSize(14)
                        .FontWeight(FontWeight.Medium)
                        .ForegroundColor(theme.OnPrimary)
                        .Padding(16)
                        .BackgroundColor(Ink)
                        .CornerRadius(12)
                        .Position(PositionMode.Absolute)
                        .Left(15)
                        .Right(15)
                        .Bottom(Insets.Bottom + 15),
                }
                : [],
            .. form.RegistrationError is not null ? new Element[] { Dialog() } : [],
        ]).HeightPercent(100);
    }

    public override Element Build() => Wrap(child);
}
