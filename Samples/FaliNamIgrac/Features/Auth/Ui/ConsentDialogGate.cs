using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class ConsentDialogGate : ScreenComponent
{
    private readonly RegistrationFormNotifier form;
    private readonly Element child;

    public ConsentDialogGate(UiContext ui, RegistrationFormNotifier form, Element child)
        : base(ui)
    {
        this.form = form;
        this.child = child;
    }

    private Element Wrap(Element child)
    {
        if (!form.ConsentPopup)
            return child;
        var panel = new VStack(
            new Text(Loc.TermsAndPrivacyErrorBody)
                .TextStyle(TextStyleFor())
                .FontSize(13)
                .TextAlignment(TextAlignment.Center)
                .ForegroundColor(Color.Black)
                .Padding(20),
            new VStack().Height(.5f).BackgroundColor(new Color(0, 0, 0, 46)),
            new Button(Loc.Ok, () => form.ConsentPopup = false)
                .TextStyle(TextStyleFor())
                .FontSize(17)
                .ForegroundColor(new Color(0, 122, 255))
                .Height(45)
                .Width(270)
        )
            .Width(270)
            .BackgroundColor(new Color(242, 242, 242))
            .CornerRadius(14)
            .ClipToBounds(true);
        return new VStack(
            child,
            new VStack(
                new Button("", () => { })
                    .WidthPercent(100)
                    .HeightPercent(100)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(0),
                panel
            )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .BackgroundColor(new Color(0, 0, 0, 102))
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
        ).HeightPercent(100);
    }

    public override Element Build() => Wrap(child);
}
