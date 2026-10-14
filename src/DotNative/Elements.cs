namespace DotNative;

public interface Element;

// Keep stateful component instances in fields. Build returns a fresh description;
// it must neither mutate state nor perform OS or application side effects.
// Clean retained instances reuse their last description. Inputs must therefore
// be immutable or reactive; ordinary field mutations require an explicit rebuild.
public abstract class Component : Element
{
    // Parameterless Build remains compatible with the first prototype.
    public virtual Element Build() =>
        throw new NotSupportedException("Override Build(BuildContext context).");

    public virtual Element Build(BuildContext context) => Build();
}

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static Color Black => new(0, 0, 0);
    public static Color Red => new(239, 68, 68);
    public static Color Blue => new(37, 99, 235);
    public static Color White => new(255, 255, 255);
    public static Color Transparent => new(0, 0, 0, 0);
}

public readonly record struct Style(
    float Padding = 0,
    Color Background = default,
    float CornerRadius = 0,
    Color Foreground = default,
    float FontSize = 17
);

public abstract record Node : Element
{
    public string? Key { get; init; }
    internal ControlProperties ResolvedControl { get; init; }
    internal bool HasForegroundOverride { get; init; }
    internal bool HasFontSizeOverride { get; init; }
    public ImageSource? BackgroundImageSource { get; internal init; }
    public ImageFit BackgroundImageFit { get; internal init; } = ImageFit.Cover;
    public bool AllowsFontScaling { get; internal init; } = true;
    public bool IsEnabled { get; internal init; } = true;
    internal InteractionRule[] InteractionRules { get; init; } = [];
    public StyleTransition? TransitionStyle { get; internal init; }
    public BoxDecoration Decoration { get; internal init; }
    public int ZIndex { get; internal init; }
    public Gradient? Gradient { get; internal init; }
    public AdvancedLayoutStyle AdvancedLayout { get; internal init; } = AdvancedLayoutStyle.Default;
    public LayoutStyle Layout { get; internal init; }
    internal System.Numerics.Matrix4x4 Projection { get; init; } =
        System.Numerics.Matrix4x4.Identity;
    public VisualStyle Visual { get; internal init; } = VisualStyle.Default;
    public FontSource? FontAsset { get; internal init; }
    public FontVariationSettings? FontVariation { get; internal init; }
    public TypographyStyle Typography { get; internal init; } = TypographyStyle.Default;
    public Style Style { get; internal init; } =
        new(0, Color.Transparent, 0, new Color(0, 0, 0), 17);
}

public sealed record VStack : ContainerNode
{
    public VStack(params Element[] children)
        : base(children) { }
}

public sealed record Text : Node, ILayoutable, IVisualBox, IPaddable, ITextual, ITextFlow
{
    public string Value { get; }
    internal TextRun[] Runs { get; init; } = [];

    public static Text Rich(params TextRun[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(runs));
        var copy = runs.ToArray();
        foreach (var run in copy)
        {
            ArgumentNullException.ThrowIfNull(run);
            run.Validate();
        }
        return new Text(string.Concat(copy.Select(r => r.Text))) { Runs = copy };
    }

    public Text(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed record Button : Node, IStateStyled, ILayoutable, IVisualBox, IPaddable, ITextFlow
{
    public string Title { get; }
    public Action OnClick { get; }

    public Button(string title, Action onClick)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        OnClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
    }
}

public static class Styling
{
    public static T WithKey<T>(this T node, string key)
        where T : Node =>
        Copy(node, node with { Key = key ?? throw new ArgumentNullException(nameof(key)) });

    public static T Padding<T>(this T node, float value)
        where T : Node, IPaddable =>
        Copy(
            node,
            node with
            {
                Style = node.Style with { Padding = Nonnegative(value) },
                Layout = node.Layout with { PaddingEdges = null },
            }
        );

    public static T Padding<T>(this T node, float horizontal, float vertical)
        where T : Node, IPaddable => node.Padding(new EdgeInsets(horizontal, vertical));

    public static T Padding<T>(this T node, float left, float top, float right, float bottom)
        where T : Node, IPaddable => node.Padding(new EdgeInsets(left, top, right, bottom));

    public static T Padding<T>(this T node, EdgeInsets value)
        where T : Node, IPaddable
    {
        foreach (var edge in new[] { value.Left, value.Top, value.Right, value.Bottom })
            Nonnegative(edge);
        return (T)(node with { Layout = node.Layout with { PaddingEdges = value } });
    }

    public static T BackgroundColor<T>(this T node, Color value)
        where T : Node, IVisualBox =>
        Copy(node, node with { Style = node.Style with { Background = value } });

    public static T CornerRadius<T>(this T node, float value)
        where T : Node, IVisualBox =>
        Copy(
            node,
            node with
            {
                Style = node.Style with { CornerRadius = Nonnegative(value) },
                Decoration = node.Decoration with { Corners = null },
            }
        );

    public static T ForegroundColor<T>(this T node, Color value)
        where T : Node, ITextual =>
        Copy(
            node,
            node with
            {
                HasForegroundOverride = true,
                Style = node.Style with { Foreground = value },
            }
        );

    public static T FontSize<T>(this T node, float value)
        where T : Node, ITextual =>
        Copy(
            node,
            node with
            {
                HasFontSizeOverride = true,
                Style = node.Style with { FontSize = Positive(value) },
            }
        );

    public static T TextColor<T>(this T node, Color value)
        where T : Node, ITextual => node.ForegroundColor(value);

    public static T TintColor<T>(this T node, Color value)
        where T : Node, ITintable =>
        Copy(
            node,
            node with
            {
                HasForegroundOverride = true,
                Style = node.Style with { Foreground = value },
            }
        );

    private static T Copy<T>(T _, Node copy)
        where T : Node => (T)copy;

    internal static float Nonnegative(float value) =>
        float.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));

    private static float Positive(float value) =>
        value > 0 ? Nonnegative(value) : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class PrimaryButton(string title, Action onClick, string? key = null) : Component
{
    public override Element Build() => Styled(ThemeData.Light);

    public override Element Build(BuildContext context) => Styled(Theme.Of(context));

    private Element Styled(IThemeData theme) =>
        new Button(title, onClick)
            .Padding(16)
            .BackgroundColor(theme.Primary)
            .ForegroundColor(theme.OnPrimary)
            .FontSize(theme.FontSize)
            .CornerRadius(theme.CornerRadius)
            .OnState(InteractionState.Hovered, new(Opacity: 0.88f))
            .OnState(InteractionState.Pressed, new(Opacity: 0.72f))
            .OnState(InteractionState.Disabled, new(Opacity: 0.45f))
            .Transition(
                new StyleTransition(TimeSpan.FromMilliseconds(120), TransitionProperties.Opacity)
            ) with
        {
            Key = key,
        };
}
