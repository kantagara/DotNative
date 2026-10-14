namespace DotNative;

public enum GradientKind : byte
{
    Linear,
    Radial,
    Conic,
}

public readonly record struct GradientPoint(float X, float Y);

public readonly record struct GradientStop(float Offset, Color Color);

/// <summary>Immutable normalized gradient. Stops include 0 and 1 and are strictly increasing.</summary>
public sealed class Gradient : IEquatable<Gradient>
{
    public GradientKind Kind { get; }
    public GradientPoint Start { get; }
    public GradientPoint End { get; }
    public IReadOnlyList<GradientStop> Stops { get; }

    private Gradient(
        GradientKind kind,
        GradientPoint start,
        GradientPoint end,
        IEnumerable<GradientStop> stops
    )
    {
        ArgumentNullException.ThrowIfNull(stops);
        var copy = stops.Take(65).ToArray();
        if (
            copy.Length is < 2 or > 64
            || copy[0].Offset != 0
            || copy[^1].Offset != 1
            || !float.IsFinite(start.X)
            || !float.IsFinite(start.Y)
            || !float.IsFinite(end.X)
            || !float.IsFinite(end.Y)
            || start.X is < 0 or > 1
            || start.Y is < 0 or > 1
            || end.X is < 0 or > 1
            || end.Y is < 0 or > 1
        )
            throw new ArgumentOutOfRangeException(nameof(stops));
        for (int i = 1; i < copy.Length; i++)
            if (!float.IsFinite(copy[i].Offset) || copy[i].Offset <= copy[i - 1].Offset)
                throw new ArgumentOutOfRangeException(nameof(stops));
        if (kind == GradientKind.Linear && start == end)
            throw new ArgumentException("A linear gradient needs distinct endpoints.");
        if (kind == GradientKind.Radial && end.X <= 0)
            throw new ArgumentOutOfRangeException(nameof(end));
        Kind = kind;
        Start = start;
        End = end;
        Stops = Array.AsReadOnly(copy);
    }

    public static Gradient Linear(
        GradientPoint start,
        GradientPoint end,
        params GradientStop[] stops
    ) => new(GradientKind.Linear, start, end, stops);

    /// <summary>Radius is a fraction of the smaller box dimension.</summary>
    public static Gradient Radial(
        GradientPoint center,
        float radius,
        params GradientStop[] stops
    ) => new(GradientKind.Radial, center, new(radius, 0), stops);

    /// <summary>Clockwise, starting at twelve o'clock.</summary>
    public static Gradient Conic(GradientPoint center, params GradientStop[] stops) =>
        new(GradientKind.Conic, center, default, stops);

    public bool Equals(Gradient? other) =>
        other is not null
        && Kind == other.Kind
        && Start == other.Start
        && End == other.End
        && Stops.SequenceEqual(other.Stops);

    public override bool Equals(object? other) => other is Gradient gradient && Equals(gradient);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Start);
        hash.Add(End);
        foreach (var stop in Stops)
            hash.Add(stop);
        return hash.ToHashCode();
    }
}

public static class GradientStyling
{
    public static T BackgroundGradient<T>(this T node, Gradient? value)
        where T : ContainerNode => (T)(node with { Gradient = value });
}
