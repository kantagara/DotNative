namespace DotNative;

public enum TextKeyboard : byte
{
    Text,
    Email,
    Number,
    Decimal,
    Telephone,
    Url,
}

public enum TextInputAction : byte
{
    Default,
    Done,
    Next,
    Search,
    Send,
    Go,
}

public enum TextCapitalization : byte
{
    None,
    Sentences,
    Words,
    Characters,
}

public enum TextAutofill : byte
{
    None,
    Username,
    Password,
    NewPassword,
    Email,
    OneTimeCode,
}

/// <summary>Native mobile editor options. Keyboard types are hints, not input validation.</summary>
public sealed record TextInputOptions
{
    public static TextInputOptions Default { get; } = new();
    public TextKeyboard Keyboard { get; init; }
    public TextInputAction Action { get; init; }
    public TextCapitalization Capitalization { get; init; }
    public TextAutofill Autofill { get; init; }

    /// <summary>Explicit hint color on native mobile editors; null uses the platform default.</summary>
    public Color? PlaceholderColor { get; init; }
    public bool IsPassword { get; init; }
    public bool IsMultiline { get; init; }
    public bool Autocorrect { get; init; } = true;

    internal void Validate()
    {
        if (
            !Enum.IsDefined(Keyboard)
            || !Enum.IsDefined(Action)
            || !Enum.IsDefined(Capitalization)
            || !Enum.IsDefined(Autofill)
        )
            throw new ArgumentOutOfRangeException(nameof(TextInputOptions));
        if (IsMultiline && Keyboard != TextKeyboard.Text)
            throw new ArgumentException("Multiline fields require the text keyboard.");
        if (IsPassword && IsMultiline)
            throw new ArgumentException("Password fields must be single-line.");
    }
}
