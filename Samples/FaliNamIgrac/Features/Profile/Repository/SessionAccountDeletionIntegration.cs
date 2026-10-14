namespace FaliNamIgrac;

/// <summary>Session generation guard around the integrator's account-scoped cleanup operations.
/// Construct/dispose and invoke public entry points on the UI dispatcher, like SessionNotifier.</summary>
public sealed class SessionAccountDeletionIntegration : IAccountDeletionIntegration, IDisposable
{
    private readonly SessionNotifier state;
    private readonly ISessionService session;
    private readonly Func<AccountDeletionLease, CancellationToken, Task> detachPush;
    private readonly Func<AccountDeletionLease, CancellationToken, Task> clearData;
    private readonly Func<AccountDeletionLease, CancellationToken, Task> finish;
    private readonly IDisposable subscription;
    private CancellationTokenSource epoch = new();
    private string? accountId;
    private bool disposed;

    public SessionAccountDeletionIntegration(
        SessionNotifier state,
        ISessionService session,
        Func<AccountDeletionLease, CancellationToken, Task> detachPush,
        Func<AccountDeletionLease, CancellationToken, Task> clearData,
        Func<AccountDeletionLease, CancellationToken, Task> finish
    )
    {
        this.state = state;
        this.session = session;
        this.detachPush = detachPush;
        this.clearData = clearData;
        this.finish = finish;
        accountId = (state.State as SessionState.SignedIn)?.Profile.Id;
        subscription = state.Listen(() =>
        {
            var next = (state.State as SessionState.SignedIn)?.Profile.Id;
            if (next == accountId)
                return;
            epoch.Cancel();
            epoch.Dispose();
            epoch = new();
            accountId = next;
        });
    }

    public async Task<AccountDeletionLease> CaptureAsync(
        string id,
        AccountDeletionStage stage,
        CancellationToken token
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (state.State is not SessionState.SignedIn signed || signed.Profile.Id != id)
            throw new InvalidOperationException("Deletion requires the confirmed account session.");
        var generation = epoch.Token;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, generation);
        // A successfully deleted identity cannot refresh a token. Local cleanup recovery needs no credential.
        var accessToken =
            stage >= AccountDeletionStage.IdentityDeleted
                ? ""
                : await session.AccessTokenAsync(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        return new(id, accessToken, generation);
    }

    private void Verify(AccountDeletionLease lease, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        lease.SessionToken.ThrowIfCancellationRequested();
        if (lease.AccountId != accountId || lease.SessionToken != epoch.Token)
            throw new InvalidOperationException("Stale deletion session.");
    }

    public Task DetachPushAsync(AccountDeletionLease lease, CancellationToken token)
    {
        Verify(lease, token);
        return detachPush(lease, token);
    }

    public Task ClearAccountDataAsync(AccountDeletionLease lease, CancellationToken token)
    {
        Verify(lease, token);
        return clearData(lease, token);
    }

    public async Task FinishAsync(AccountDeletionLease lease, CancellationToken token)
    {
        Verify(lease, token);
        await finish(lease, token);
        if (state.State is not SessionState.SignedOut)
            throw new InvalidOperationException("Deletion cleanup did not finish the session.");
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        subscription.Dispose();
        epoch.Cancel();
        epoch.Dispose();
    }
}
