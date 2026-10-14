# Mobile text input

`TextField.Input` configures native iOS and Android editors. The default remains a
single-line text field. `OnSubmitted` receives the current native text when the
user invokes a keyboard action, and runs through the same UI dispatcher as
`OnChanged`. It does not log the text or automatically submit a network request.

```csharp
private readonly FocusNode passwordFocus = new();

new TextField(email.Value, value => email.Value = value)
{
    Placeholder = "Email",
    Input = new()
    {
        Keyboard = TextKeyboard.Email,
        Action = TextInputAction.Next,
        Autofill = TextAutofill.Username,
        Autocorrect = false,
    },
    OnSubmitted = _ => passwordFocus.RequestFocus(),
};

new TextField(password.Value, value => password.Value = value)
{
    Focus = passwordFocus,
    Input = new()
    {
        IsPassword = true,
        Action = TextInputAction.Done,
        Autofill = TextAutofill.Password,
    },
    OnSubmitted = _ => passwordFocus.Unfocus(),
};
```

`TextKeyboard` supports text, email, number, decimal, telephone and URL keyboards.
These are OS hints, not validation rules. Password mode uses the text password
editor and disables autocorrection, spell checking and automatic capitalization.
`TextAutofill` supports username, password, new password, email and one-time code;
the OS decides whether to offer a suggestion. Number-pad keyboards on iOS may
not display a return key; provide an explicit submit/dismiss button when needed.

The available actions are Default, Done, Next, Search, Send and Go. The application
owns focus traversal and completion. There is no implicit form submission.

For a chat composer or description:

```csharp
new TextField(message.Value, value => message.Value = value)
{
    Placeholder = "Message",
    Input = new()
    {
        IsMultiline = true,
        Action = TextInputAction.Send,
        Capitalization = TextCapitalization.Sentences,
    },
    OnSubmitted = SendMessage,
}.Height(100);
```

Multiline requires the text keyboard and cannot be combined with password mode.
With `Action = Default`, Return inserts a newline. An explicit action such as
Send invokes `OnSubmitted`. iOS uses UITextView; single-line fields use
UITextField. Android uses EditText. Changing `IsMultiline` replaces the native
editor and loses its native focus/selection; text remains controlled by `Value`.
Changing other options, including revealing a password, retains node identity.
The renderer leaves marked text alone and avoids replacing text when an edit
comes back unchanged through managed state.

Non-default options currently fail explicitly on desktop adapters before native
presentation. This prevents an unsupported password field being shown as plain
text. The WidgetSamples mobile form demo appears only for iOS/Android targets.

Keyboard avoidance, selection APIs, automatic focus traversal, form validation,
secure token storage and authentication services are separate work. This change
does not make the full application production-ready.

## Verification

- `dotnet run --project tests/DotNative.Tests -c Release`: options, validation,
  resetting, fresh submit callbacks, disabled/deleted nodes and editor replacement.
- `cargo test --manifest-path native/core/Cargo.toml`: malformed payload rejection,
  transactional rollback, incompatible nodes and multiline replacement contract.
- `cargo build --manifest-path native/core/Cargo.toml --target aarch64-apple-ios-sim --example ios_input_smoke`
  followed by `xcrun simctl spawn booted "$PWD/native/core/target/aarch64-apple-ios-sim/debug/examples/ios_input_smoke"`:
  real UIKit traits, identity, multiline behavior and native submit callbacks.
  This executable invokes target/delegate callbacks; it is not an end-to-end
  software-keyboard or autofill test.
- WidgetSamples Android `TextInputTest`: actual EditText password transformation,
  selection, option reset, autofill hints and multiline/IME configuration.

Test real typing, IME composition, autofill and keyboard visibility on physical
phones before relying on them in a release application.
