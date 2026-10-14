namespace DotNative;

[Flags]
public enum TransitionProperties
{
    None = 0,
    Opacity = 1,
    Transform = 2,
    Colors = 4,
    Border = 8,
    Corners = 16,
    Shadow = 32,
    All = Opacity | Transform | Colors | Border | Corners | Shadow,
}

public enum TransitionEasing
{
    Linear,
    EaseInOut,
    EaseOutCubic,
    EaseInCubic,
}

public sealed record StyleTransition
{
    public TimeSpan Duration { get; }
    public TransitionProperties Properties { get; }
    public TransitionEasing Easing { get; }

    public StyleTransition(
        TimeSpan duration,
        TransitionProperties properties = TransitionProperties.All,
        TransitionEasing easing = TransitionEasing.EaseInOut
    )
    {
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(duration));
        if ((properties & ~TransitionProperties.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(properties));
        if (!Enum.IsDefined(easing))
            throw new ArgumentOutOfRangeException(nameof(easing));
        Duration = duration;
        Properties = properties;
        Easing = easing;
    }
}

public static class TransitionStyling
{
    /// <summary>Animate subsequent visual changes. Null removes transitions and snaps to the target.</summary>
    public static T Transition<T>(this T node, StyleTransition? value)
        where T : Node, IVisualBox => (T)(node with { TransitionStyle = value });
}

internal readonly record struct Presentation(
    Style Style,
    VisualStyle Visual,
    BoxDecoration Decoration
)
{
    internal static Presentation Of(Node node) => new(node.Style, node.Visual, node.Decoration);

    internal static Presentation Interpolate(
        Presentation from,
        Presentation to,
        float progress,
        TransitionProperties properties
    )
    {
        if (progress >= 1)
            return to;
        bool Has(TransitionProperties p) => (properties & p) != 0;
        float Mix(float a, float b) => (float)((double)a + ((double)b - a) * progress);
        Color ColorMix(Color a, Color b) =>
            new(
                (byte)Math.Clamp(MathF.Round(Mix(a.R, b.R)), 0, 255),
                (byte)Math.Clamp(MathF.Round(Mix(a.G, b.G)), 0, 255),
                (byte)Math.Clamp(MathF.Round(Mix(a.B, b.B)), 0, 255),
                (byte)Math.Clamp(MathF.Round(Mix(a.A, b.A)), 0, 255)
            );
        BorderSide Side(BorderSide? side, VisualStyle v) =>
            side ?? new(v.BorderWidth, v.BorderColor);
        BorderSide? Edge(BorderSide? a, BorderSide? b)
        {
            if (a is null && b is null)
                return null;
            var x = Side(a, from.Visual);
            var y = Side(b, to.Visual);
            return y with { Width = Mix(x.Width, y.Width), Color = ColorMix(x.Color, y.Color) };
        }
        var style = to.Style;
        var visual = to.Visual;
        var decoration = to.Decoration;
        if (Has(TransitionProperties.Colors))
            style = style with
            {
                Background = ColorMix(from.Style.Background, to.Style.Background),
                Foreground = ColorMix(from.Style.Foreground, to.Style.Foreground),
            };
        if (Has(TransitionProperties.Opacity))
            visual = visual with { Opacity = Mix(from.Visual.Opacity, to.Visual.Opacity) };
        if (Has(TransitionProperties.Transform))
            visual = visual with
            {
                TranslationX = Mix(from.Visual.TranslationX, to.Visual.TranslationX),
                TranslationY = Mix(from.Visual.TranslationY, to.Visual.TranslationY),
                ScaleX = Mix(from.Visual.ScaleX, to.Visual.ScaleX),
                ScaleY = Mix(from.Visual.ScaleY, to.Visual.ScaleY),
                Rotation = Mix(from.Visual.Rotation, to.Visual.Rotation),
            };
        if (Has(TransitionProperties.Border))
        {
            visual = visual with
            {
                BorderWidth = Mix(from.Visual.BorderWidth, to.Visual.BorderWidth),
                BorderColor = ColorMix(from.Visual.BorderColor, to.Visual.BorderColor),
            };
            decoration = decoration with
            {
                Left = Edge(from.Decoration.Left, to.Decoration.Left),
                Top = Edge(from.Decoration.Top, to.Decoration.Top),
                Right = Edge(from.Decoration.Right, to.Decoration.Right),
                Bottom = Edge(from.Decoration.Bottom, to.Decoration.Bottom),
            };
        }
        if (Has(TransitionProperties.Corners))
        {
            style = style with
            {
                CornerRadius = Mix(from.Style.CornerRadius, to.Style.CornerRadius),
            };
            if (from.Decoration.Corners is not null || to.Decoration.Corners is not null)
            {
                var a =
                    from.Decoration.Corners
                    ?? new CornerRadii(
                        from.Style.CornerRadius,
                        from.Style.CornerRadius,
                        from.Style.CornerRadius,
                        from.Style.CornerRadius
                    );
                var b =
                    to.Decoration.Corners
                    ?? new CornerRadii(
                        to.Style.CornerRadius,
                        to.Style.CornerRadius,
                        to.Style.CornerRadius,
                        to.Style.CornerRadius
                    );
                decoration = decoration with
                {
                    Corners = new(
                        Mix(a.TopLeft, b.TopLeft),
                        Mix(a.TopRight, b.TopRight),
                        Mix(a.BottomRight, b.BottomRight),
                        Mix(a.BottomLeft, b.BottomLeft)
                    ),
                };
            }
        }
        if (Has(TransitionProperties.Shadow))
        {
            var a = from.Visual.Shadow;
            var b = to.Visual.Shadow;
            visual = visual with
            {
                Shadow = new(
                    ColorMix(a.Color, b.Color),
                    Mix(a.Blur, b.Blur),
                    Mix(a.OffsetX, b.OffsetX),
                    Mix(a.OffsetY, b.OffsetY)
                ),
            };
        }
        return new(style, visual, decoration);
    }
}
