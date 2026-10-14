using System.Text.RegularExpressions;

namespace FaliNamIgrac;

public enum PasswordStrength
{
    Empty,
    Weak,
    Medium,
    Strong,
}

public sealed record RegistrationForm(
    string? Email = null,
    string? Password = null,
    string? ConfirmationPassword = null,
    bool? PrivacyPolicy = null,
    bool? Terms = null
)
{
    public const int PasswordMinLength = 7;

    // The original form currently reports email fields as valid, but checks the
    // complete address when enabling Send Code. Keep those two rules distinct.
    public bool ValidateEmail() => true;

    public bool ValidatePasswords() =>
        string.IsNullOrEmpty(Password) || Password == ConfirmationPassword;

    public bool Validate() =>
        Email is not null
        && Regex.IsMatch(Email, "^[^@]+@[^@]+\\.[^@]+$")
        && Password is { Length: >= PasswordMinLength }
        && !string.IsNullOrEmpty(ConfirmationPassword)
        && Password == ConfirmationPassword
        && PrivacyPolicy == true
        && Terms == true;

    public PasswordStrength Strength
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return PasswordStrength.Empty;
            if (Password.Length < PasswordMinLength)
                return PasswordStrength.Weak;
            var criteria = new[]
            {
                Regex.IsMatch(Password, "[a-z]"),
                Regex.IsMatch(Password, "[A-Z]"),
                Regex.IsMatch(Password, "[0-9]"),
                Regex.IsMatch(Password, "[^A-Za-z0-9]"),
            }.Count(value => value);
            return criteria >= 3 && Password.Length >= 10 ? PasswordStrength.Strong
                : criteria >= 2 ? PasswordStrength.Medium
                : PasswordStrength.Weak;
        }
    }
}
