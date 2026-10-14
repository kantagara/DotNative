namespace DotNative;

/// <summary>A native, controlled single-choice control. Selection cannot toggle off.</summary>
public sealed record RadioButton : Node, IStateStyled, ILayoutable, ITextFlow, IPaddable
{
    public string Label { get; }
    public bool IsSelected { get; }
    public Action OnSelected { get; }

    public RadioButton(string label, bool isSelected, Action onSelected)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        IsSelected = isSelected;
        OnSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
    }
}

public sealed record RadioOption<T>(T Value, string Label);

/// <summary>Mutually exclusive radio choices; application state owns the value.</summary>
public sealed class RadioGroup<T> : Component
{
    private readonly T _value;
    private readonly Action<T> _changed;
    private readonly RadioOption<T>[] _options;
    public Axis Orientation { get; init; } = Axis.Vertical;

    public RadioGroup(T value, Action<T> onChanged, params RadioOption<T>[] options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _changed = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _options = (RadioOption<T>[])options.Clone();
        var values = new HashSet<T>();
        foreach (var option in _options)
        {
            ArgumentNullException.ThrowIfNull(option);
            ArgumentNullException.ThrowIfNull(option.Label);
            if (!values.Add(option.Value))
                throw new ArgumentException("Radio values must be unique.", nameof(options));
        }
        if (!values.Contains(value))
            throw new ArgumentException("The selected value must be an option.", nameof(value));
        _value = value;
    }

    public override Element Build(BuildContext context)
    {
        if (!Enum.IsDefined(Orientation))
            throw new InvalidOperationException("Invalid radio orientation.");
        Element[] choices = _options
            .Select(option =>
                (Element)
                    new RadioButton(
                        option.Label,
                        EqualityComparer<T>.Default.Equals(_value, option.Value),
                        () => _changed(option.Value)
                    ).Padding(8)
            )
            .ToArray();
        return Orientation == Axis.Horizontal
            ? new HStack(choices).Spacing(8)
            : new VStack(choices).Spacing(4);
    }
}
