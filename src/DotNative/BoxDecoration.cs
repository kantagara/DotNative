namespace DotNative;

public enum BorderLineStyle : byte
{
    Solid,
    Dashed,
    Dotted,
}

public readonly record struct BorderSide(
    float Width,
    Color Color,
    BorderLineStyle LineStyle = BorderLineStyle.Solid
)
{
    public static BorderSide None => default;

    internal void Validate()
    {
        Styling.Nonnegative(Width);
        if (!Enum.IsDefined(LineStyle))
            throw new ArgumentOutOfRangeException(nameof(LineStyle));
    }
}

/// <summary>Clockwise corner order: top-left, top-right, bottom-right, bottom-left.</summary>
public readonly record struct CornerRadii(
    float TopLeft,
    float TopRight,
    float BottomRight,
    float BottomLeft
)
{
    internal void Validate()
    {
        Styling.Nonnegative(TopLeft);
        Styling.Nonnegative(TopRight);
        Styling.Nonnegative(BottomRight);
        Styling.Nonnegative(BottomLeft);
    }
}

public readonly record struct BoxDecoration(
    CornerRadii? Corners = null,
    BorderSide? Left = null,
    BorderSide? Top = null,
    BorderSide? Right = null,
    BorderSide? Bottom = null
)
{
    internal void Validate()
    {
        Corners?.Validate();
        Left?.Validate();
        Top?.Validate();
        Right?.Validate();
        Bottom?.Validate();
    }
}

public static class BoxDecorationExtensions
{
    public static T CornerRadius<T>(
        this T node,
        float topLeft,
        float topRight,
        float bottomRight,
        float bottomLeft
    )
        where T : Node, IVisualBox =>
        node.CornerRadii(new(topLeft, topRight, bottomRight, bottomLeft));

    public static T CornerRadii<T>(this T node, CornerRadii? value)
        where T : Node, IVisualBox => Apply(node, node.Decoration with { Corners = value });

    public static T BorderLeft<T>(this T node, BorderSide? value)
        where T : Node, IVisualBox => Apply(node, node.Decoration with { Left = value });

    public static T BorderTop<T>(this T node, BorderSide? value)
        where T : Node, IVisualBox => Apply(node, node.Decoration with { Top = value });

    public static T BorderRight<T>(this T node, BorderSide? value)
        where T : Node, IVisualBox => Apply(node, node.Decoration with { Right = value });

    public static T BorderBottom<T>(this T node, BorderSide? value)
        where T : Node, IVisualBox => Apply(node, node.Decoration with { Bottom = value });

    public static T Border<T>(this T node, float width, Color color, BorderLineStyle style)
        where T : Node, IVisualBox
    {
        var side = new BorderSide(width, color, style);
        return Apply(
            node,
            node.Decoration with
            {
                Left = side,
                Top = side,
                Right = side,
                Bottom = side,
            }
        );
    }

    private static T Apply<T>(T node, BoxDecoration value)
        where T : Node, IVisualBox
    {
        value.Validate();
        return (T)(node with { Decoration = value });
    }
}
