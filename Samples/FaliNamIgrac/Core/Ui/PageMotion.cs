using DotNative;

namespace FaliNamIgrac;

internal static class PageMotion
{
    internal readonly record struct Frame(
        float OutgoingX,
        float IncomingX,
        float OutgoingOpacity,
        float IncomingOpacity,
        float Shadow
    );

    public static Frame Evaluate(float time, bool back, bool ios)
    {
        time = Math.Clamp(time, 0, 1);
        if (!ios)
        {
            var slide = Emphasized(time);
            return back
                ? new(
                    .25f * slide,
                    -.25f * (1 - slide),
                    1 - Math.Clamp(time / .25f, 0, 1),
                    Math.Clamp(time / .75f, 0, 1),
                    0
                )
                : new(
                    -.25f * slide,
                    .25f * (1 - slide),
                    1 - Math.Clamp(time / .25f, 0, 1),
                    Math.Clamp(time / .75f, 0, 1),
                    0
                );
        }
        var primary = FastEase(time);
        var secondary = Cubic(time, .35f, .91f, .33f, .97f);
        return back
            ? new(
                primary,
                -Cubic(1 - time, .67f, .03f, .65f, .09f) / 3,
                1,
                1,
                Cubic(1 - time, .35f, .91f, .33f, .97f)
            )
            : new(-secondary / 3, 1 - primary, 1, 1, secondary);
    }

    // Original three-point curves, with the x coordinate inverted before evaluating y.
    private static float FastEase(float x) =>
        ThreePoint(x, .056f, .024f, .108f, .3085f, .198f, .541f, .3655f, 1, .5465f, .989f);

    private static float Emphasized(float x) =>
        ThreePoint(x, .05f, 0, .133333f, .06f, .166666f, .4f, .208333f, .82f, .25f, 1);

    private static float ThreePoint(
        float x,
        float x1,
        float y1,
        float x2,
        float y2,
        float midpointX,
        float midpointY,
        float x3,
        float y3,
        float x4,
        float y4
    ) =>
        x < midpointX
            ? midpointY
                * Cubic(
                    x / midpointX,
                    x1 / midpointX,
                    y1 / midpointY,
                    x2 / midpointX,
                    y2 / midpointY
                )
            : midpointY
                + (1 - midpointY)
                    * Cubic(
                        (x - midpointX) / (1 - midpointX),
                        (x3 - midpointX) / (1 - midpointX),
                        (y3 - midpointY) / (1 - midpointY),
                        (x4 - midpointX) / (1 - midpointX),
                        (y4 - midpointY) / (1 - midpointY)
                    );

    private static float Cubic(float x, float x1, float y1, float x2, float y2)
    {
        if (x <= 0 || x >= 1)
            return Math.Clamp(x, 0, 1);
        float low = 0,
            high = 1;
        for (var index = 0; index < 20; index++)
        {
            var t = (low + high) / 2;
            var result = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
            if (result < x)
                low = t;
            else
                high = t;
        }
        var u = (low + high) / 2;
        return 3 * (1 - u) * (1 - u) * u * y1 + 3 * (1 - u) * u * u * y2 + u * u * u;
    }
}
