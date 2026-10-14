using DotNative;

namespace WidgetSamples.Widgets;

public sealed class ImageSamples : Component
{
    // Two colors, including a translucent half, make scaling and alpha tint visible.
    private static readonly ImageSource Source = ImageSource.FromBytes(
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAECAYAAACzzX7wAAAAFUlEQVR4nGP4z8DwHxkDUQMKpr0CAJB+N9GSCHBfAAAAAElFTkSuQmCC"
        )
    );
    private readonly State<ImageFit> _fit = new(ImageFit.Contain);
    private readonly State<bool> _tint = new(false);
    private readonly State<bool> _front = new(false);

    public override Element Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        return new VStack(
            new Text("Images & layers").FontSize(22).Bold(),
            new Button(
                $"Image fit: {_fit.Value} · change",
                () => _fit.Value = (ImageFit)(((int)_fit.Value + 1) % 5)
            ).Padding(10),
            new Button(
                _tint.Value ? "Restore image colors" : "Tint image",
                () => _tint.Value = !_tint.Value
            ).Padding(10),
            new Image(Source)
                .Width(160)
                .Height(100)
                .Fit(_fit.Value)
                .TintColor(_tint.Value ? theme.Primary : null)
                .Border(1, theme.Primary),
            new Container(new Text("Image background").Padding(12).TextColor(Color.White).Bold())
                .Height(100)
                .BackgroundImage(Source, _fit.Value)
                .CornerRadius(12),
            new Button("Swap front layer", () => _front.Value = !_front.Value).Padding(10),
            new VStack(
                new Text("First child")
                    .Padding(16)
                    .BackgroundColor(Color.Blue)
                    .TextColor(Color.White)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(0)
                    .ZIndex(_front.Value ? 2 : 0),
                new Text("Second child")
                    .Padding(16)
                    .BackgroundColor(Color.Red)
                    .TextColor(Color.White)
                    .Position(PositionMode.Absolute)
                    .Left(70)
                    .Top(25)
            ).Height(90)
        ).Spacing(10);
    }
}
