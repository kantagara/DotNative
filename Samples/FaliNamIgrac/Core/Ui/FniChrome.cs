using DotNative;

namespace FaliNamIgrac;

// Stateless screen chrome. Call these from Build; do not inherit a second toolkit.
public static class FniChrome
{
    public static Element SafeArea(WindowInsets insets, IThemeData theme, Element content) =>
        new VStack(content)
            .Padding(0, insets.Top, 0, Math.Max(insets.Bottom, insets.Keyboard))
            .HeightPercent(100)
            .BackgroundColor(theme.Background);

    public static Element AppBar(PresentationTarget target, IThemeData theme, Action back) =>
        new HStack(
            new Button(target.Platform == NativePlatform.IOS ? "\uf570" : "\ue092", back)
                .TextStyle(Face(theme, "Icons"))
                .FontSize(24)
                .ForegroundColor(theme.Foreground)
                .Width(56)
                .Height(56)
        ).Height(56);

    public static TextStyle Face(IThemeData theme, string face = "Regular") =>
        face switch
        {
            "Bold" => theme.Text.Title,
            "Medium" => theme.Text.Label,
            "ExtraBold" => theme.Text.Strong,
            "Icons" => theme.Text.Icon,
            _ => theme.Text.Body,
        };

    public static Element SocialButtons(
        IThemeData theme,
        float w,
        float h,
        float r,
        Action google,
        Action apple
    )
    {
        Element Button(string asset, Action action, bool isApple)
        {
            var image = new Image(FniAssets.Image("icons/" + asset + ".png"))
                .Width(24 * w)
                .Height((isApple ? 20 : 24) * h);
            if (isApple)
                image = image.TintColor(theme.Foreground);
            return new VStack(
                new VStack(
                    image,
                    new Button("", action)
                        .Width(56 * w)
                        .Height(56 * h)
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0)
                )
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .Width(56 * w)
                    .Height(56 * h)
                    .BackgroundColor(theme.Surface)
                    .CornerRadius(16 * r)
            ).Padding(4);
        }
        return new HStack(Button("google", google, false), Button("apple", apple, true))
            .Spacing(16 * w)
            .MainAlignment(MainAxisAlignment.Center);
    }

    public static Element Busy(
        IPresentationState presentation,
        State<ImageSource?> image,
        AnimationController orbit,
        Color? color = null
    )
    {
        var w = (presentation.Dimensions.Value?.Width ?? 375) / 375;
        var h = Math.Max(presentation.Dimensions.Value?.Height ?? 812, 700) / 812;
        return new VStack()
            .Width(24 * w)
            .Height(24 * w)
            .Border(2.5f, color ?? Color.White)
            .BorderTop(new BorderSide(2.5f, Color.Transparent))
            .CornerRadius(12 * w)
            .Rotate(orbit.Value * 3780);
    }
}
