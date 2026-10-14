using DotNative;

namespace FaliNamIgrac;

public sealed record ReportingAccessState(
    bool IsAdmin = false,
    bool Checked = false,
    ReportingIssue? Issue = null,
    DateTimeOffset? RetryAt = null
);

/// <summary>Menu visibility only. Every protected endpoint still authorizes on the server.</summary>
public sealed class AdminAccessNotifier(
    IUiDispatcher dispatcher,
    ReportingSession session,
    IReportingApi api,
    TimeProvider? clock = null
) : ReportingNotifier<ReportingAccessState>(dispatcher, session, new(), clock)
{
    protected override ReportingAccessState WithIssue(
        ReportingAccessState state,
        ReportingIssue? issue,
        DateTimeOffset? retryAt
    ) => new(Issue: issue, RetryAt: retryAt); // Failed or in-flight refresh hides previously granted visibility.

    protected override DateTimeOffset? RetryAt(ReportingAccessState state) => state.RetryAt;

    public Task<bool> RefreshAsync(CancellationToken token = default) =>
        Execute(async (account, ct) => new(await api.AdminAsync(account, ct), true), token);
}
