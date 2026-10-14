namespace DotNative;

/// <summary>Shared text styles supplied by ThemeData.</summary>
public sealed record TextTheme
{
    public TextStyle Body { get; init; } = new();
    public TextStyle Title { get; init; } = new() { Size = 24, Weight = FontWeight.Bold };
    public TextStyle Label { get; init; } = new() { Size = 14, Weight = FontWeight.Medium };
    public TextStyle Strong { get; init; } = new() { Weight = FontWeight.ExtraBold };
    public TextStyle Icon { get; init; } = new() { Size = 24 };
}

public sealed record TextStyle
{
    public FontSource? Font { get; init; }
    public float Size { get; init; } = 16;
    public FontWeight Weight { get; init; } = FontWeight.Normal;
    public Color? Foreground { get; init; }
}

public static class ThemedTypography
{
    public static T TextStyle<T>(this T node, TextStyle style)
        where T : Node, ITextual
    {
        ArgumentNullException.ThrowIfNull(style);
        var result = node.FontSource(style.Font).FontSize(style.Size).FontWeight(style.Weight);
        return style.Foreground is { } color ? result.ForegroundColor(color) : result;
    }
}
