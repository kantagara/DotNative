using DotNative;

namespace FaliNamIgrac;

public enum ModerationAction
{
    Approve,
    Dismiss,
    Revoke,
    Unban,
}

public sealed record ModerationDecision(
    ModerationAction Action,
    int ReportId,
    int UserId,
    int? Days = null
);

public sealed record ModerationState(
    bool IsAdmin = false,
    bool Loaded = false,
    IReadOnlyList<ModerationReport>? Reports = null,
    ModerationDecision? Confirmation = null,
    bool Applied = false,
    int? RevokedCount = null,
    ReportingIssue? Issue = null,
    DateTimeOffset? RetryAt = null
);

public sealed class ModerationNotifier(
    IUiDispatcher dispatcher,
    ReportingSession session,
    IReportingApi api,
    TimeProvider? clock = null
) : ReportingNotifier<ModerationState>(dispatcher, session, new(), clock)
{
    protected override ModerationState WithIssue(
        ModerationState state,
        ReportingIssue? issue,
        DateTimeOffset? retryAt
    ) =>
        issue is ReportingIssue.Forbidden or ReportingIssue.Unauthorized
            ? new(Issue: issue, RetryAt: retryAt)
            : state with
            {
                Issue = issue,
                RetryAt = retryAt,
            };

    protected override DateTimeOffset? RetryAt(ModerationState state) => state.RetryAt;

    private async Task Authorize(ReportingAccount account, CancellationToken token)
    {
        if (!await api.AdminAsync(account, token))
            throw new ReportingException(ReportingIssue.Forbidden);
        token.ThrowIfCancellationRequested();
    }

    public Task<bool> LoadAsync(CancellationToken token = default) =>
        Execute(
            async (account, ct) =>
            {
                await Authorize(account, ct);
                var reports = await api.ReportsAsync(account, ct);
                return new(
                    true,
                    true,
                    reports,
                    Applied: State.Applied,
                    RevokedCount: State.RevokedCount
                );
            },
            token
        );

    public void Ask(ModerationAction action, int reportId, int? days = null)
    {
        if (Busy || !State.IsAdmin || !State.Loaded || Session.UserId is null)
            return;
        var report = State.Reports?.SingleOrDefault(r => r.Id == reportId);
        if (
            report is null
            || (action is ModerationAction.Approve or ModerationAction.Dismiss && !report.Pending)
            || (action is ModerationAction.Revoke or ModerationAction.Unban && !report.Approved)
            || (action == ModerationAction.Approve && days is not (0 or 7 or 30 or 365))
        )
        {
            SetState(State with { Issue = ReportingIssue.InvalidDecision });
            return;
        }
        SetState(
            State with
            {
                Confirmation = new(
                    action,
                    reportId,
                    report.ReportedUserId,
                    action == ModerationAction.Approve ? days : null
                ),
                Applied = false,
                RevokedCount = null,
                Issue = null,
            }
        );
    }

    public void CancelConfirmation()
    {
        if (!Busy)
            SetState(State with { Confirmation = null });
    }

    public Task<bool> ConfirmAsync(CancellationToken token = default)
    {
        if (State.Confirmation is not { } decision || !State.IsAdmin || !State.Loaded)
            return Task.FromResult(false);
        return Execute(
            async (account, ct) =>
            {
                await Authorize(account, ct);
                int? count = null;
                switch (decision.Action)
                {
                    case ModerationAction.Approve:
                        await api.UpdateAsync(
                            account,
                            decision.ReportId,
                            new("APPROVED", decision.Days, Clock.GetUtcNow()),
                            ct
                        );
                        break;
                    case ModerationAction.Dismiss:
                        await api.UpdateAsync(account, decision.ReportId, new("DISMISSED"), ct);
                        break;
                    case ModerationAction.Revoke:
                        await api.RevokeAsync(account, decision.ReportId, ct);
                        break;
                    case ModerationAction.Unban:
                        count = await api.UnbanAsync(account, decision.UserId, ct);
                        break;
                    default:
                        throw new ReportingException(ReportingIssue.InvalidDecision);
                }
                ct.ThrowIfCancellationRequested();
                // Clear the confirmation before reloading. A reload retry never repeats a successful write.
                await PublishAsync(
                    State with
                    {
                        Applied = true,
                        RevokedCount = count,
                        Confirmation = null,
                        Loaded = false,
                    },
                    ct
                );
                ct.ThrowIfCancellationRequested();
                var reports = await api.ReportsAsync(account, ct);
                return new(true, true, reports, Applied: true, RevokedCount: count);
            },
            token
        );
    }
}
