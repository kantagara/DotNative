namespace FaliNamIgrac;

public interface ISessionService : IDisposable
{
    bool IsSignedIn { get; }

    string Email { get; }

    Task SignInAsync(string email, string password, CancellationToken token);

    Task<AccountInfo> AcceptWebSessionAsync(
        string accessToken,
        string refreshToken,
        int expiresIn,
        string idToken,
        CancellationToken token
    );

    Task SignUpAsync(string email, string password, CancellationToken token);

    Task ResendSignUpCodeAsync(string email, CancellationToken token) =>
        Task.FromException(new NotSupportedException("authResendUnavailable"));

    Task ConfirmSignUpAsync(string email, string code, CancellationToken token);

    Task<AccountInfo> GetAccountInfoAsync(CancellationToken token);

    Task RequestPasswordResetAsync(string email, CancellationToken token);

    Task ConfirmPasswordResetAsync(
        string email,
        string code,
        string password,
        CancellationToken token
    );

    Task<bool> RestoreAsync(CancellationToken token);

    Task<string> AccessTokenAsync(CancellationToken token);

    Task<string> IdTokenAsync(CancellationToken token);

    Task SignOutAsync(CancellationToken token);
}
