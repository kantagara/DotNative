namespace FaliNamIgrac;

public sealed record RegistrationFormState
{
    public RegistrationForm RegistrationForm { get; init; } = new();
    public bool RegistrationPasswordVisible { get; init; } = false;
    public bool RegistrationConfirmationVisible { get; init; } = false;
    public bool RegistrationConfirmationTouched { get; init; } = false;
    public string RegistrationCode { get; init; } = "";
}
