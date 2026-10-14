using DotNative;

namespace FaliNamIgrac;

// Mirrors Flutter's game_created_popup.dart; mounted only after a confirmed save.
public sealed class GameSavedPopup(UiContext ui, bool isEdit, Action close) : ScreenComponent(ui)
{
    private readonly AnimationController entrance = new(ui.Dispatcher);

    public async void Enter()
    {
        try
        {
            await entrance.AnimateToAsync(
                1,
                TimeSpan.FromMilliseconds(300),
                t => t * t * (3 - 2 * t),
                lifetime.Token
            );
        }
        catch (OperationCanceledException) { }
    }

    public override void Leave() => entrance.Dispose();

    public override Element Build()
    {
        var size = presentation.Dimensions.Value ?? (375, 812);
        var r = Math.Max(.8f, Math.Min(size.Item1 / 375f, size.Item2 / 812f));
        var width = Math.Min(520, size.Item1 - 80 * r);
        var imageSize = width - 32 * r;
        return new VStack(
            new Button("", close)
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
                .BackgroundColor(new Color(0, 0, 0, 138)),
            new VStack(
                new Image(FniAssets.Image("success.png"))
                    .Fit(ImageFit.Contain)
                    .Width(imageSize)
                    .Height(imageSize * 612f / 623f),
                new VStack().Height(16 * r),
                new Text(isEdit ? Loc.GameUpdated : Loc.GameCreated)
                    .TextStyle(TextStyleFor("Bold"))
                    .FontSize(24 * r)
                    .ForegroundColor(Ink)
                    .TextAlignment(TextAlignment.Center),
                new VStack().Height(32 * r),
                new Button(Loc.Close, close)
                    .TextStyle(TextStyleFor("ExtraBold"))
                    .FontSize(16 * r)
                    .ForegroundColor(Color.White)
                    .TextAlignment(TextAlignment.Center)
                    .BackgroundColor(new Color(60, 94, 245))
                    .Height(56 * r)
                    .CornerRadius(16 * r)
            )
                .Padding(16 * r)
                .Width(width)
                .CornerRadius(16 * r)
                .BackgroundColor(theme.Background)
                .Margin(0, 24 * r + Insets.Bottom)
                .Translate(0, (1 - entrance.Value) * size.Item2)
        )
            .WidthPercent(100)
            .HeightPercent(100)
            .MainAlignment(MainAxisAlignment.End)
            .CrossAlignment(CrossAxisAlignment.Center)
            .WithKey("game-saved-popup");
    }
}
