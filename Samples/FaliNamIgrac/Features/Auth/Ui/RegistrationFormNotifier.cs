using DotNative;
using DotNative.Composition;
using DotNative.Plugins;

namespace FaliNamIgrac;

public sealed class RegistrationFormNotifier(
    IUiDispatcher dispatcher,
    IRegistrationService registration,
    SessionNotifier session
) : Notifier<RegistrationFormState>(dispatcher, new())
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;

    public RegistrationForm RegistrationForm
    {
        get => State.RegistrationForm;
        set => SetState(State with { RegistrationForm = value });
    }

    public bool RegistrationPasswordVisible
    {
        get => State.RegistrationPasswordVisible;
        set => SetState(State with { RegistrationPasswordVisible = value });
    }

    public bool RegistrationConfirmationVisible
    {
        get => State.RegistrationConfirmationVisible;
        set => SetState(State with { RegistrationConfirmationVisible = value });
    }

    public bool RegistrationConfirmationTouched
    {
        get => State.RegistrationConfirmationTouched;
        set => SetState(State with { RegistrationConfirmationTouched = value });
    }

    private readonly State<bool> registrationAuthenticating = new(false);

    public bool RegistrationAuthenticating
    {
        get => registrationAuthenticating.Value;
        set => registrationAuthenticating.Value = value;
    }

    public string RegistrationCode
    {
        get => State.RegistrationCode;
        set => SetState(State with { RegistrationCode = value });
    }

    private readonly State<bool> consentPopup = new(false);

    public bool ConsentPopup
    {
        get => consentPopup.Value;
        set => consentPopup.Value = value;
    }

    private readonly State<string?> registrationError = new(null);

    public string? RegistrationError
    {
        get => registrationError.Value;
        set => registrationError.Value = value;
    }

    private readonly State<string?> registrationSnackbar = new(null);

    public string? RegistrationSnackbar
    {
        get => registrationSnackbar.Value;
        set => registrationSnackbar.Value = value;
    }

    public void SetEmail(string email) =>
        RegistrationForm = RegistrationForm with { Email = email };

    public void SetPassword(string password, bool confirmation)
    {
        RegistrationForm = confirmation
            ? RegistrationForm with
            {
                ConfirmationPassword = password,
            }
            : RegistrationForm with
            {
                Password = password,
            };
        if (confirmation)
            RegistrationConfirmationTouched = true;
    }

    public void ToggleConsent(bool privacy) =>
        RegistrationForm = privacy
            ? RegistrationForm with
            {
                PrivacyPolicy = RegistrationForm.PrivacyPolicy != true,
            }
            : RegistrationForm with
            {
                Terms = RegistrationForm.Terms != true,
            };

    public void TogglePasswordVisibility(bool confirmation)
    {
        if (confirmation)
            RegistrationConfirmationVisible = !RegistrationConfirmationVisible;
        else
            RegistrationPasswordVisible = !RegistrationPasswordVisible;
    }

    public State<bool> Confirming { get; } = new(false);
    private RegistrationChallenge? registrationChallenge;
    private CancellationTokenSource operations = new();
    public bool CanConfirm =>
        registrationChallenge is { CodeConfirmed: true } || RegistrationCode.Length == 6;

    public void Reset()
    {
        operations.Cancel();
        operations.Dispose();
        operations = new();
        RegistrationForm = new();
        RegistrationPasswordVisible = false;
        RegistrationConfirmationVisible = false;
        RegistrationConfirmationTouched = false;
        RegistrationCode = "";
        registrationChallenge = null;
        ConsentPopup = false;
    }

    public Task<bool> BeginAsync(CancellationToken token) =>
        RunExclusive(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                operations.Token
            );
            token = linked.Token;
            if (!RegistrationForm.Validate())
                return false;
            var challenge =
                await registration.ResumeConfirmationAsync(
                    RegistrationForm.Email!,
                    RegistrationForm.Password!,
                    token
                ) ?? await registration.BeginAsync(RegistrationForm, token);
            token.ThrowIfCancellationRequested();
            await uiDispatcher.InvokeAsync(() =>
            {
                registrationChallenge = challenge;
                RegistrationCode = "";
            });
            return true;
        });

    public Task<bool> ResendAsync(CancellationToken token) =>
        RunExclusive(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                operations.Token
            );
            if (registrationChallenge is not { CodeConfirmed: false } challenge || session.Busy)
                return false;
            await registration.ResendCodeAsync(challenge, linked.Token);
            return true;
        });

    // Session Busy is the sole gate for confirmation + sign-in; Authenticating is only display state.
    public Task ConfirmAsync(CancellationToken token) =>
        session.ConfirmRegistrationAsync(
            async token =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    token,
                    operations.Token
                );
                token = linked.Token;
                if (Busy || registrationChallenge is not { } challenge || !CanConfirm)
                    return null;
                Confirming.Value = true;
                try
                {
                    await registration.ConfirmCodeAsync(challenge, RegistrationCode, token);
                    await uiDispatcher.InvokeAsync(() => RegistrationAuthenticating = true);
                    var identity = await registration.SignInAfterRegistrationAsync(
                        challenge,
                        token
                    );
                    token.ThrowIfCancellationRequested();
                    await uiDispatcher.InvokeAsync(() =>
                    {
                        registrationChallenge = null;
                        RegistrationForm = new();
                    });
                    return identity;
                }
                finally
                {
                    await uiDispatcher.InvokeAsync(() =>
                    {
                        RegistrationAuthenticating = false;
                        Confirming.Value = false;
                    });
                }
            },
            token
        );
}
