using DotNative;

namespace FaliNamIgrac;

internal static class LoginMotion
{
    public static Element Header(float width, float widthScale, float heightScale, float progress)
    {
        var height = 122 * heightScale;
        var outerScale = EaseOut(Interval(progress, .3f, .4f));
        var slide = EaseOut(Interval(progress, .4f, .6f));
        Element Circle(string asset, float size, float offset) =>
            new VStack(
                new Image(FniAssets.Image("images/" + asset + ".png"))
                    .Fit(ImageFit.Contain)
                    .Width(size * widthScale)
                    .Height(size * heightScale)
            )
                .Width(width)
                .Height(height)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Translate(width * offset * slide * outerScale, 0)
                .Scale(outerScale)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .ClipToBounds(false);

        var logoProgress = Interval(progress, 0, .3f);
        return new VStack(
            Circle("landing_circle_4", 53, -.4f),
            Circle("landing_circle_2", 53, .4f),
            Circle("landing_circle_1", 106, -.2f),
            Circle("landing_circle_5", 106, .2f),
            new VStack(
                new Image(FniAssets.Image("images/logo.png"))
                    .Fit(ImageFit.Contain)
                    .Width(122 * widthScale)
                    .Height(height)
            )
                .Width(width)
                .Height(height)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Opacity(logoProgress)
                .Scale(.5f + .5f * EaseOut(logoProgress))
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Width(width)
            .Height(height)
            .ClipToBounds(false);
    }

    private static float Interval(float value, float start, float end) =>
        Math.Clamp((value - start) / (end - start), 0, 1);

    // Original easeOut: cubic Bezier (0, 0, .58, 1), evaluated by its x coordinate.
    private static float EaseOut(float x)
    {
        float lo = 0,
            hi = 1;
        for (var i = 0; i < 20; i++)
        {
            var t = (lo + hi) / 2;
            var bx = 3 * (1 - t) * t * t * .58f + t * t * t;
            if (bx < x)
                lo = t;
            else
                hi = t;
        }
        var u = (lo + hi) / 2;
        return 3 * (1 - u) * u * u + u * u * u;
    }
}
