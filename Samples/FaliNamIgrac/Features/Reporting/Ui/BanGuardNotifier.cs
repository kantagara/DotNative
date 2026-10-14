using DotNative;

namespace FaliNamIgrac;

public sealed record BanGuardState(
    ReportingBanStatus? Status = null,
    ReportingIssue? Issue = null,
    DateTimeOffset? RetryAt = null
);

/// <summary>Server decides bans. Failed checks preserve a known ban; unknown status follows Flutter's fail-open policy.
/// Authentication failures never permit an action. Do not apply this guard to logout or ban refresh.</summary>
public sealed class BanGuardNotifier(
    IUiDispatcher dispatcher,
    ReportingSession session,
    IReportingApi api,
    TimeProvider? clock = null
) : ReportingNotifier<BanGuardState>(dispatcher, session, new(), clock)
{
    public bool BlocksActions =>
        Session.UserId is null
        || State.Status?.Banned == true
        || State.Issue is ReportingIssue.Unauthorized or ReportingIssue.Forbidden;

    protected override BanGuardState WithIssue(
        BanGuardState state,
        ReportingIssue? issue,
        DateTimeOffset? retryAt
    ) => state with { Issue = issue, RetryAt = retryAt };

    protected override DateTimeOffset? RetryAt(BanGuardState state) => state.RetryAt;

    public Task<bool> RefreshAsync(CancellationToken token = default) =>
        Execute(async (account, ct) => new(await api.BanAsync(account, ct)), token);

    public async Task<bool> AllowActionAsync(CancellationToken token = default)
    {
        var version = Session.Version;
        // An in-flight check must not permit a second action based on an old snapshot.
        if (Busy)
            return false;
        var refreshed = await RefreshAsync(token);
        var failedOpen =
            State.Issue
            is ReportingIssue.Network
                or ReportingIssue.InvalidResponse
                or ReportingIssue.RateLimited
                or ReportingIssue.NotFound;
        return (refreshed || failedOpen)
            && !token.IsCancellationRequested
            && version == Session.Version
            && !BlocksActions;
    }

    /// <summary>The caller must also hold its own double-submit lock. The delegate receives a token cancelled on account change.</summary>
    public async Task<bool> RunAllowedAsync(
        Func<CancellationToken, Task> action,
        CancellationToken token = default
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Session.Token);
        if (!await AllowActionAsync(linked.Token))
            return false;
        linked.Token.ThrowIfCancellationRequested();
        await action(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        return true;
    }
}
