using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.Runtime;
using DotNative;
using DotNative.Plugins;
using DotNative.SecureStorage;

namespace FaliNamIgrac;

internal sealed record SessionTokens(
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string? IdToken = null
);

[JsonSerializable(typeof(SessionTokens))]
[JsonSerializable(typeof(string))]
internal partial class SessionJson : JsonSerializerContext;

public sealed class SessionService : ISessionService
{
    private readonly Func<ISecureStorage> storageFactory;
    private ISecureStorage? secureStorage;
    private ISecureStorage storage => secureStorage ??= storageFactory();
    private readonly IAmazonCognitoIdentityProvider provider;
    private readonly bool ownsProvider;
    private long generation;

    public SessionService(IPlatformChannels channels, PresentationTarget target)
        : this(
            new AmazonCognitoIdentityProviderClient(
                new AnonymousAWSCredentials(),
                RegionEndpoint.GetBySystemName(BackendConfiguration.Region)
            ),
            () => SecureStorage.Create(channels, target)
        )
    {
        ownsProvider = true;
    }

    // Caller owns the injected provider. Enables deterministic transport/storage tests.
    public SessionService(
        IAmazonCognitoIdentityProvider provider,
        Func<ISecureStorage> storageFactory
    )
    {
        this.provider = provider;
        this.storageFactory = storageFactory;
    }

    private void CheckOperation(long revision, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (revision != Volatile.Read(ref generation))
            throw new OperationCanceledException("Session changed.");
    }

    private readonly SemaphoreSlim gate = new(1, 1);
    private SessionTokens? current;
    public bool IsSignedIn => current is not null;
    public string Email => current?.Email ?? "";

    public async Task SignInAsync(string email, string password, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        token.ThrowIfCancellationRequested();
        var revision = Interlocked.Increment(ref generation);
        await gate.WaitAsync(token);
        try
        {
            CheckOperation(revision, token);
            var pool = new CognitoUserPool(
                BackendConfiguration.PoolId,
                BackendConfiguration.ClientId,
                provider
            );
            var user = new CognitoUser(email.Trim(), BackendConfiguration.ClientId, pool, provider);
            var result = await user.StartWithSrpAuthAsync(
                new InitiateSrpAuthRequest { Password = password },
                token
            );
            var auth = CognitoSessionResult.RequireComplete(
                result.AuthenticationResult,
                result.ChallengeName?.Value
            );
            var session = new SessionTokens(
                email.Trim(),
                auth.AccessToken,
                auth.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(
                    auth.ExpiresIn
                        ?? throw new InvalidDataException("Cognito nije vratio trajanje sesije.")
                ),
                auth.IdToken
            );
            await Save(session, revision, token);
        }
        catch (NotAuthorizedException)
        {
            throw new InvalidOperationException("Email ili lozinka nisu ispravni.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AccountInfo> AcceptWebSessionAsync(
        string accessToken,
        string refreshToken,
        int expiresIn,
        string idToken,
        CancellationToken token
    )
    {
        if (
            string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(refreshToken)
            || string.IsNullOrWhiteSpace(idToken)
            || expiresIn <= 0
        )
            throw new InvalidDataException("Invalid authentication tokens.");
        token.ThrowIfCancellationRequested();
        var revision = Interlocked.Increment(ref generation);
        var user = await provider.GetUserAsync(
            new GetUserRequest { AccessToken = accessToken },
            token
        );
        var attributes = user.UserAttributes.ToDictionary(
            attribute => attribute.Name,
            attribute => attribute.Value
        );
        if (!attributes.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email))
            throw new InvalidDataException("The authenticated account has no email address.");
        await gate.WaitAsync(token);
        try
        {
            await Save(
                new SessionTokens(
                    email,
                    accessToken,
                    refreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                    idToken
                ),
                revision,
                token
            );
        }
        finally
        {
            gate.Release();
        }
        CheckOperation(revision, token);
        return AccountInfo.FromCognito(attributes);
    }

    public async Task SignUpAsync(string email, string password, CancellationToken token)
    {
        await provider.SignUpAsync(
            new SignUpRequest
            {
                ClientId = BackendConfiguration.ClientId,
                Username = email,
                Password = password,
                UserAttributes = [new AttributeType { Name = "email", Value = email }],
            },
            token
        );
    }

    public async Task ResendSignUpCodeAsync(string email, CancellationToken token)
    {
        await provider.ResendConfirmationCodeAsync(
            new ResendConfirmationCodeRequest
            {
                ClientId = BackendConfiguration.ClientId,
                Username = email.Trim(),
            },
            token
        );
    }

    public async Task ConfirmSignUpAsync(string email, string code, CancellationToken token)
    {
        await provider.ConfirmSignUpAsync(
            new ConfirmSignUpRequest
            {
                ClientId = BackendConfiguration.ClientId,
                Username = email,
                ConfirmationCode = code,
            },
            token
        );
    }

    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken token)
    {
        var revision = Volatile.Read(ref generation);
        var response = await provider.GetUserAsync(
            new GetUserRequest { AccessToken = await AccessTokenAsync(token) },
            token
        );
        var attributes = response.UserAttributes.ToDictionary(
            attribute => attribute.Name,
            attribute => attribute.Value
        );
        CheckOperation(revision, token);
        return AccountInfo.FromCognito(attributes);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken token)
    {
        await provider.ForgotPasswordAsync(
            new ForgotPasswordRequest
            {
                ClientId = BackendConfiguration.ClientId,
                Username = email,
            },
            token
        );
    }

    public async Task ConfirmPasswordResetAsync(
        string email,
        string code,
        string password,
        CancellationToken token
    )
    {
        await provider.ConfirmForgotPasswordAsync(
            new ConfirmForgotPasswordRequest
            {
                ClientId = BackendConfiguration.ClientId,
                Username = email,
                ConfirmationCode = code,
                Password = password,
            },
            token
        );
    }

    public async Task<bool> RestoreAsync(CancellationToken token)
    {
        var revision = Volatile.Read(ref generation);
        await gate.WaitAsync(token);
        try
        {
            var json = await storage.ReadAsync("session-v1", token);
            if (json is null)
                return false;
            CheckOperation(revision, token);
            var restored =
                JsonSerializer.Deserialize(json, SessionJson.Default.SessionTokens)
                ?? throw new InvalidDataException(
                    "Your session could not be verified. Please sign in again."
                );
            if (
                string.IsNullOrWhiteSpace(restored.Email)
                || string.IsNullOrWhiteSpace(restored.RefreshToken)
                || string.IsNullOrWhiteSpace(restored.AccessToken)
            )
                throw new InvalidDataException(
                    "Your session could not be verified. Please sign in again."
                );
            current = restored;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            current = null;
            await storage.DeleteAsync("session-v1", token);
            return false;
        }
        finally
        {
            gate.Release();
        }
        try
        {
            await AccessTokenAsync(token);
            CheckOperation(revision, token);
            return true;
        }
        catch (NotAuthorizedException)
        {
            // AccessTokenAsync already cleared the rejected session under its gate.
            // Do not log out a newer login that began while this restore was finishing.
            CheckOperation(revision, token);
            return false;
        }
    }

    public async Task<string> AccessTokenAsync(CancellationToken token)
    {
        var revision = Volatile.Read(ref generation);
        await gate.WaitAsync(token);
        try
        {
            CheckOperation(revision, token);
            var session = current ?? throw new InvalidOperationException("Potrebna je prijava.");
            if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return session.AccessToken;
            var response = await provider.InitiateAuthAsync(
                new InitiateAuthRequest
                {
                    ClientId = BackendConfiguration.ClientId,
                    AuthFlow = AuthFlowType.REFRESH_TOKEN_AUTH,
                    AuthParameters = new() { ["REFRESH_TOKEN"] = session.RefreshToken },
                },
                token
            );
            var auth = CognitoSessionResult.RequireComplete(
                response.AuthenticationResult,
                response.ChallengeName?.Value,
                requireRefresh: false
            );
            session = session with
            {
                AccessToken = auth.AccessToken,
                IdToken = auth.IdToken ?? session.IdToken,
                RefreshToken = auth.RefreshToken ?? session.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                    auth.ExpiresIn
                        ?? throw new InvalidDataException("Cognito nije vratio trajanje sesije.")
                ),
            };
            await Save(session, revision, token);
            return session.AccessToken;
        }
        catch (NotAuthorizedException)
        {
            if (revision == Volatile.Read(ref generation))
            {
                current = null;
                await storage.DeleteAsync("session-v1", CancellationToken.None);
            }
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> IdTokenAsync(CancellationToken token)
    {
        var revision = Volatile.Read(ref generation);
        await AccessTokenAsync(token);
        CheckOperation(revision, token);
        return current?.IdToken ?? throw new InvalidOperationException("Please sign in again.");
    }

    public async Task SignOutAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref generation);
        current = null;
        // Once logout starts it must delete persisted credentials even if the view leaves.
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            await storage.DeleteAsync("session-v1", CancellationToken.None);
            await storage.DeleteAsync("userId", CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task Save(SessionTokens session, long revision, CancellationToken token)
    {
        CheckOperation(revision, token);
        await storage.WriteAsync(
            "session-v1",
            JsonSerializer.Serialize(session, SessionJson.Default.SessionTokens),
            token
        );
        CheckOperation(revision, token);
        current = session;
    }

    public void Dispose()
    {
        if (ownsProvider)
            provider.Dispose();
        gate.Dispose();
    }
}
