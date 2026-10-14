using DotNative;
using DotNative.Plugins;
using DotNative.SecureStorage;

namespace FaliNamIgrac;

public sealed class RegistrationChallenge
{
    public string Email { get; }
    internal string Password { get; }
    public bool CodeConfirmed { get; internal set; }

    internal RegistrationChallenge(string email, string password) =>
        (Email, Password) = (email, password);
}

public sealed record RegistrationIdentity(
    string Email,
    string TemporaryUserId,
    AccountInfo Account
);

public sealed class RegistrationUnavailableException : Exception
{
    public RegistrationUnavailableException()
        : base("registrationNotPossible") { }
}

public sealed class RegistrationService : IRegistrationService
{
    private readonly ISessionService session;
    private readonly IRegistrationApi api;
    private readonly Func<ISecureStorage> storageFactory;
    private ISecureStorage? secureStorage;
    private ISecureStorage storage => secureStorage ??= storageFactory();

    public RegistrationService(
        ISessionService session,
        IApiClient backend,
        IPlatformChannels channels,
        PresentationTarget target
    )
        : this(session, new RegistrationApi(backend), () => SecureStorage.Create(channels, target))
    { }

    public RegistrationService(
        ISessionService session,
        IRegistrationApi api,
        Func<ISecureStorage> storageFactory
    ) => (this.session, this.api, this.storageFactory) = (session, api, storageFactory);

    private static string ProgressKey(string prefix, string email) =>
        prefix
        + "-"
        + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())
            )
        );

    public async Task<bool> IsFullyRegisteredAsync(CancellationToken token) =>
        await api.CheckAuthenticatedAsync(session.Email, token)
        == UserRegistrationStatus.FullyRegistered;

    public async Task ClearTemporaryUserAsync(CancellationToken token)
    {
        await storage.DeleteAsync(ProgressKey("registration-v2", session.Email), token);
        await storage.DeleteAsync("tempUserId", token);
        await storage.DeleteAsync("tempUserEmail", token);
    }

    public async Task<RegistrationIdentity?> RestoreTemporaryUserAsync(CancellationToken token)
    {
        if (!session.IsSignedIn)
            return null;
        var saved = await storage.ReadAsync(ProgressKey("registration-v2", session.Email), token);
        if (saved is not null)
        {
            var pending = System.Text.Json.JsonSerializer.Deserialize(
                saved,
                AuthProgressJson.Default.PendingRegistration
            );
            return
                pending is not null
                && string.Equals(pending.Email, session.Email, StringComparison.OrdinalIgnoreCase)
                ? new RegistrationIdentity(
                    pending.Email,
                    pending.TemporaryUserId,
                    new AccountInfo(Email: pending.Email)
                )
                : null;
        }
        var id = await storage.ReadAsync("tempUserId", token);
        var email = await storage.ReadAsync("tempUserEmail", token);
        // Never reuse an incomplete registration belonging to another session.
        // Older unscoped entries are resolved through the backend instead.
        return
            !string.IsNullOrEmpty(id)
            && string.Equals(email, session.Email, StringComparison.OrdinalIgnoreCase)
            ? new RegistrationIdentity(session.Email, id, new AccountInfo())
            : null;
    }

    public async Task<RegistrationIdentity?> ResolveSignedInUserAsync(
        AccountInfo account,
        AccountAuthType? authType,
        bool restoringSession,
        CancellationToken token
    )
    {
        var email = account.Email ?? session.Email;
        var status = await api.CheckAuthenticatedAsync(email, token);
        if (status == UserRegistrationStatus.FullyRegistered)
        {
            await ClearTemporaryUserAsync(token);
            return null;
        }

        var temporaryId = await GetOrCreateTemporaryUserAsync(
            email,
            authType ?? InferAuthType(account),
            token
        );
        if (!string.IsNullOrEmpty(temporaryId))
            return new RegistrationIdentity(email, temporaryId, account);

        // Another request may have completed registration between the status
        // check and the temporary-user lookup. Never create a duplicate profile.
        if (await api.CheckAsync(email, token) == UserRegistrationStatus.FullyRegistered)
        {
            await ClearTemporaryUserAsync(token);
            return null;
        }
        throw new InvalidOperationException(
            status
                is UserRegistrationStatus.TempUser
                    or UserRegistrationStatus.RegisteredButNotInTempOrUsers
                ? "Registration state is invalid. Please try again."
                : "Prijava nije završena. Pokušaj ponovo."
        );
    }

    private static AccountAuthType InferAuthType(AccountInfo account)
    {
        var identities = account.Identities ?? "";
        try
        {
            var providers = System.Text.Json.JsonSerializer.Deserialize(
                identities,
                BackendJson.Default.CognitoIdentityArray
            );
            if (providers is { Length: > 0 })
            {
                var identity = providers[0];
                identities = string.Join(
                    " ",
                    identity.ProviderName,
                    identity.ProviderType,
                    identity.UserId
                );
            }
        }
        catch (System.Text.Json.JsonException) { }
        return identities.Contains("google", StringComparison.OrdinalIgnoreCase)
                ? AccountAuthType.Google
            : identities.Contains("apple", StringComparison.OrdinalIgnoreCase)
                ? AccountAuthType.Apple
            : identities.Contains("facebook", StringComparison.OrdinalIgnoreCase)
                ? AccountAuthType.Facebook
            : AccountAuthType.Email;
    }

    public async Task<RegistrationChallenge?> ResumeConfirmationAsync(
        string email,
        string password,
        CancellationToken token
    )
    {
        var json = await storage.ReadAsync(ProgressKey("confirmation-v1", email), token);
        if (json is null)
            return null;
        var saved = System.Text.Json.JsonSerializer.Deserialize(
            json,
            AuthProgressJson.Default.PendingConfirmation
        );
        return
            saved is not null
            && string.Equals(email.Trim(), saved.Email, StringComparison.OrdinalIgnoreCase)
            ? new RegistrationChallenge(saved.Email, password) { CodeConfirmed = saved.Confirmed }
            : null;
    }

    private Task SaveConfirmationAsync(RegistrationChallenge challenge, CancellationToken token) =>
        storage.WriteAsync(
            ProgressKey("confirmation-v1", challenge.Email),
            System.Text.Json.JsonSerializer.Serialize(
                new PendingConfirmation(challenge.Email, challenge.CodeConfirmed),
                AuthProgressJson.Default.PendingConfirmation
            ),
            token
        );

    public async Task<RegistrationChallenge> BeginAsync(
        RegistrationForm form,
        CancellationToken token
    )
    {
        if (!form.Validate())
            throw new ArgumentException("Registration form is not valid.", nameof(form));
        if (await api.CheckAsync(form.Email!, token) != UserRegistrationStatus.NotRegistered)
            throw new RegistrationUnavailableException();
        await session.SignUpAsync(form.Email!.Trim(), form.Password!, token);
        var challenge = new RegistrationChallenge(form.Email.Trim(), form.Password!);
        await SaveConfirmationAsync(challenge, token);
        return challenge;
    }

    public async Task<RegistrationIdentity> ConfirmAsync(
        RegistrationChallenge challenge,
        string code,
        CancellationToken token
    )
    {
        await ConfirmCodeAsync(challenge, code, token);
        return await SignInAfterRegistrationAsync(challenge, token);
    }

    public async Task ConfirmCodeAsync(
        RegistrationChallenge challenge,
        string code,
        CancellationToken token
    )
    {
        if (challenge.CodeConfirmed)
            return;
        await session.ConfirmSignUpAsync(challenge.Email, code, token);
        challenge.CodeConfirmed = true;
        await SaveConfirmationAsync(challenge, token);
    }

    public Task ResendCodeAsync(RegistrationChallenge challenge, CancellationToken token) =>
        session.ResendSignUpCodeAsync(challenge.Email, token);

    public async Task<RegistrationIdentity> SignInAfterRegistrationAsync(
        RegistrationChallenge challenge,
        CancellationToken token
    )
    {
        await session.SignInAsync(challenge.Email, challenge.Password, token);
        var temporaryId = await GetOrCreateTemporaryUserAsync(
            challenge.Email,
            AccountAuthType.Email,
            token
        );
        if (string.IsNullOrEmpty(temporaryId))
            throw new InvalidOperationException(
                "Failed to initialize registration. Please try again."
            );
        var account = await session.GetAccountInfoAsync(token);
        await storage.DeleteAsync(ProgressKey("confirmation-v1", challenge.Email), token);
        return new RegistrationIdentity(challenge.Email, temporaryId, account);
    }

    public async Task<string?> GetOrCreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    )
    {
        if (await api.CheckAsync(email, token) == UserRegistrationStatus.FullyRegistered)
            return null;
        var id = await api.GetTemporaryUserIdAsync(email, token);
        if (string.IsNullOrEmpty(id))
            id = await api.CreateTemporaryUserAsync(email, authType, token);
        if (!string.IsNullOrEmpty(id))
        {
            token.ThrowIfCancellationRequested();
            if (
                !session.IsSignedIn
                || !string.Equals(session.Email, email, StringComparison.OrdinalIgnoreCase)
            )
                throw new OperationCanceledException("Account changed.");
            await storage.WriteAsync(
                ProgressKey("registration-v2", email),
                System.Text.Json.JsonSerializer.Serialize(
                    new PendingRegistration(email, id),
                    AuthProgressJson.Default.PendingRegistration
                ),
                token
            );
        }
        return id;
    }
}
