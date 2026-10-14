namespace FaliNamIgrac;

/// <summary>Account epoch and credential snapshot. SetUser/Dispose are UI-thread operations.</summary>
public sealed class ReportingSession : IDisposable
{
    private readonly Func<CancellationToken, Task<string>> accessToken;
    private CancellationTokenSource lifetime = new();
    private IDisposable? subscription;
    private bool disposed;

    public ReportingSession(Func<CancellationToken, Task<string>> accessToken) =>
        this.accessToken = accessToken;

    public ReportingSession(SessionNotifier session, ISessionService auth)
        : this(auth.AccessTokenAsync)
    {
        void Sync() =>
            SetUser(
                session.State is SessionState.SignedIn signed
                && int.TryParse(signed.Profile.Id, out var id)
                    ? id
                    : null
            );
        Sync();
        subscription = session.Listen(Sync);
    }

    public int? UserId { get; private set; }
    public long Version { get; private set; }
    public CancellationToken Token => lifetime.Token;
    public event Action? Changed;

    public void SetUser(int? id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (id is <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (UserId == id)
            return;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        UserId = id;
        Version++;
        Changed?.Invoke();
    }

    public async Task<ReportingAccount> CaptureAsync(CancellationToken token)
    {
        var version = Version;
        var id = UserId ?? throw new ReportingException(ReportingIssue.Unauthorized);
        token.ThrowIfCancellationRequested();
        var credential = await accessToken(token);
        token.ThrowIfCancellationRequested();
        if (version != Version || disposed)
            throw new OperationCanceledException(token);
        if (string.IsNullOrWhiteSpace(credential))
            throw new ReportingException(ReportingIssue.Unauthorized);
        return new(id, credential);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        subscription?.Dispose();
        lifetime.Cancel();
        UserId = null;
        Version++;
        Changed?.Invoke();
        lifetime.Dispose();
    }
}
