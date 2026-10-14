namespace DotNative;

public enum FontWeight : ushort
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900,
}

public enum TextAlignment : byte
{
    Left,
    Center,
    Right,
    Justify,
    Start,
    End,
}

[Flags]
public enum TextDecoration : byte
{
    None = 0,
    Underline = 1,
    Strikethrough = 2,
}

public enum TextOverflow : byte
{
    Default,
    Wrap,
    Clip,
    EllipsisStart,
    EllipsisMiddle,
    EllipsisEnd,
}

public readonly record struct TypographyStyle(
    string FontFamily = "",
    FontWeight Weight = FontWeight.Normal,
    bool Italic = false,
    TextAlignment Alignment = TextAlignment.Left,
    int? MaxLines = null,
    TextOverflow Overflow = TextOverflow.Default,
    float LetterSpacing = 0,
    float? LineHeight = null,
    TextDecoration Decoration = TextDecoration.None
)
{
    public TypographyStyle()
        : this(FontFamily: "", Weight: FontWeight.Normal) { }

    public static TypographyStyle Default => new(FontFamily: "", Weight: FontWeight.Normal);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(FontFamily);
        if (
            !float.IsFinite(LetterSpacing)
            || (LineHeight is { } height && (!float.IsFinite(height) || height <= 0))
            || (Decoration & ~(TextDecoration.Underline | TextDecoration.Strikethrough)) != 0
        )
            throw new ArgumentOutOfRangeException(nameof(TypographyStyle));
        if (
            FontFamily.Contains('\0')
            || FontFamily.Length > 1024
            || !Enum.IsDefined(Weight)
            || !Enum.IsDefined(Alignment)
            || !Enum.IsDefined(Overflow)
            || MaxLines < 0
        )
            throw new ArgumentOutOfRangeException(nameof(TypographyStyle));
    }
}

public static class Typography
{
    /// <summary>Native family name; null restores the system family. Missing fonts use the system fallback.</summary>
    public static T FontFamily<T>(this T node, string? value)
        where T : Node, ITextual =>
        (T)(
            Apply(node, node.Typography with { FontFamily = value ?? "" }) with
            {
                FontAsset = null,
            }
        );

    public static T FontWeight<T>(this T node, FontWeight value)
        where T : Node, ITextual => Apply(node, node.Typography with { Weight = value });

    public static T Bold<T>(this T node, bool value = true)
        where T : Node, ITextual =>
        node.FontWeight(value ? DotNative.FontWeight.Bold : DotNative.FontWeight.Normal);

    public static T Italic<T>(this T node, bool value = true)
        where T : Node, ITextual => Apply(node, node.Typography with { Italic = value });

    public static T TextAlignment<T>(this T node, TextAlignment value)
        where T : Node, ITextual => Apply(node, node.Typography with { Alignment = value });

    /// <summary>Zero means unlimited; null restores the native control default.</summary>
    public static T MaxLines<T>(this T node, int? value)
        where T : Node, ITextFlow => Apply(node, node.Typography with { MaxLines = value });

    public static T TextOverflow<T>(this T node, TextOverflow value)
        where T : Node, ITextFlow => Apply(node, node.Typography with { Overflow = value });

    public static T LetterSpacing<T>(this T node, float value)
        where T : Node, ITextFlow => Apply(node, node.Typography with { LetterSpacing = value });

    public static T LineHeight<T>(this T node, float? value)
        where T : Node, ITextFlow => Apply(node, node.Typography with { LineHeight = value });

    public static T TextDecoration<T>(this T node, TextDecoration value)
        where T : Node, ITextFlow => Apply(node, node.Typography with { Decoration = value });

    public static T Underline<T>(this T node, bool value = true)
        where T : Node, ITextFlow =>
        node.TextDecoration(
            value
                ? node.Typography.Decoration | DotNative.TextDecoration.Underline
                : node.Typography.Decoration & ~DotNative.TextDecoration.Underline
        );

    public static T Strikethrough<T>(this T node, bool value = true)
        where T : Node, ITextFlow =>
        node.TextDecoration(
            value
                ? node.Typography.Decoration | DotNative.TextDecoration.Strikethrough
                : node.Typography.Decoration & ~DotNative.TextDecoration.Strikethrough
        );

    private static T Apply<T>(T node, TypographyStyle style)
        where T : Node, ITextual
    {
        style.Validate();
        return (T)(node with { Typography = style });
    }
}
