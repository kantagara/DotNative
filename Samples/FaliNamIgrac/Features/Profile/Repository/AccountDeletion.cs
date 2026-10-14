using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using DotNative.Preferences;

namespace FaliNamIgrac;

public enum AccountDeletionStage
{
    None,
    PushDetached,
    BackendDeleted,
    IdentityDeleted,
    Completed,
}

public sealed record AccountDeletionCheckpoint(string AccountId, AccountDeletionStage Stage);

[JsonSerializable(typeof(AccountDeletionCheckpoint))]
internal partial class AccountDeletionJson : JsonSerializerContext;

public interface IAccountDeletionJournal
{
    Task<AccountDeletionStage> ReadAsync(string accountId, CancellationToken token);
    Task WriteAsync(string accountId, AccountDeletionStage stage, CancellationToken token);
}

/// <summary>No tokens or other credentials are persisted. Only the account-scoped recovery checkpoint.</summary>
public sealed class PreferencesAccountDeletionJournal(IPreferences preferences)
    : IAccountDeletionJournal
{
    private static string Key(string id) => "fni.delete.v1." + Uri.EscapeDataString(id);

    public async Task<AccountDeletionStage> ReadAsync(string accountId, CancellationToken token)
    {
        var value = await preferences.GetAsync(Key(accountId), "", token);
        if (string.IsNullOrEmpty(value))
            return AccountDeletionStage.None;
        var checkpoint =
            JsonSerializer.Deserialize(value, AccountDeletionJson.Default.AccountDeletionCheckpoint)
            ?? throw new InvalidDataException();
        if (checkpoint.AccountId != accountId || !Enum.IsDefined(checkpoint.Stage))
            throw new InvalidDataException();
        return checkpoint.Stage;
    }

    public Task WriteAsync(string accountId, AccountDeletionStage stage, CancellationToken token) =>
        preferences.SetAsync(
            Key(accountId),
            JsonSerializer.Serialize(
                new AccountDeletionCheckpoint(accountId, stage),
                AccountDeletionJson.Default.AccountDeletionCheckpoint
            ),
            token
        );
}

public interface IAccountDeletionRemote
{
    Task DeleteBackendAsync(string accountId, string accessToken, CancellationToken token);
    Task DeleteIdentityAsync(string accessToken, CancellationToken token);
}

/// <summary>The supplied HTTP client's base address must be the existing backend API base URI.</summary>
public sealed class AccountDeletionRemote(HttpClient http, IAmazonCognitoIdentityProvider cognito)
    : IAccountDeletionRemote
{
    public async Task DeleteBackendAsync(
        string accountId,
        string accessToken,
        CancellationToken token
    )
    {
        if (!int.TryParse(accountId, out var id) || id <= 0)
            throw new ArgumentException("Invalid account ID.");
        using var request = new HttpRequestMessage(HttpMethod.Delete, "users/" + id);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, token);
        // Do not infer absence from a gateway 404 or a backend 500 after a partial deletion.
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException(
                "Backend account deletion is not confirmed.",
                null,
                response.StatusCode
            );
    }

    public async Task DeleteIdentityAsync(string accessToken, CancellationToken token)
    {
        await cognito.DeleteUserAsync(new DeleteUserRequest { AccessToken = accessToken }, token);
    }
}

/// <summary>Integrator must bind this lease to a single session generation and cancel it at logout/account change.</summary>
public sealed record AccountDeletionLease(
    string AccountId,
    string AccessToken,
    CancellationToken SessionToken
)
{
    public override string ToString() => $"AccountDeletionLease({AccountId})";
}

public interface IAccountDeletionIntegration
{
    Task<AccountDeletionLease> CaptureAsync(
        string accountId,
        AccountDeletionStage stage,
        CancellationToken token
    );

    // Unregister only this device's push token while backend authentication still works. Must be idempotent.
    Task DetachPushAsync(AccountDeletionLease lease, CancellationToken token);

    // Idempotently clear this account's chat/media cache, account preferences, and secure credentials.
    // Do not delete a shared cache directory or credentials belonging to a different session generation.
    Task ClearAccountDataAsync(AccountDeletionLease lease, CancellationToken token);

    // Publish signed-out navigation only after both remote deletions and local cleanup have succeeded.
    Task FinishAsync(AccountDeletionLease lease, CancellationToken token);
}

public sealed class AccountDeletionWorkflow(
    IAccountDeletionRemote remote,
    IAccountDeletionJournal journal,
    IAccountDeletionIntegration integration
)
{
    public async Task RunAsync(
        string accountId,
        Action<AccountDeletionStage> progress,
        CancellationToken token
    )
    {
        var stage = await journal.ReadAsync(accountId, token);
        var lease = await integration.CaptureAsync(accountId, stage, token);
        if (lease.AccountId != accountId)
            throw new InvalidOperationException("Deletion session mismatch.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            lease.SessionToken
        );
        var ct = linked.Token;
        ct.ThrowIfCancellationRequested();
        progress(stage);
        async Task Commit(AccountDeletionStage next)
        {
            // Publish the confirmed remote outcome even if persistence fails; never report total success.
            stage = next;
            progress(stage);
            await journal.WriteAsync(accountId, next, ct);
        }
        if (stage < AccountDeletionStage.PushDetached)
        {
            ct.ThrowIfCancellationRequested();
            await integration.DetachPushAsync(lease, ct);
            await Commit(AccountDeletionStage.PushDetached);
        }
        if (stage < AccountDeletionStage.BackendDeleted)
        {
            ct.ThrowIfCancellationRequested();
            await remote.DeleteBackendAsync(accountId, lease.AccessToken, ct);
            await Commit(AccountDeletionStage.BackendDeleted);
        }
        if (stage < AccountDeletionStage.IdentityDeleted)
        {
            ct.ThrowIfCancellationRequested();
            await remote.DeleteIdentityAsync(lease.AccessToken, ct);
            await Commit(AccountDeletionStage.IdentityDeleted);
        }
        ct.ThrowIfCancellationRequested();
        await integration.ClearAccountDataAsync(lease, ct);
        // Store completion before Finish invalidates the session lease. Finish is retryable.
        await journal.WriteAsync(accountId, AccountDeletionStage.Completed, ct);
        ct.ThrowIfCancellationRequested();
        await integration.FinishAsync(lease, ct);
        progress(AccountDeletionStage.Completed);
    }
}
