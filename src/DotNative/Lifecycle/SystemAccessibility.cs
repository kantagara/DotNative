namespace DotNative;

public readonly record struct AccessibilityPreferences(
    bool ReduceMotion = false,
    float FontScale = 1,
    bool HighContrast = false
)
{
    public AccessibilityPreferences()
        : this(false, 1, false) { }

    public static AccessibilityPreferences Default => new(false, 1, false);

    internal void Validate()
    {
        if (!float.IsFinite(FontScale) || FontScale < 0.5f || FontScale > 5)
            throw new ArgumentOutOfRangeException(nameof(FontScale));
    }
}

/// <summary>Reactive OS preferences, independent of application styling choices.</summary>
public interface ISystemAccessibility
{
    AccessibilityPreferences Preferences { get; }
}

internal sealed class SystemAccessibility : ReactiveSource, ISystemAccessibility
{
    private AccessibilityPreferences _value = AccessibilityPreferences.Default;
    public AccessibilityPreferences Preferences
    {
        get
        {
            TrackRead();
            return _value;
        }
    }

    internal void Update(AccessibilityPreferences value)
    {
        VerifyCanWrite();
        value.Validate();
        if (_value == value)
            return;
        _value = value;
        PublishChange();
    }
}

public static class AccessibilityStyling
{
    /// <summary>Opt out only for text whose fixed size is essential, such as a diagram label.</summary>
    public static T FontScaling<T>(this T node, bool enabled = true)
        where T : Node, ITextual => (T)(node with { AllowsFontScaling = enabled });
}
