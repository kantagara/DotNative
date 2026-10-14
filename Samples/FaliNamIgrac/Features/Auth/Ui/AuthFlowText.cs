namespace FaliNamIgrac;

/// <summary>Agent 11 binds these strings to generated localization.</summary>
public sealed record AuthFlowText(
    string ResendCode,
    string CodeSent,
    string NicknameCheckFailed,
    string PlacesFailed,
    string ChallengeUnsupported,
    string SessionInvalid,
    string CreationUncertain
)
{
    public static AuthFlowText From(AppLocalizations text) =>
        new(
            text.AuthResendCode,
            text.AuthCodeSent,
            text.OnboardingNicknameCheckFailed,
            text.OnboardingPlacesFailed,
            text.AuthChallengeUnsupported,
            text.AuthSessionInvalid,
            text.OnboardingCreationUncertain
        );

    public static AuthFlowText English { get; } =
        new(
            "Resend code",
            "A new code has been sent.",
            "Could not check the nickname. Try again.",
            "Could not load places. Try again.",
            "This account requires an additional sign-in challenge that this app cannot yet complete.",
            "Your session could not be verified. Please sign in again.",
            "Profile creation could not be confirmed. Retry to check its status before continuing."
        );
}
