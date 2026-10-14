using System.Text.RegularExpressions;
using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class LoginFormNotifier(IUiDispatcher dispatcher)
    : Notifier<LoginFormState>(dispatcher, new())
{
    public string Message
    {
        get => State.Message;
        set => SetState(State with { Message = value });
    }

    public string Email
    {
        get => State.Email;
        set => SetState(State with { Email = value });
    }

    public string Password
    {
        get => State.Password;
        set => SetState(State with { Password = value });
    }

    public bool ShowPassword
    {
        get => State.ShowPassword;
        set => SetState(State with { ShowPassword = value });
    }

    public bool LoginEmailError
    {
        get => State.LoginEmailError;
        set => SetState(State with { LoginEmailError = value });
    }

    public bool LoginEmailFillError
    {
        get => State.LoginEmailFillError;
        set => SetState(State with { LoginEmailFillError = value });
    }

    public bool LoginEmailFocused
    {
        get => State.LoginEmailFocused;
        set => SetState(State with { LoginEmailFocused = value });
    }

    public bool LoginPasswordFocused
    {
        get => State.LoginPasswordFocused;
        set => SetState(State with { LoginPasswordFocused = value });
    }

    public void ValidateLoginEmail() =>
        LoginEmailError = Email.Length > 0 && !Regex.IsMatch(Email, "^[^@]+@[^@]+\\.[^@]+");

    public void ValidateLoginEmailFill()
    {
        ValidateLoginEmail();
        LoginEmailFillError = LoginEmailError;
    }

    public void Reset() => SetState(new());
}
