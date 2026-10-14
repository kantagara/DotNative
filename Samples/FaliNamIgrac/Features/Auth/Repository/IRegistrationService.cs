namespace FaliNamIgrac;

public interface IRegistrationService
{
    Task<bool> IsFullyRegisteredAsync(CancellationToken token) =>
        Task.FromException<bool>(
            new NotSupportedException("Registration reconciliation is unavailable.")
        );

    Task ClearTemporaryUserAsync(CancellationToken token);

    Task<RegistrationIdentity?> RestoreTemporaryUserAsync(CancellationToken token);

    Task<RegistrationIdentity?> ResolveSignedInUserAsync(
        AccountInfo account,
        AccountAuthType? authType,
        bool restoringSession,
        CancellationToken token
    );

    Task<RegistrationChallenge?> ResumeConfirmationAsync(
        string email,
        string password,
        CancellationToken token
    ) => Task.FromResult<RegistrationChallenge?>(null);

    Task<RegistrationChallenge> BeginAsync(RegistrationForm form, CancellationToken token);

    Task<RegistrationIdentity> ConfirmAsync(
        RegistrationChallenge challenge,
        string code,
        CancellationToken token
    );

    Task ResendCodeAsync(RegistrationChallenge challenge, CancellationToken token) =>
        Task.FromException(new NotSupportedException("authResendUnavailable"));

    Task ConfirmCodeAsync(RegistrationChallenge challenge, string code, CancellationToken token);

    Task<RegistrationIdentity> SignInAfterRegistrationAsync(
        RegistrationChallenge challenge,
        CancellationToken token
    );

    Task<string?> GetOrCreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    );
}
