namespace DotNative;

public enum MainAxisAlignment : byte
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

public enum CrossAxisAlignment : byte
{
    Stretch,
    Start,
    Center,
    End,
}

public enum Axis : byte
{
    Vertical,
    Horizontal,
}

public readonly record struct LayoutStyle(
    float? Width = null,
    float? Height = null,
    float Gap = 0,
    float FlexGrow = 0,
    Axis Direction = Axis.Vertical,
    MainAxisAlignment MainAlignment = MainAxisAlignment.Start,
    CrossAxisAlignment CrossAlignment = CrossAxisAlignment.Stretch,
    float FlexShrink = 0,
    EdgeInsets Margin = default,
    float? MinWidth = null,
    float? MinHeight = null,
    float? MaxWidth = null,
    float? MaxHeight = null,
    float? FlexBasis = null,
    FlexWrap Wrap = FlexWrap.NoWrap,
    SelfAlignment AlignSelf = SelfAlignment.Auto,
    EdgeInsets? PaddingEdges = null,
    LayoutBehavior Behavior = default
);

public abstract record ContainerNode : Node, ILayoutable, IVisualBox, IPaddable
{
    public IReadOnlyList<Element> Children { get; internal init; }

    protected ContainerNode(params Element[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (Array.Exists(children, child => child is null))
            throw new ArgumentException("Null child.");
        Children = Array.AsReadOnly((Element[])children.Clone());
    }
}

public sealed record HStack : ContainerNode
{
    public HStack(params Element[] children)
        : base(children)
    {
        Layout = new(Direction: Axis.Horizontal);
    }
}

public sealed record Container : ContainerNode
{
    public Container(Element? child = null)
        : base(child is null ? [] : [child]) { }
}

public sealed record SizedBox : ContainerNode
{
    public SizedBox(float? width = null, float? height = null, Element? child = null)
        : base(child is null ? [] : [child])
    {
        if (width is { } w)
            Styling.Nonnegative(w);
        if (height is { } h)
            Styling.Nonnegative(h);
        Layout = new(Width: width, Height: height);
    }
}

public sealed record Spacer : ContainerNode
{
    public Spacer(float flex = 1)
        : base()
    {
        if (!float.IsFinite(flex) || flex <= 0)
            throw new ArgumentOutOfRangeException(nameof(flex));
        Layout = new(FlexGrow: flex);
    }
}

public enum FlexWrap : byte
{
    NoWrap,
    Wrap,
    WrapReverse,
}

public enum SelfAlignment : byte
{
    Auto,
    Stretch,
    Start,
    Center,
    End,
}

/// <summary>Physical edge distances in logical pixels. Margins may be negative.</summary>
public readonly record struct EdgeInsets(float Left, float Top, float Right, float Bottom)
{
    public EdgeInsets(float all)
        : this(all, all, all, all) { }

    public EdgeInsets(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical) { }

    internal void Validate()
    {
        foreach (var value in new[] { Left, Top, Right, Bottom })
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public static class LayoutExtensions
{
    public static T Grow<T>(this T node, float value = 1)
        where T : Node, ILayoutable =>
        (T)(node with { Layout = node.Layout with { FlexGrow = Styling.Nonnegative(value) } });

    public static T Margin<T>(this T node, float all)
        where T : Node, ILayoutable => node.Margin(new EdgeInsets(all));

    public static T Margin<T>(this T node, float horizontal, float vertical)
        where T : Node, ILayoutable => node.Margin(new EdgeInsets(horizontal, vertical));

    public static T Margin<T>(this T node, float left, float top, float right, float bottom)
        where T : Node, ILayoutable => node.Margin(new EdgeInsets(left, top, right, bottom));

    public static T Margin<T>(this T node, EdgeInsets value)
        where T : Node, ILayoutable
    {
        value.Validate();
        return (T)(node with { Layout = node.Layout with { Margin = value } });
    }

    /// <summary>Null restores the intrinsic flex basis.</summary>
    public static T FlexBasis<T>(this T node, float? value)
        where T : Node, ILayoutable =>
        (T)(
            node with
            {
                Layout = node.Layout with
                {
                    FlexBasis = value is { } v ? Styling.Nonnegative(v) : null,
                },
            }
        );

    public static T MinWidth<T>(this T node, float? value)
        where T : Node, ILayoutable => Constrain(node, node.Layout with { MinWidth = value });

    public static T MinHeight<T>(this T node, float? value)
        where T : Node, ILayoutable => Constrain(node, node.Layout with { MinHeight = value });

    public static T MaxWidth<T>(this T node, float? value)
        where T : Node, ILayoutable => Constrain(node, node.Layout with { MaxWidth = value });

    public static T MaxHeight<T>(this T node, float? value)
        where T : Node, ILayoutable => Constrain(node, node.Layout with { MaxHeight = value });

    public static T AlignSelf<T>(this T node, SelfAlignment value)
        where T : Node, ILayoutable =>
        Enum.IsDefined(value)
            ? (T)(node with { Layout = node.Layout with { AlignSelf = value } })
            : throw new ArgumentOutOfRangeException(nameof(value));

    public static T Wrap<T>(this T node, FlexWrap value = FlexWrap.Wrap)
        where T : ContainerNode =>
        Enum.IsDefined(value)
            ? (T)(node with { Layout = node.Layout with { Wrap = value } })
            : throw new ArgumentOutOfRangeException(nameof(value));

    public static T AutoWidth<T>(this T node)
        where T : Node, ILayoutable =>
        (T)(
            node with
            {
                Layout = node.Layout with
                {
                    Width = null,
                    Behavior = node.Layout.Behavior with { WidthPercent = null },
                },
            }
        );

    public static T AutoHeight<T>(this T node)
        where T : Node, ILayoutable =>
        (T)(
            node with
            {
                Layout = node.Layout with
                {
                    Height = null,
                    Behavior = node.Layout.Behavior with { HeightPercent = null },
                },
            }
        );

    private static T Constrain<T>(T node, LayoutStyle layout)
        where T : Node, ILayoutable
    {
        ValidateExtended(layout);
        return (T)(node with { Layout = layout });
    }

    internal static void ValidateExtended(LayoutStyle layout)
    {
        layout.Behavior.Validate();
        layout.Margin.Validate();
        if (layout.PaddingEdges is { } padding)
            foreach (
                var value in new[] { padding.Left, padding.Top, padding.Right, padding.Bottom }
            )
                Styling.Nonnegative(value);
        foreach (
            var value in new[]
            {
                layout.MinWidth,
                layout.MinHeight,
                layout.MaxWidth,
                layout.MaxHeight,
                layout.FlexBasis,
            }
        )
            if (value is { } v)
                Styling.Nonnegative(v);
        if (
            layout.MinWidth > layout.MaxWidth
            || layout.MinHeight > layout.MaxHeight
            || !Enum.IsDefined(layout.Wrap)
            || !Enum.IsDefined(layout.AlignSelf)
        )
            throw new ArgumentOutOfRangeException(nameof(layout));
    }

    /// <summary>Allows this node to shrink when its parent has insufficient space.</summary>
    public static T Shrink<T>(this T node, float value = 1)
        where T : Node, ILayoutable =>
        (T)(node with { Layout = node.Layout with { FlexShrink = Styling.Nonnegative(value) } });

    public static T Width<T>(this T node, float value)
        where T : Node, ILayoutable =>
        (T)(
            node with
            {
                Layout = node.Layout with
                {
                    Width = Styling.Nonnegative(value),
                    Behavior = node.Layout.Behavior with { WidthPercent = null },
                },
            }
        );

    public static T Height<T>(this T node, float value)
        where T : Node, ILayoutable =>
        (T)(
            node with
            {
                Layout = node.Layout with
                {
                    Height = Styling.Nonnegative(value),
                    Behavior = node.Layout.Behavior with { HeightPercent = null },
                },
            }
        );

    public static T Spacing<T>(this T node, float value)
        where T : ContainerNode =>
        (T)(node with { Layout = node.Layout with { Gap = Styling.Nonnegative(value) } });

    public static T MainAlignment<T>(this T node, MainAxisAlignment value)
        where T : ContainerNode =>
        Enum.IsDefined(value)
            ? (T)(node with { Layout = node.Layout with { MainAlignment = value } })
            : throw new ArgumentOutOfRangeException(nameof(value));

    public static T CrossAlignment<T>(this T node, CrossAxisAlignment value)
        where T : ContainerNode =>
        Enum.IsDefined(value)
            ? (T)(node with { Layout = node.Layout with { CrossAlignment = value } })
            : throw new ArgumentOutOfRangeException(nameof(value));
}
