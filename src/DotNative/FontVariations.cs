namespace DotNative;

public readonly record struct FontAxis(string Tag, float Value);

/// <summary>OpenType variation coordinates. Axis tags are four ASCII letters/digits.</summary>
public sealed class FontVariationSettings : IEquatable<FontVariationSettings>
{
    public IReadOnlyList<FontAxis> Axes { get; }

    public FontVariationSettings(params FontAxis[] axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Length > 16)
            throw new ArgumentOutOfRangeException(nameof(axes));
        var copy = axes.OrderBy(a => a.Tag, StringComparer.Ordinal).ToArray();
        foreach (var axis in copy)
            if (
                axis.Tag is not { Length: 4 }
                || !axis.Tag.All(char.IsAsciiLetterOrDigit)
                || !float.IsFinite(axis.Value)
            )
                throw new ArgumentException("Invalid font axis.", nameof(axes));
        if (copy.Select(a => a.Tag).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Duplicate font axis.", nameof(axes));
        Axes = Array.AsReadOnly(copy);
    }

    public bool Equals(FontVariationSettings? other) =>
        other is not null && Axes.SequenceEqual(other.Axes);

    public override bool Equals(object? other) =>
        other is FontVariationSettings value && Equals(value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var axis in Axes)
            hash.Add(axis);
        return hash.ToHashCode();
    }
}

public static class VariableTypography
{
    /// <summary>Null resets the font's default axes. Unsupported axes follow native font fallback behavior.</summary>
    public static T FontVariations<T>(this T node, FontVariationSettings? settings)
        where T : Node, ITextual => (T)(node with { FontVariation = settings });
}
