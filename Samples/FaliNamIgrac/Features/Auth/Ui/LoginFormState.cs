namespace FaliNamIgrac;

public sealed record LoginFormState
{
    public string Message { get; init; } = "";
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";
    public bool ShowPassword { get; init; } = false;
    public bool LoginEmailError { get; init; } = false;
    public bool LoginEmailFillError { get; init; } = false;
    public bool LoginEmailFocused { get; init; } = false;
    public bool LoginPasswordFocused { get; init; } = false;
}
