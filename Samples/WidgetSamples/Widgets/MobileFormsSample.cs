using DotNative;

namespace WidgetSamples.Widgets;

/// <summary>Exercises the editors needed by login and chat without a backend.</summary>
public sealed class MobileFormsSample : Component
{
    private readonly State<string> _email = new("");
    private readonly State<string> _password = new("");
    private readonly State<string> _message = new("");
    private readonly State<string> _status = new("Try Next, Done and Send on the keyboard.");
    private readonly State<bool> _showPassword = new(false);
    private readonly FocusNode _passwordFocus = new();
    private readonly FocusNode _messageFocus = new();

    public override Element Build(BuildContext context)
    {
        var target = context.TryGet<PresentationTarget>(out var renderer)
            ? renderer
            : PresentationTarget.Local;
        if (target.Platform is not (NativePlatform.IOS or NativePlatform.Android))
            return new Text(
                "Mobile login and chat editors are available in the iOS/Android preview."
            ).FontSize(14);
        return new VStack(
            new Text("Login and chat inputs").FontSize(22),
            new TextField(_email.Value, value => _email.Value = value)
            {
                Placeholder = "Email",
                Input = new()
                {
                    Keyboard = TextKeyboard.Email,
                    Action = TextInputAction.Next,
                    Autofill = TextAutofill.Username,
                    Autocorrect = false,
                },
                OnSubmitted = _ => _passwordFocus.RequestFocus(),
            }
                .Padding(12)
                .Border(1, Color.Blue)
                .CornerRadius(8),
            new TextField(_password.Value, value => _password.Value = value)
            {
                Placeholder = "Password",
                Focus = _passwordFocus,
                Input = new()
                {
                    IsPassword = !_showPassword.Value,
                    Action = TextInputAction.Done,
                    Autofill = TextAutofill.Password,
                    Autocorrect = false,
                },
                OnSubmitted = _ =>
                {
                    _passwordFocus.Unfocus();
                    _status.Value = "Form submitted locally; no login request sent.";
                },
            }
                .Padding(12)
                .Border(1, Color.Blue)
                .CornerRadius(8),
            new Button(
                _showPassword.Value ? "Hide password" : "Show password",
                () => _showPassword.Value = !_showPassword.Value
            ).Padding(8),
            new TextField(_message.Value, value => _message.Value = value)
            {
                Placeholder = "Write a message",
                Focus = _messageFocus,
                Input = new()
                {
                    IsMultiline = true,
                    Action = TextInputAction.Send,
                    Capitalization = TextCapitalization.Sentences,
                },
                OnSubmitted = text =>
                {
                    _status.Value = $"Submitted locally: {text}";
                    _message.Value = "";
                    _messageFocus.Unfocus();
                },
            }
                .Height(100)
                .Padding(12)
                .Border(1, Color.Blue)
                .CornerRadius(8),
            new Text(_status.Value).FontSize(14)
        ).Spacing(12);
    }
}
