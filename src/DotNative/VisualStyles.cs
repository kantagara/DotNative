namespace DotNative;

public readonly record struct BoxShadow(
    Color Color,
    float Blur = 0,
    float OffsetX = 0,
    float OffsetY = 0
);

public readonly record struct VisualStyle(
    float Opacity = 1,
    float BorderWidth = 0,
    Color BorderColor = default,
    BoxShadow Shadow = default,
    float TranslationX = 0,
    float TranslationY = 0,
    float ScaleX = 1,
    float ScaleY = 1,
    float Rotation = 0,
    bool ClipToBounds = true
)
{
    public VisualStyle()
        : this(Opacity: 1, ScaleX: 1, ScaleY: 1, ClipToBounds: true) { }

    public static VisualStyle Default => new(Opacity: 1, ScaleX: 1, ScaleY: 1, ClipToBounds: true);

    internal void Validate()
    {
        if (!float.IsFinite(Opacity) || Opacity < 0 || Opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        Styling.Nonnegative(BorderWidth);
        Styling.Nonnegative(Shadow.Blur);
        foreach (
            var value in new[]
            {
                Shadow.OffsetX,
                Shadow.OffsetY,
                TranslationX,
                TranslationY,
                ScaleX,
                ScaleY,
                Rotation,
            }
        )
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public static class VisualStyling
{
    public static T Opacity<T>(this T node, float value)
        where T : Node, IVisualBox => Apply(node, node.Visual with { Opacity = value });

    /// <summary>A border drawn inside the box; does not change layout size.</summary>
    public static T Border<T>(this T node, float width, Color color)
        where T : Node, IVisualBox =>
        Apply(node, node.Visual with { BorderWidth = width, BorderColor = color }) with
        {
            Decoration = node.Decoration with
            {
                Left = null,
                Top = null,
                Right = null,
                Bottom = null,
            },
        };

    /// <summary>Outer shadow. Use ClipToBounds(false) to let it extend beyond the box.</summary>
    public static T Shadow<T>(this T node, BoxShadow value)
        where T : Node, IVisualBox => Apply(node, node.Visual with { Shadow = value });

    public static T Shadow<T>(
        this T node,
        Color color,
        float blur,
        float offsetX = 0,
        float offsetY = 0
    )
        where T : Node, IVisualBox => node.Shadow(new BoxShadow(color, blur, offsetX, offsetY));

    public static T ClipToBounds<T>(this T node, bool value = true)
        where T : Node, IVisualBox => Apply(node, node.Visual with { ClipToBounds = value });

    public static T Translate<T>(this T node, float x, float y)
        where T : Node, IVisualBox =>
        Apply(node, node.Visual with { TranslationX = x, TranslationY = y });

    public static T Scale<T>(this T node, float value)
        where T : Node, IVisualBox => node.Scale(value, value);

    public static T Scale<T>(this T node, float x, float y)
        where T : Node, IVisualBox => Apply(node, node.Visual with { ScaleX = x, ScaleY = y });

    /// <summary>Clockwise degrees around the center; does not change layout size.</summary>
    public static T Rotate<T>(this T node, float degrees)
        where T : Node, IVisualBox => Apply(node, node.Visual with { Rotation = degrees });

    /// <summary>Visual projection about the box center, in logical pixels. Does not change layout.</summary>
    public static T ProjectiveTransform<T>(this T node, System.Numerics.Matrix4x4 matrix)
        where T : Node, IVisualBox
    {
        foreach (var value in ProjectionValues(matrix))
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(matrix));
        return (T)(node with { Projection = matrix });
    }

    internal static float[] ProjectionValues(System.Numerics.Matrix4x4 m) =>
        [
            m.M11,
            m.M12,
            m.M13,
            m.M14,
            m.M21,
            m.M22,
            m.M23,
            m.M24,
            m.M31,
            m.M32,
            m.M33,
            m.M34,
            m.M41,
            m.M42,
            m.M43,
            m.M44,
        ];

    private static T Apply<T>(T node, VisualStyle value)
        where T : Node, IVisualBox
    {
        value.Validate();
        return (T)(node with { Visual = value });
    }
}
