namespace DotNative;

public enum PositionMode : byte
{
    Relative,
    Absolute,
}

public enum Visibility : byte
{
    Visible,
    Hidden,
    Collapsed,
}

/// <summary>Optional physical offsets in logical pixels; negative offsets are allowed.</summary>
public readonly record struct PositionInsets(
    float? Left = null,
    float? Top = null,
    float? Right = null,
    float? Bottom = null
);

public readonly record struct LayoutBehavior(
    float? WidthPercent = null,
    float? HeightPercent = null,
    float? AspectRatio = null,
    PositionMode Position = PositionMode.Relative,
    Visibility Visibility = Visibility.Visible,
    PositionInsets Insets = default
)
{
    internal void Validate()
    {
        if (WidthPercent is { } w)
            Styling.Nonnegative(w);
        if (HeightPercent is { } h)
            Styling.Nonnegative(h);
        if (AspectRatio is { } ratio && (!float.IsFinite(ratio) || ratio <= 0))
            throw new ArgumentOutOfRangeException(nameof(AspectRatio));
        foreach (var value in new[] { Insets.Left, Insets.Top, Insets.Right, Insets.Bottom })
            if (value is { } v && !float.IsFinite(v))
                throw new ArgumentOutOfRangeException(nameof(Insets));
        if (!Enum.IsDefined(Position) || !Enum.IsDefined(Visibility))
            throw new ArgumentOutOfRangeException(nameof(LayoutBehavior));
    }
}

public static class Positioning
{
    /// <summary>Percentage of the containing block; 100 means its full width.</summary>
    public static T WidthPercent<T>(this T node, float value)
        where T : Node, ILayoutable =>
        Apply(node.AutoWidth(), node.Layout.Behavior with { WidthPercent = value });

    public static T HeightPercent<T>(this T node, float value)
        where T : Node, ILayoutable =>
        Apply(node.AutoHeight(), node.Layout.Behavior with { HeightPercent = value });

    public static T AspectRatio<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        Apply(node, node.Layout.Behavior with { AspectRatio = value });

    public static T Position<T>(this T node, PositionMode value)
        where T : Node, ILayoutable => Apply(node, node.Layout.Behavior with { Position = value });

    public static T Visibility<T>(this T node, Visibility value)
        where T : Node, ILayoutable =>
        Apply(node, node.Layout.Behavior with { Visibility = value });

    public static T Insets<T>(this T node, PositionInsets value)
        where T : Node, ILayoutable => Apply(node, node.Layout.Behavior with { Insets = value });

    public static T Left<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        node.Insets(node.Layout.Behavior.Insets with { Left = value });

    public static T Top<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        node.Insets(node.Layout.Behavior.Insets with { Top = value });

    public static T Right<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        node.Insets(node.Layout.Behavior.Insets with { Right = value });

    public static T Bottom<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        node.Insets(node.Layout.Behavior.Insets with { Bottom = value });

    private static T Apply<T>(T node, LayoutBehavior value)
        where T : Node, ILayoutable
    {
        value.Validate();
        return (T)(node with { Layout = node.Layout with { Behavior = value } });
    }
}
