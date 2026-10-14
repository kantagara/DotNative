namespace DotNative;

public enum FlowDirection : byte
{
    Inherit,
    LeftToRight,
    RightToLeft,
}

public enum ContentAlignment : byte
{
    Default,
    Start,
    Center,
    End,
    Stretch,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

public enum GridTrackUnit : byte
{
    Auto,
    Points,
    Percent,
    Fraction,
}

public readonly record struct GridTrack(GridTrackUnit Unit, float Value = 0)
{
    public static GridTrack Auto => new(GridTrackUnit.Auto);

    public static GridTrack Points(float value) => Checked(GridTrackUnit.Points, value);

    public static GridTrack Percent(float value) => Checked(GridTrackUnit.Percent, value);

    public static GridTrack Fraction(float value = 1) => Checked(GridTrackUnit.Fraction, value);

    private static GridTrack Checked(GridTrackUnit unit, float value)
    {
        var track = new GridTrack(unit, value);
        track.Validate();
        return track;
    }

    internal void Validate()
    {
        Styling.Nonnegative(Value);
        if (
            !Enum.IsDefined(Unit)
            || (Unit == GridTrackUnit.Auto && Value != 0)
            || (Unit == GridTrackUnit.Fraction && Value == 0)
        )
            throw new ArgumentOutOfRangeException(nameof(GridTrack));
    }
}

/// <summary>Immutable explicit tracks. Missing rows/columns use intrinsic auto tracks.</summary>
public sealed class GridDefinition : IEquatable<GridDefinition>
{
    public IReadOnlyList<GridTrack> Columns { get; }
    public IReadOnlyList<GridTrack> Rows { get; }

    public GridDefinition(IEnumerable<GridTrack> columns, IEnumerable<GridTrack>? rows = null)
    {
        Columns = Copy(columns);
        Rows = Copy(rows ?? []);
    }

    private static IReadOnlyList<GridTrack> Copy(IEnumerable<GridTrack> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var tracks = values.Take(129).ToArray();
        if (tracks.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(values));
        foreach (var track in tracks)
            track.Validate();
        return Array.AsReadOnly(tracks);
    }

    public bool Equals(GridDefinition? other) =>
        other is not null && Columns.SequenceEqual(other.Columns) && Rows.SequenceEqual(other.Rows);

    public override bool Equals(object? other) => other is GridDefinition grid && Equals(grid);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var track in Columns)
            hash.Add(track);
        hash.Add(Columns.Count);
        foreach (var track in Rows)
            hash.Add(track);
        return hash.ToHashCode();
    }
}

public sealed record Grid : ContainerNode
{
    public Grid(GridDefinition definition, params Element[] children)
        : base(children)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AdvancedLayout = new(Grid: definition);
    }
}

public readonly record struct AdvancedLayoutStyle(
    FlowDirection FlowDirection = FlowDirection.Inherit,
    float? RowGap = null,
    float? ColumnGap = null,
    ContentAlignment AlignContent = ContentAlignment.Default,
    bool Reverse = false,
    float? MarginStart = null,
    float? MarginEnd = null,
    float? PaddingStart = null,
    float? PaddingEnd = null,
    float? InsetStart = null,
    float? InsetEnd = null,
    GridDefinition? Grid = null,
    int GridColumn = 0,
    int ColumnSpan = 1,
    int GridRow = 0,
    int RowSpan = 1
)
{
    public AdvancedLayoutStyle()
        : this(ColumnSpan: 1, RowSpan: 1) { }

    public static AdvancedLayoutStyle Default => new(ColumnSpan: 1, RowSpan: 1);

    internal void Validate()
    {
        if (
            !Enum.IsDefined(FlowDirection)
            || !Enum.IsDefined(AlignContent)
            || GridColumn is < 0 or > 128
            || GridRow is < 0 or > 128
            || ColumnSpan is < 1 or > 128
            || RowSpan is < 1 or > 128
            || GridColumn + ColumnSpan > 129
            || GridRow + RowSpan > 129
        )
            throw new ArgumentOutOfRangeException(nameof(AdvancedLayoutStyle));
        foreach (var value in new[] { RowGap, ColumnGap, PaddingStart, PaddingEnd })
            if (value is { } v)
                Styling.Nonnegative(v);
        foreach (var value in new[] { MarginStart, MarginEnd, InsetStart, InsetEnd })
            if (value is { } v && !float.IsFinite(v))
                throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public static class AdvancedLayoutExtensions
{
    /// <summary>Sibling painting order. Higher values appear on top; ties retain child order.</summary>
    public static T ZIndex<T>(this T node, int value)
        where T : Node, ILayoutable => (T)(node with { ZIndex = value });

    public static T FlowDirection<T>(this T node, FlowDirection value)
        where T : Node, ILayoutable =>
        Apply(node, node.AdvancedLayout with { FlowDirection = value });

    public static T RowGap<T>(this T node, float? value)
        where T : ContainerNode => Apply(node, node.AdvancedLayout with { RowGap = value });

    public static T ColumnGap<T>(this T node, float? value)
        where T : ContainerNode => Apply(node, node.AdvancedLayout with { ColumnGap = value });

    public static T AlignContent<T>(this T node, ContentAlignment value)
        where T : ContainerNode => Apply(node, node.AdvancedLayout with { AlignContent = value });

    public static T Reverse<T>(this T node, bool value = true)
        where T : ContainerNode => Apply(node, node.AdvancedLayout with { Reverse = value });

    public static T MarginInline<T>(this T node, float? start, float? end)
        where T : Node, ILayoutable =>
        Apply(node, node.AdvancedLayout with { MarginStart = start, MarginEnd = end });

    public static T PaddingInline<T>(this T node, float? start, float? end)
        where T : Node, IPaddable =>
        Apply(node, node.AdvancedLayout with { PaddingStart = start, PaddingEnd = end });

    public static T InsetInline<T>(this T node, float? start, float? end)
        where T : Node, ILayoutable =>
        Apply(node, node.AdvancedLayout with { InsetStart = start, InsetEnd = end });

    /// <summary>1-based column, or 0 for auto placement.</summary>
    public static T GridColumn<T>(this T node, int column, int span = 1)
        where T : Node, ILayoutable =>
        Apply(node, node.AdvancedLayout with { GridColumn = column, ColumnSpan = span });

    public static T GridRow<T>(this T node, int row, int span = 1)
        where T : Node, ILayoutable =>
        Apply(node, node.AdvancedLayout with { GridRow = row, RowSpan = span });

    private static T Apply<T>(T node, AdvancedLayoutStyle style)
        where T : Node
    {
        style.Validate();
        return (T)(node with { AdvancedLayout = style });
    }
}
