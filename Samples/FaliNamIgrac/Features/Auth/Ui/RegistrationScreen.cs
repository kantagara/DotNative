using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class RegistrationScreen : ScreenComponent
{
    private readonly RegistrationFormNotifier form;

    public RegistrationScreen(UiContext ui, RegistrationFormNotifier form)
        : base(ui)
    {
        this.form = form;
    }

    private readonly FocusNode registrationEmailFocus = new();
    private readonly FocusNode registrationPasswordFocus = new();
    private readonly FocusNode registrationConfirmationFocus = new();

    private Element RegistrationPage(
        Action back,
        Action googleSignIn,
        Action appleSignIn,
        Action showConsentError,
        Action showCodePage,
        Element busyIndicator
    )
    {
        if (presentation.Dimensions.Value is not { } dimensions)
            return SafeArea(Splash());
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var r = Math.Min(w, h);
        var width = dimensions.Width - 32 * w;
        var value = form.RegistrationForm;
        Element Gap(float value) => new VStack().Height(value * h);
        Text Label(string text, float size, string face = "Regular") =>
            new Text(text)
                .TextStyle(TextStyleFor(face))
                .FontWeight(
                    face == "Bold" ? FontWeight.Bold
                    : face == "Medium" ? FontWeight.Medium
                    : FontWeight.Normal
                )
                .FontSize(size * w)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center);

        Element Password(bool confirmation)
        {
            var visible = confirmation
                ? form.RegistrationConfirmationVisible
                : form.RegistrationPasswordVisible;
            var valid = !confirmation || value.ValidatePasswords();
            var warning = AuthWarning;
            var color = valid ? new Color(128, 128, 128) : warning;
            var field = new TextField(
                (confirmation ? value.ConfirmationPassword : value.Password) ?? "",
                value => form.SetPassword(value, confirmation)
            )
            {
                Focus = confirmation ? registrationConfirmationFocus : registrationPasswordFocus,
                Placeholder = confirmation ? Loc.ConfirmPassword : Loc.Password,
                Input = new()
                {
                    PlaceholderColor = AuthHint,
                    IsPassword = !visible,
                    Autocorrect = false,
                },
            }
                .TextStyle(TextStyleFor())
                .FontSize(16 * w)
                .ForegroundColor(color)
                .Height(56 * h)
                .Padding(16 * w, 0, 56 * w, 0)
                .BackgroundColor(
                    valid
                        ? (theme.Mode == ThemeMode.Dark ? new Color(22, 22, 22) : theme.Surface)
                        : theme.ErrorSurface
                )
                .Border(1, valid ? new Color(246, 246, 246) : warning)
                .CornerRadius(16 * r)
                .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
                .WithKey(confirmation ? "registration-confirmation" : "registration-password");
            var error = confirmation
                ? !valid && form.RegistrationConfirmationTouched
                    ? Loc.PasswordMismatchError
                    : null
                : value.Password is { Length: > 0 and < RegistrationForm.PasswordMinLength }
                    ? Loc.PasswordTooShort(RegistrationForm.PasswordMinLength)
                    : null;
            var strengthColor = value.Strength switch
            {
                PasswordStrength.Strong => Accent,
                PasswordStrength.Medium => new Color(255, 152, 0),
                _ => warning,
            };
            var fraction = value.Strength switch
            {
                PasswordStrength.Strong => 1f,
                PasswordStrength.Medium => .66f,
                _ => .33f,
            };
            return new VStack([
                new VStack(
                    field,
                    new Button(
                        visible ? "\ue6bd" : "\ue6be",
                        () => form.TogglePasswordVisibility(confirmation)
                    )
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(24 * r)
                        .ForegroundColor(valid ? Ink : AuthWarning)
                        .TextAlignment(TextAlignment.Center)
                        .Width(48 * w)
                        .Height(56 * h)
                        .Position(PositionMode.Absolute)
                        .Right(0)
                        .Top(0)
                ).Height(56 * h),
                .. error is not null
                    ? new Element[]
                    {
                        Label(error, 12)
                            .ForegroundColor(warning)
                            .TextAlignment(TextAlignment.Left)
                            .Padding(12 * w, 8),
                    }
                    : [],
                .. !confirmation && value.Strength != PasswordStrength.Empty
                    ? new Element[] { Gap(8), new HStack(
                            new VStack(
                                new VStack()
                                    .WidthPercent(fraction * 100)
                                    .Height(6 * h)
                                    .BackgroundColor(strengthColor)
                            )
                                .Grow()
                                .Height(6 * h)
                                .BackgroundColor(new Color(0, 0, 0, 10))
                                .CornerRadius(4 * r)
                                .ClipToBounds(true),
                            Label(
                                    value.Strength switch
                                    {
                                        PasswordStrength.Strong => Loc.PasswordStrengthStrong,
                                        PasswordStrength.Medium => Loc.PasswordStrengthMedium,
                                        _ => Loc.PasswordStrengthWeak,
                                    },
                                    16
                                )
                                .ForegroundColor(strengthColor)
                        ).Spacing(8 * w).CrossAlignment(CrossAxisAlignment.Center) }
                    : [],
            ]);
        }

        Element Consent(bool privacy)
        {
            var selected = (privacy ? value.PrivacyPolicy : value.Terms) == true;
            return new HStack(
                new VStack(
                    new VStack(
                        selected
                            ? new Text("\ue156")
                                .TextStyle(TextStyleFor("Icons"))
                                .FontSize(16)
                                .ForegroundColor(Color.White)
                            : new VStack()
                    )
                        .Width(18)
                        .Height(18)
                        .Border(2, selected ? Accent : AuthHint)
                        .BackgroundColor(selected ? Accent : theme.Surface)
                        .CornerRadius(4 * r)
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center),
                    new Button(
                        "",
                        () =>
                        {
                            form.ToggleConsent(privacy);
                        }
                    ).Width(48).Height(48).Position(PositionMode.Absolute).Left(0).Top(0)
                )
                    .Width(48)
                    .Height(48)
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center),
                Text.Rich(
                        new TextRun(Loc.Accepting + " "),
                        new TextRun(
                            privacy ? Loc.PrivacyPolicy : Loc.Terms,
                            Foreground: Accent,
                            Weight: FontWeight.ExtraBold
                        )
                    )
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * w)
                    .ForegroundColor(new Color(128, 128, 128))
            ).CrossAlignment(CrossAxisAlignment.Center);
        }

        void Social(Action signIn)
        {
            if (value.PrivacyPolicy != true || value.Terms != true)
                showConsentError();
            else
                signIn();
        }

        var email = new TextField(value.Email ?? "", form.SetEmail)
        {
            Focus = registrationEmailFocus,
            Placeholder = Loc.Email,
            Input = new()
            {
                PlaceholderColor = AuthHint,
                Keyboard = TextKeyboard.Email,
                Autocorrect = false,
            },
        }
            .TextStyle(TextStyleFor())
            .FontSize(16 * w)
            .ForegroundColor(Ink)
            .Height(56 * h)
            .Padding(16 * w)
            .BackgroundColor(theme.Surface)
            .Border(1, new Color(246, 246, 246))
            .CornerRadius(16 * r)
            .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
            .WithKey("registration-email");
        return SafeArea(
            new VStack(
                AuthAppBar(back),
                new ScrollView(
                    new VStack(
                        LoginMotion.Header(width, w, h, loginAnimation.Value),
                        Gap(24),
                        Label(Loc.RegisterTitle, 32, "Bold"),
                        Gap(8),
                        Label(Loc.RegisterBody, 14).ForegroundColor(new Color(128, 128, 128)),
                        Gap(24),
                        SocialAuthenticationButtons(
                            w,
                            h,
                            r,
                            () => Social(googleSignIn),
                            () => Social(appleSignIn)
                        ),
                        Gap(16),
                        Label(Loc.OrSignUpWith, 16, "Medium"),
                        Gap(16),
                        email,
                        Gap(32),
                        Password(false),
                        Gap(32),
                        Password(true),
                        Gap(34),
                        Consent(true),
                        Consent(false),
                        Gap(32),
                        RegistrationSubmit(
                            Loc.SendCode,
                            () => _ = BeginRegistrationAsync(showCodePage),
                            value.Validate(),
                            form.Busy,
                            busyIndicator,
                            width,
                            w,
                            h,
                            r
                        ),
                        Gap(16)
                    )
                        .Width(dimensions.Width)
                        .Padding(16 * w, 0)
                )
                    .Grow()
                    .Shrink()
            )
                .HeightPercent(100)
                .BackgroundColor(theme.Background)
        );
    }

    public override Element Build() =>
        RegistrationPage(
            () => NavigateAuth(ApplicationScreen.Landing),
            () => _ = SocialSignInAsync("Google"),
            () => _ = SocialSignInAsync("SignInWithApple"),
            () => form.ConsentPopup = true,
            () => NavigateAuth(ApplicationScreen.RegistrationCode),
            BusyIndicator()
        );

    public override void Leave()
    {
        registrationEmailFocus.Unfocus();
        registrationPasswordFocus.Unfocus();
        registrationConfirmationFocus.Unfocus();
    }

    private async Task BeginRegistrationAsync(Action showCodePage)
    {
        try
        {
            if (await form.BeginAsync(lifetime.Token))
            {
                Leave();
                showCodePage();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            ShowRecoveryToast(
                error is RegistrationUnavailableException
                    ? Loc.RegistrationNotPossible
                    : error.Message
            );
        }
    }
}
