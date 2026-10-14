namespace DotNative;

/// <summary>A text run. Null styling inherits from the containing Text.</summary>
public sealed record TextRun(
    string Text,
    float? FontSize = null,
    Color? Foreground = null,
    FontWeight? Weight = null,
    bool? Italic = null,
    string? FontFamily = null,
    TextDecoration? Decoration = null
)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Text);
        if (FontSize is { } size && (!float.IsFinite(size) || size <= 0))
            throw new ArgumentOutOfRangeException(nameof(FontSize));
        new TypographyStyle(
            FontFamily: FontFamily ?? "",
            Weight: Weight ?? FontWeight.Normal,
            Decoration: Decoration ?? TextDecoration.None
        ).Validate();
    }
}
