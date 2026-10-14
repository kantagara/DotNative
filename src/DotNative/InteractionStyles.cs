namespace DotNative;

public interface IInteractive;

public interface IStateStyled : IInteractive, IVisualBox;

[Flags]
public enum InteractionState
{
    None = 0,
    Hovered = 1,
    Focused = 2,
    Pressed = 4,
    Disabled = 8,
    Selected = 16,
}

/// <summary>Only specified properties override the base style. Matching rules apply in declaration order.</summary>
public sealed record InteractionStyle(
    Color? Background = null,
    Color? Foreground = null,
    float? Opacity = null,
    float? BorderWidth = null,
    Color? BorderColor = null,
    float? CornerRadius = null,
    BoxShadow? Shadow = null,
    float? TranslationX = null,
    float? TranslationY = null,
    float? ScaleX = null,
    float? ScaleY = null,
    float? Rotation = null
)
{
    internal Node Apply(Node node)
    {
        var style = node.Style with
        {
            Background = Background ?? node.Style.Background,
            Foreground = Foreground ?? node.Style.Foreground,
            CornerRadius = CornerRadius ?? node.Style.CornerRadius,
        };
        var visual = node.Visual with
        {
            Opacity = Opacity ?? node.Visual.Opacity,
            BorderWidth = BorderWidth ?? node.Visual.BorderWidth,
            BorderColor = BorderColor ?? node.Visual.BorderColor,
            Shadow = Shadow ?? node.Visual.Shadow,
            TranslationX = TranslationX ?? node.Visual.TranslationX,
            TranslationY = TranslationY ?? node.Visual.TranslationY,
            ScaleX = ScaleX ?? node.Visual.ScaleX,
            ScaleY = ScaleY ?? node.Visual.ScaleY,
            Rotation = Rotation ?? node.Visual.Rotation,
        };
        BorderSide? OverrideEdge(BorderSide? side) =>
            side is { } edge
                ? edge with
                {
                    Width = BorderWidth ?? edge.Width,
                    Color = BorderColor ?? edge.Color,
                }
                : null;
        var decoration = node.Decoration with
        {
            Corners = CornerRadius.HasValue ? null : node.Decoration.Corners,
            Left = OverrideEdge(node.Decoration.Left),
            Top = OverrideEdge(node.Decoration.Top),
            Right = OverrideEdge(node.Decoration.Right),
            Bottom = OverrideEdge(node.Decoration.Bottom),
        };
        return node with { Style = style, Visual = visual, Decoration = decoration };
    }

    internal void Validate()
    {
        if (CornerRadius is { } radius)
            Styling.Nonnegative(radius);
        Apply(new Text("")).Visual.Validate();
    }
}

internal sealed record InteractionRule(InteractionState State, InteractionStyle Style);

public static class InteractionStyling
{
    public static T Enabled<T>(this T node, bool enabled = true)
        where T : Node, IInteractive => (T)(node with { IsEnabled = enabled });

    public static T OnState<T>(this T node, InteractionState state, InteractionStyle style)
        where T : Node, IStateStyled
    {
        ArgumentNullException.ThrowIfNull(style);
        if (
            state == InteractionState.None
            || (
                state
                & ~(
                    InteractionState.Hovered
                    | InteractionState.Focused
                    | InteractionState.Pressed
                    | InteractionState.Disabled
                    | InteractionState.Selected
                )
            ) != 0
        )
            throw new ArgumentOutOfRangeException(nameof(state));
        var pointerStates =
            InteractionState.Hovered | InteractionState.Focused | InteractionState.Disabled;
        var supported = node switch
        {
            Button => pointerStates | InteractionState.Pressed,
            RadioButton => pointerStates | InteractionState.Pressed | InteractionState.Selected,
            Switch => pointerStates | InteractionState.Selected,
            TextField or Slider => pointerStates,
            _ => InteractionState.Disabled,
        };
        if ((state & ~supported) != 0)
            throw new ArgumentException(
                "This control does not expose the requested native state.",
                nameof(state)
            );
        if (style.Foreground is not null && node is not ITextual)
            throw new ArgumentException(
                "Foreground state styling requires a textual control.",
                nameof(style)
            );
        style.Validate();
        return (T)(node with { InteractionRules = [.. node.InteractionRules, new(state, style)] });
    }

    public static T ClearStateStyles<T>(this T node)
        where T : Node, IStateStyled => (T)(node with { InteractionRules = [] });
}
