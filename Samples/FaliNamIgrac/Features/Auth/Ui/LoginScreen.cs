using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class LoginScreen : ScreenComponent
{
    private readonly LoginFormNotifier form;
    private readonly SessionNotifier session;
    private readonly AuthFlowText? fixedMessages;
    private AuthFlowText messages => fixedMessages ?? AuthFlowText.From(ui.Text);

    public LoginScreen(
        UiContext ui,
        LoginFormNotifier form,
        SessionNotifier session,
        AuthFlowText? text = null
    )
        : base(ui)
    {
        this.form = form;
        this.session = session;
        fixedMessages = text;
    }

    private readonly FocusNode loginEmailFocus = new();
    private readonly FocusNode loginPasswordFocus = new();

    private Element SignIn()
    {
        if (target.Platform is not (NativePlatform.IOS or NativePlatform.Android))
            return new Text(Loc.OpenMobileApp).FontSize(18);
        if (presentation.Dimensions.Value is not { } dimensions)
            return new Text(form.Message).ForegroundColor(Ink);

        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var sp = w;
        var r = Math.Min(w, h);
        var width = dimensions.Width - 32 * w;
        Element Gap(float value) => new VStack().Height(value * h);
        Text Label(string text, float size, string weight = "Regular")
        {
            var label = new Text(text)
                .TextStyle(TextStyleFor(weight))
                .FontSize(size * sp)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center)
                .FontWeight(
                    weight switch
                    {
                        "Bold" => FontWeight.Bold,
                        "Medium" => FontWeight.Medium,
                        _ => FontWeight.Normal,
                    }
                );
            return label;
        }
        var canSignIn =
            form.Password.Length > 0
            && System.Text.RegularExpressions.Regex.IsMatch(form.Email, "^[^@]+@[^@]+\\.[^@]+");
        var fill = theme.Surface;
        var emailField = new TextField(
            form.Email,
            value =>
            {
                form.Email = value;
                form.ValidateLoginEmail();
                if (!form.LoginEmailFocused && !form.LoginPasswordFocused)
                    form.ValidateLoginEmailFill();
            }
        )
        {
            Placeholder = Loc.Email,
            Focus = loginEmailFocus,
            OnFocusChanged = focused =>
            {
                form.LoginEmailFocused = focused;
                if (!focused)
                    form.ValidateLoginEmail();
            },
            OnSubmitted = _ => form.ValidateLoginEmailFill(),
            Input = new()
            {
                PlaceholderColor = AuthHint,
                Keyboard = TextKeyboard.Email,
                Autofill = TextAutofill.Username,
                Autocorrect = false,
            },
        }
            .TextStyle(TextStyleFor())
            .FontSize(16 * sp)
            .ForegroundColor(Ink)
            .Height(56 * h)
            .Padding(16 * w)
            .BackgroundColor(form.LoginEmailFillError ? theme.ErrorSurface : fill)
            .Border(1, form.LoginEmailError ? theme.Error : fill)
            .CornerRadius(16 * r)
            .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
            .WithKey("email");
        var passwordField = new TextField(
            form.Password,
            value =>
            {
                form.Password = value;
                form.ValidateLoginEmail();
            }
        )
        {
            Placeholder = Loc.Password,
            Focus = loginPasswordFocus,
            OnFocusChanged = focused =>
            {
                form.LoginPasswordFocused = focused;
                if (!focused)
                    form.ValidateLoginEmail();
            },
            Input = new()
            {
                PlaceholderColor = AuthHint,
                IsPassword = !form.ShowPassword,
                Autofill = TextAutofill.Password,
                Autocorrect = false,
            },
        }
            .TextStyle(TextStyleFor())
            .FontSize(16 * sp)
            .ForegroundColor(Ink)
            .Height(56 * h)
            .Padding(16 * w, 0, 56 * w, 0)
            .BackgroundColor(fill)
            .Border(1, new Color(246, 246, 246))
            .CornerRadius(16 * r)
            .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
            .WithKey("password");
        var passwordBox = new VStack(
            passwordField,
            new Button(
                form.ShowPassword ? "\ue6bd" : "\ue6be",
                () => form.ShowPassword = !form.ShowPassword
            )
                .TextStyle(TextStyleFor("Icons"))
                .FontSize(24 * sp)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center)
                .Width(48 * w)
                .Height(56 * h)
                .Position(PositionMode.Absolute)
                .Right(0)
                .Top(0)
        ).Height(56 * h);
        var submit = new VStack(
            new HStack(
                Label(Loc.SignIn, 14, "Medium").ForegroundColor(theme.OnPrimary),
                session.Busy
                    ? BusyIndicator()
                    : new Image(FniAssets.Image("icons/arrow_right.png"))
                        .Width(24 * w)
                        .Height(24 * h)
                        .TintColor(Color.White)
            )
                .Spacing(20 * w)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Height(56 * h),
            new Button("", () => _ = SignInAsync())
                .Enabled(canSignIn && !session.Busy)
                .Width(width)
                .Height(56 * h)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Height(56 * h)
            .BackgroundColor(canSignIn ? Accent : (theme.Primary with { A = 51 }))
            .CornerRadius(16 * r);

        return new VStack(
            AuthAppBar(() => NavigateAuth(ApplicationScreen.Landing)),
            new ScrollView(
                new VStack(
                    LoginMotion.Header(width, w, h, loginAnimation.Value),
                    Gap(24),
                    Label(Loc.SignIn, 32, "Bold"),
                    Gap(8),
                    Label(Loc.LoginBody, 14).ForegroundColor(new Color(128, 128, 128)),
                    Gap(24),
                    emailField,
                    form.LoginEmailError
                        ? Label(Loc.EmailError, 12)
                            .ForegroundColor(AuthWarning)
                            .TextAlignment(TextAlignment.Left)
                            .Padding(12 * w, 8)
                        : new VStack(),
                    Gap(32),
                    passwordBox,
                    Gap(16),
                    new Button(Loc.ForgotPassword, OpenRecovery)
                        .TextStyle(TextStyleFor())
                        .FontWeight(FontWeight.ExtraBold)
                        .FontSize(16 * sp)
                        .ForegroundColor(Accent)
                        .Height(48),
                    Gap(16),
                    submit,
                    Gap(16),
                    Label(Loc.OrSignUpWith, 16, "Medium"),
                    Gap(16),
                    SocialAuthenticationButtons(
                        w,
                        h,
                        r,
                        () => _ = SocialSignInAsync("Google"),
                        () => _ = SocialSignInAsync("SignInWithApple")
                    )
                )
                    .Width(dimensions.Width)
                    .Padding(16 * w, 0)
            )
                .Grow()
                .Shrink()
        )
            .HeightPercent(100)
            .BackgroundColor(theme.Background);
    }

    public override Element Build() => SafeArea(SignIn());

    public override void Leave()
    {
        loginEmailFocus.Unfocus();
        loginPasswordFocus.Unfocus();
    }

    private async Task SignInAsync()
    {
        try
        {
            await session.SignInAsync(form.Email, form.Password, lifetime.Token);
            if (session.State is not SessionState.SignedOut)
                form.Password = "";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            var message =
                error is UnsupportedAuthenticationChallengeException
                    ? messages.ChallengeUnsupported
                    : FniErrors.UserMessage(presentation.Text, error);
            form.Message = message;
            ShowRecoveryToast(message);
        }
    }
}
