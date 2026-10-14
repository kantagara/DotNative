using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class RecoveryHost : PinScreen
{
    private readonly IRecoveryController recovery;
    private readonly ApplicationScreen page;

    public RecoveryHost(UiContext ui, IRecoveryController recovery, ApplicationScreen page)
        : base(ui)
    {
        this.recovery = recovery;
        this.page = page;
    }

    private readonly FocusNode recoveryEmailFocus = new();
    private readonly FocusNode recoveryPasswordFocus = new();
    private readonly FocusNode recoveryConfirmationFocus = new();

    private void OnPasswordResetCompleted(bool success)
    {
        if (success)
            NavigateAuth(ApplicationScreen.Landing);
        ShowRecoveryToast(success ? Loc.PasswordSaveSuccess : Loc.PasswordSaveError);
    }

    private async Task ConfirmPasswordResetAsync()
    {
        if (recovery.Busy)
            return;
        try
        {
            OnPasswordResetCompleted(await recovery.ConfirmResetAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            if (!disposed)
                ShowRecoveryToast(Loc.RequestFailed);
        }
    }

    private Element Recovery(ApplicationScreen page)
    {
        if (presentation.Dimensions.Value is not { } dimensions)
            return SafeArea(Splash());
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var r = Math.Min(w, h);
        var width = dimensions.Width - 32 * w;
        Text Label(string text, float size, string face = "Regular")
        {
            var result = new Text(text)
                .TextStyle(TextStyleFor(face))
                .FontWeight(
                    face == "Bold" ? FontWeight.Bold
                    : face == "Medium" ? FontWeight.SemiBold
                    : FontWeight.Normal
                )
                .FontSize(size * w)
                .TextAlignment(TextAlignment.Center)
                .ForegroundColor(face == "Regular" ? new Color(128, 128, 128) : Ink);
            return result;
        }
        Element Gap(float size) => new VStack().Height(size * h);
        Element Image(string path, float ratio)
        {
            var imageWidth = path.EndsWith("forgot_password_header.png")
                ? Math.Min(343, width)
                : width;
            return new VStack(
                new Image(FniAssets.Image(path))
                    .Fit(ImageFit.Contain)
                    .Width(imageWidth)
                    .Height(imageWidth * ratio)
            ).CrossAlignment(CrossAxisAlignment.Center);
        }
        Element Submit(Action click, bool enabled, float buttonWidth) =>
            new VStack(
                new HStack(
                    Label(Loc.ContinueLabel, 14, "Medium").ForegroundColor(theme.OnPrimary),
                    recovery.Busy
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
                new Button("", click)
                    .Enabled(enabled && !recovery.Busy)
                    .Width(buttonWidth)
                    .Height(56 * h)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(0)
            )
                .Height(56 * h)
                .Width(buttonWidth)
                .BackgroundColor(
                    enabled || recovery.Busy ? Accent : (theme.Primary with { A = 51 })
                )
                .CornerRadius(16 * r);

        Element Password(
            string value,
            Action<string> set,
            bool visible,
            Action<bool> setVisible,
            string key,
            bool confirmation
        )
        {
            var valid = !confirmation || recovery.IsConfirmationValid;
            var color = valid ? new Color(128, 128, 128) : AuthWarning;
            var field = new TextField(
                value,
                text =>
                {
                    set(text);
                    if (confirmation)
                        recovery.ConfirmationTouched = true;
                }
            )
            {
                Focus = confirmation ? recoveryConfirmationFocus : recoveryPasswordFocus,
                Placeholder = Loc.Password, // Both hints are "password" in the reference.
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
                .Border(1, valid ? new Color(246, 246, 246) : color)
                .CornerRadius(16 * r)
                .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
                .WithKey(key);
            return new VStack(
                new VStack(
                    field,
                    new Button(visible ? "\ue6bd" : "\ue6be", () => setVisible(!visible))
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
                valid || !recovery.ConfirmationTouched
                    ? new VStack()
                    : Label(Loc.PasswordMismatchError, 12)
                        .ForegroundColor(color)
                        .TextAlignment(TextAlignment.Left)
                        .Padding(12 * w, 8)
            );
        }

        VStack content;
        Action back;
        switch (page)
        {
            case ApplicationScreen.ResetPassword:
                back = () => NavigateAuth(ApplicationScreen.Login);
                var validEmail = recovery.IsEmailValid;
                var emailField = new TextField(recovery.Email ?? "", text => recovery.Email = text)
                {
                    Placeholder = Loc.Email,
                    Focus = recoveryEmailFocus,
                    OnFocusChanged = focused =>
                    {
                        if (!focused)
                            recovery.EmailError = !recovery.IsEmailValid;
                    },
                    Input = new()
                    {
                        PlaceholderColor = AuthHint,
                        Keyboard = TextKeyboard.Email,
                        Autocorrect = false,
                    },
                }
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * w)
                    .ForegroundColor(validEmail ? Ink : AuthWarning)
                    .Height(56 * h)
                    .Padding(16 * w)
                    .BackgroundColor(validEmail ? theme.Surface : theme.ErrorSurface)
                    .Border(1, validEmail ? new Color(246, 246, 246) : AuthWarning)
                    .CornerRadius(16 * r)
                    .OnState(InteractionState.Focused, new(BorderWidth: 1, BorderColor: Accent))
                    .WithKey("recovery-email");
                content = new VStack(
                    Image("images/forgot_password_header.png", 324f / 343),
                    Gap(24),
                    Label(Loc.ForgotPasswordTitle, 32, "Bold").TextAlignment(TextAlignment.Left),
                    Gap(8),
                    Label(Loc.ForgotPasswordBody, 14),
                    Gap(24),
                    emailField,
                    recovery.EmailError && !validEmail
                        ? Label(Loc.EmailError, 12)
                            .ForegroundColor(AuthWarning)
                            .TextAlignment(TextAlignment.Left)
                            .Padding(12 * w, 8)
                        : new VStack(),
                    Gap(16),
                    Submit(
                        () => _ = recovery.RequestResetAsync(lifetime.Token),
                        recovery.CanRequestReset,
                        width
                    )
                );
                break;
            case ApplicationScreen.PasswordCode:
                back = () => NavigateAuth(ApplicationScreen.ResetPassword);
                content = new VStack(
                    Image("images/code.png", 686f / 694),
                    Label(Loc.EnterCode, 32, "Bold"),
                    Gap(8),
                    Label(Loc.EnterCodeBody, 14),
                    Gap(48),
                    AuthPin(
                        width,
                        w,
                        h,
                        r,
                        Label,
                        recovery.Code,
                        value => recovery.Code = value,
                        pinFocus,
                        "recovery-pin-editor"
                    ),
                    Gap(48),
                    Submit(
                        () =>
                        {
                            if (!recovery.AdvanceToPassword())
                                return;
                            NavigateAuth(ApplicationScreen.NewPassword);
                        },
                        recovery.CanContinueCode,
                        width
                    )
                );
                break;
            default:
                back = () => NavigateAuth(ApplicationScreen.ResetPassword);
                content = new VStack(
                    Image("images/forgot_password_header.png", 324f / 343),
                    Gap(24),
                    Label(Loc.EnterNewPasswordTitle, 32, "Bold").TextAlignment(TextAlignment.Left),
                    Gap(8),
                    Label(Loc.EnterNewPasswordBody, 14),
                    Gap(24),
                    Password(
                        recovery.Password,
                        value => recovery.Password = value,
                        recovery.PasswordVisible,
                        value => recovery.PasswordVisible = value,
                        "reset-password",
                        false
                    ),
                    Gap(32),
                    Password(
                        recovery.Confirmation,
                        value => recovery.Confirmation = value,
                        recovery.ConfirmationVisible,
                        value => recovery.ConfirmationVisible = value,
                        "reset-confirmation",
                        true
                    ),
                    Gap(32),
                    Submit(() => _ = ConfirmPasswordResetAsync(), recovery.CanConfirmReset, width)
                );
                break;
        }
        Element result = SafeArea(
            new VStack(
                AuthAppBar(back),
                new ScrollView(content.Width(dimensions.Width).Padding(16 * w, 0)).Grow().Shrink()
            )
                .HeightPercent(100)
                .BackgroundColor(theme.Background)
        );
        if (recovery.ResponsePopupVisible)
        {
            var panelWidth = dimensions.Width - 20 * r;
            var innerWidth = panelWidth - 48;
            var panel = new VStack(
                new Image(FniAssets.Image("images/password_popup.png"))
                    .Fit(ImageFit.Contain)
                    .Width(innerWidth)
                    .Height(innerWidth * 612 / 622),
                Gap(16),
                Label(
                    recovery.RequestSucceeded ? Loc.EmailSendSuccessTitle : Loc.EmailSendErrorTitle,
                    24,
                    "Bold"
                ),
                Label(
                    recovery.RequestSucceeded ? Loc.EmailSendSuccessBody : Loc.EmailSendErrorBody,
                    14
                ),
                Gap(32),
                Submit(
                    () =>
                    {
                        recovery.ResponsePopupVisible = false;
                        if (recovery.RequestSucceeded)
                            NavigateAuth(ApplicationScreen.PasswordCode);
                    },
                    true,
                    innerWidth
                )
            ).Width(panelWidth).Padding(24, 16, 24, 24).CrossAlignment(CrossAxisAlignment.Center).BackgroundColor(theme.Mode == ThemeMode.Dark ? new Color(22, 22, 22) : theme.Surface).CornerRadius(28);
            result = new VStack(
                result,
                new VStack(panel)
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .BackgroundColor(new Color(0, 0, 0, 138))
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
            )
                .HeightPercent(100)
                .BackgroundColor(theme.Background);
        }
        return result;
    }

    public override Element Build() => Recovery(page);

    public override void Leave()
    {
        base.Leave();
        recoveryEmailFocus.Unfocus();
        recoveryPasswordFocus.Unfocus();
        recoveryConfirmationFocus.Unfocus();
    }
}
