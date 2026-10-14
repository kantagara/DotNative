using DotNative;

namespace FaliNamIgrac;

internal static class OrbitMotion
{
    // Values and the double ScreenUtil size scaling are preserved from the source widget.
    private static readonly (string Asset, float Radius, float Angle, float Size)[] Faces =
    [
        ("landing_circle_1", 180, 5 * MathF.PI / 3, 128),
        ("landing_circle_3", 180, .5f * MathF.PI / 3, 103),
        ("landing_circle_5", 180, 2 * MathF.PI / 3, 112),
        ("landing_circle_2", 120, 0, 71),
        ("landing_circle_6", 180, 4 * MathF.PI / 3, 110),
        ("landing_circle_4", 120, MathF.PI, 83),
    ];

    public static Element Build(
        float width,
        float height,
        AnimationController animation,
        ImageSource? decoration
    )
    {
        var w = width / 375;
        var h = Math.Max(height, 700) / 812;
        var r = Math.Min(w, h);
        var boxHeight = 426 * h;
        var ring = new Color(60, 94, 245, 102);
        return new VStack([
            new Image(FniAssets.Image("splash.png"))
                .Fit(ImageFit.Contain)
                .Width(200 * w)
                .Height(200 * h)
                .Position(PositionMode.Absolute)
                .Left((width - 200 * w) / 2)
                .Top((boxHeight - 200 * h) / 2),
            .. Ring(width / 2, boxHeight / 2, 254 * w / 2, 254 * h / 2, ring),
            .. Ring(width / 2, boxHeight / 2, 382 * w / 2, 382 * h / 2, ring),
            .. decoration is null
                ? []
                : new Element[]
                {
                    new Image(decoration)
                        .Fit(ImageFit.Fill)
                        .Width(width)
                        .Height(boxHeight)
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0),
                },
            .. Faces.Select(face => new OrbitingFace(face, width, boxHeight, animation, w, h, r)),
        ])
            .Width(width)
            .Height(boxHeight);
    }

    // Only the six image components subscribe to animation frames. Transforms
    // preserve layout and leave the landing copy, rings and modal tree untouched.
    private sealed class OrbitingFace(
        (string Asset, float Radius, float Angle, float Size) face,
        float width,
        float boxHeight,
        AnimationController animation,
        float w,
        float h,
        float r
    ) : Component
    {
        private readonly Image image = new Image(FniAssets.Image($"images/{face.Asset}.png"))
            .Fit(ImageFit.Contain)
            .Width(face.Size * r * w)
            .Height(face.Size * r * h)
            .Position(PositionMode.Absolute)
            .Left((width - face.Size * r * w) / 2)
            .Top((boxHeight - face.Size * r * h) / 2);

        public override Element Build()
        {
            var angle = animation.Value * MathF.Tau + face.Angle;
            return image.Translate(
                face.Radius * r * MathF.Cos(angle),
                face.Radius * r * MathF.Sin(angle)
            );
        }
    }

    // Flutter draws two dotted circles (dash 10, gap 10). A box border only
    // paints the 1px edge wedge, so the orbit reads as side ticks.
    private static Element[] Ring(float cx, float cy, float rx, float ry, Color color)
    {
        const float dash = 10;
        const float gap = 10;
        var circumference = MathF.PI * (3 * (rx + ry) - MathF.Sqrt((3 * rx + ry) * (rx + 3 * ry)));
        var count = Math.Max(8, (int)MathF.Round(circumference / (dash + gap)));
        return
        [
            .. Enumerable
                .Range(0, count)
                .Select(i =>
                {
                    var angle = i * MathF.Tau / count;
                    var x = cx + rx * MathF.Cos(angle);
                    var y = cy + ry * MathF.Sin(angle);
                    var tangent =
                        MathF.Atan2(ry * MathF.Cos(angle), -rx * MathF.Sin(angle)) * 180 / MathF.PI;
                    return (Element)
                        new VStack()
                            .Width(dash)
                            .Height(1)
                            .BackgroundColor(color)
                            .Rotate(tangent)
                            .Position(PositionMode.Absolute)
                            .Left(x - dash / 2)
                            .Top(y - 0.5f);
                }),
        ];
    }
}
