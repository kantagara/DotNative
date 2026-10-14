namespace FaliNamIgrac;

public interface IRegistrationApi
{
    Task<UserRegistrationStatus> CheckAsync(string email, CancellationToken token);

    Task<UserRegistrationStatus> CheckAuthenticatedAsync(string email, CancellationToken token);

    Task<string?> GetTemporaryUserIdAsync(string email, CancellationToken token);

    Task<string?> CreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    );
}
