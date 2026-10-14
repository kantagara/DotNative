namespace DotNative;

public sealed partial class RenderHost
{
    private AccessibilityPreferences _accessibility = AccessibilityPreferences.Default;
    private bool _accessibilityDirty;
    private bool ShouldReduceMotion => _reducedMotion || _accessibility.ReduceMotion;

    /// <summary>For native/custom hosts. System reduced motion cannot be disabled by an application override.</summary>
    public void SetSystemAccessibility(AccessibilityPreferences preferences)
    {
        VerifyUsable();
        preferences.Validate();
        if (_accessibility == preferences)
            return;
        var previous = _accessibility;
        _accessibility = preferences;
        if (preferences.ReduceMotion && !previous.ReduceMotion)
            AdvanceTransitions();
        if (preferences.FontScale != previous.FontScale)
        {
            _accessibilityDirty = true;
            ScheduleRender();
        }
    }

    private Node ApplyAccessibility(Node node)
    {
        if (node is not ITextual || !node.AllowsFontScaling || _accessibility.FontScale == 1)
            return node;
        float Scale(float value) =>
            (float)Math.Min(float.MaxValue, (double)value * _accessibility.FontScale);
        node = node with
        {
            Style = node.Style with { FontSize = Scale(node.Style.FontSize) },
            Typography = node.Typography with
            {
                LetterSpacing = (float)
                    Math.Clamp(
                        (double)node.Typography.LetterSpacing * _accessibility.FontScale,
                        -float.MaxValue,
                        float.MaxValue
                    ),
                LineHeight = node.Typography.LineHeight is { } height ? Scale(height) : null,
            },
        };
        if (node is Text text && text.Runs.Length != 0)
            node = text with
            {
                Runs = text
                    .Runs.Select(run =>
                        run.FontSize is { } size ? run with { FontSize = Scale(size) } : run
                    )
                    .ToArray(),
            };
        return node;
    }
}
