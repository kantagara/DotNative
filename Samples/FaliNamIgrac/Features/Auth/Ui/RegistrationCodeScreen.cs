using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class RegistrationCodeScreen : PinScreen
{
    private readonly RegistrationFormNotifier form;
    private readonly AuthFlowText? fixedMessages;
    private AuthFlowText messages => fixedMessages ?? AuthFlowText.From(ui.Text);

    public RegistrationCodeScreen(
        UiContext ui,
        RegistrationFormNotifier form,
        AuthFlowText? text = null
    )
        : base(ui)
    {
        this.form = form;
        fixedMessages = text;
    }

    private Element RegistrationCodePage(Action back, Element busyIndicator)
    {
        if (presentation.Dimensions.Value is not { } dimensions)
            return SafeArea(Splash());
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var r = Math.Min(w, h);
        var width = dimensions.Width - 32 * r;
        Text Label(string text, float size, string face = "Regular") =>
            new Text(text)
                .TextStyle(TextStyleFor(face))
                .FontWeight(
                    face == "Bold" ? FontWeight.Bold
                    : face == "Medium" ? FontWeight.Medium
                    : FontWeight.Normal
                )
                .FontSize(size * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center);
        var visibleHeight = Math.Max(
            0,
            dimensions.Height - Insets.Top - Math.Max(Insets.Bottom, Insets.Keyboard) - 56
        );
        var page = new VStack(
            new VStack(
                new Image(FniAssets.Image("images/code.png"))
                    .Fit(ImageFit.Contain)
                    .Width(width)
                    .Height(width * 686 / 694),
                Label(Loc.EnterCode, 32, "Bold"),
                new VStack().Height(8 * h),
                Label(Loc.EnterCodeBody, 14).ForegroundColor(theme.MutedForeground)
            ),
            new VStack().Height(12 * h),
            AuthPin(
                width,
                w,
                h,
                r,
                Label,
                form.RegistrationCode,
                value => form.RegistrationCode = value,
                pinFocus,
                "registration-pin-editor"
            ),
            new VStack().Height(32 * h),
            Label(Loc.AuthCodeNotReceived, 14).ForegroundColor(AuthHint),
            new VStack(
                new Button(form.Busy ? "" : messages.ResendCode, () => _ = ResendAsync())
                    .TextStyle(TextStyleFor("Medium"))
                    .FontWeight(FontWeight.Medium)
                    .FontSize(16 * w)
                    .ForegroundColor(Accent)
                    .TextAlignment(TextAlignment.Center)
                    .Height(48 * h)
                    .WidthPercent(100)
                    .Enabled(!form.Busy && !form.Confirming.Value),
                form.Busy
                    ? new VStack(BusyIndicator(Accent))
                        .Position(PositionMode.Absolute)
                        .Insets(new(0, 0, 0, 0))
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center)
                    : new VStack()
            ),
            new VStack().Height(16 * h),
            RegistrationSubmit(
                Loc.ContinueLabel,
                () => _ = ConfirmRegistrationAsync(),
                form.CanConfirm && !form.Busy,
                form.Confirming.Value,
                busyIndicator,
                width,
                w,
                h,
                r
            )
        )
            .MinHeight(visibleHeight)
            .Padding(16 * r, 0, 16 * r, 24 * h);
        return SafeArea(
            new VStack(AuthAppBar(back), new ScrollView(page).Grow().Shrink())
                .HeightPercent(100)
                .BackgroundColor(theme.Background)
        );
    }

    public override Element Build() =>
        RegistrationCodePage(() => NavigateAuth(ApplicationScreen.Registration), BusyIndicator());

    private async Task ResendAsync()
    {
        try
        {
            if (await form.ResendAsync(lifetime.Token) && !disposed)
                ShowRecoveryToast(messages.CodeSent);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!disposed)
                ShowRecoveryToast(FniErrors.UserMessage(presentation.Text, error));
        }
    }

    private async Task ConfirmRegistrationAsync()
    {
        try
        {
            await form.ConfirmAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            ShowRecoveryToast(error.Message);
        }
    }
}
