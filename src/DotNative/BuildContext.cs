namespace DotNative;

public readonly record struct ViewportSize(float Width, float Height);

// Safe area and keyboard overlap of the viewport, in the same units as ViewportSize.
// A runner that already places the canvas inside the status bar reports Top as 0.
public readonly record struct WindowInsets(float Top, float Bottom, float Keyboard);

// Context is a tree-local lookup scope, never a global service locator. It is
// valid during synchronous Build only; capture values, not context, in callbacks.
public sealed class BuildContext
{
    internal sealed class Lifetime
    {
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal bool Active = true;
    }

    private readonly Lifetime _lifetime;
    private readonly BuildContext? _parent;
    private readonly Type? _type;
    private readonly object? _value;

    internal BuildContext(
        Lifetime lifetime,
        BuildContext? parent = null,
        Type? type = null,
        object? value = null
    ) => (_lifetime, _parent, _type, _value) = (lifetime, parent, type, value);

    internal BuildContext With<T>(T value)
        where T : notnull => new(_lifetime, this, typeof(T), value);

    private object? Lookup(Type type)
    {
        for (BuildContext? scope = this; scope is not null; scope = scope._parent)
            if (scope._type == type)
                return scope._value;
        return null;
    }

    internal bool Matches(Dictionary<Type, object?> reads) =>
        reads.All(read => Equals(Lookup(read.Key), read.Value));

    internal bool SameValues(BuildContext other)
    {
        BuildContext? left = this;
        BuildContext? right = other;
        while (left is not null && right is not null)
        {
            if (left._type != right._type || !Equals(left._value, right._value))
                return false;
            left = left._parent;
            right = right._parent;
        }
        return left is null && right is null;
    }

    public T Get<T>()
        where T : notnull =>
        TryGet<T>(out var value)
            ? value
            : throw new InvalidOperationException($"No ancestor provides {typeof(T).Name}.");

    public bool TryGet<T>(out T value)
        where T : notnull
    {
        if (!_lifetime.Active || _lifetime.Thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(
                "BuildContext lookups require the active UI-thread Build. Capture the returned value for later use."
            );
        var found = Lookup(typeof(T));
        if (BuildScope.ContextReads is { } reads)
            reads[typeof(T)] = found;
        if (found is T result)
        {
            value = result;
            return true;
        }
        value = default!;
        return false;
    }
}

internal interface IContextScope : Element
{
    Element Child { get; }
    BuildContext Apply(BuildContext parent);
}

public class ContextScope<T> : IContextScope
    where T : notnull
{
    public T Value { get; }
    public Element Child { get; }

    public ContextScope(T value, Element child)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Child = child ?? throw new ArgumentNullException(nameof(child));
    }

    BuildContext IContextScope.Apply(BuildContext parent) => parent.With(Value);
}

public sealed class Builder(Func<BuildContext, Element> build) : Component
{
    private readonly Func<BuildContext, Element> _build =
        build ?? throw new ArgumentNullException(nameof(build));

    public override Element Build(BuildContext context) => _build(context);
}

public sealed record ThemeData : IThemeData
{
    public ThemeMode Mode { get; init; } = ThemeMode.Light;
    public Color Primary { get; init; } = Color.Blue;
    public Color OnPrimary { get; init; } = Color.White;
    public Color Background { get; init; } = Color.White;
    public Color Foreground { get; init; } = new(0, 0, 0);
    public Color Surface { get; init; } = new(246, 246, 246);
    public Color SecondaryBackground { get; init; } = new(245, 246, 247);
    public Color MutedForeground { get; init; } = new(128, 128, 128);
    public Color Error { get; init; } = Color.Red;
    public Color ErrorSurface { get; init; } = new(255, 226, 226);
    public Color Success { get; init; } = new(76, 175, 80);
    public TextTheme Text { get; init; } = new();
    public float FontSize { get; init; } = 17;
    public float CornerRadius { get; init; } = 8;
    public static ThemeData Light { get; } = new();
    public static ThemeData Dark { get; } =
        new()
        {
            Background = new(24, 24, 27),
            Foreground = Color.White,
            Mode = ThemeMode.Dark,
        };
}

public sealed class Theme(IThemeData data, Element child) : ContextScope<IThemeData>(data, child)
{
    public static IThemeData Of(BuildContext context) =>
        context.TryGet<IThemeData>(out var theme) ? theme : ThemeData.Light;
}
