using DotNative;

namespace FaliNamIgrac;

public sealed record ReportUserState(
    string Reason = "",
    string Context = "",
    bool Sent = false,
    ReportingIssue? Issue = null,
    DateTimeOffset? RetryAt = null
);

/// <summary>One instance per report form; reporter identity always comes from the bound session.</summary>
public sealed class ReportUserNotifier : ReportingNotifier<ReportUserState>
{
    private readonly IReportingApi api;
    private readonly BanGuardNotifier guard;
    private readonly long openedVersion;
    public ReportTarget Target { get; }
    public bool IsCurrent =>
        !IsDisposed && openedVersion == Session.Version && Session.UserId is not null;

    public ReportUserNotifier(
        IUiDispatcher dispatcher,
        ReportingSession session,
        IReportingApi api,
        BanGuardNotifier guard,
        ReportTarget target,
        TimeProvider? clock = null,
        string initialContext = ""
    )
        : base(dispatcher, session, new(Context: initialContext), clock)
    {
        this.api = api;
        this.guard = guard;
        Target = target;
        openedVersion = session.Version;
    }

    protected override void Reset()
    {
        base.Reset();
        SetState(new());
    }

    public void EditReason(string value)
    {
        if (!Busy && !State.Sent && IsCurrent)
            SetState(
                State with
                {
                    Reason = value,
                    Issue = State.Issue
                        is ReportingIssue.AlreadyReported
                            or ReportingIssue.RateLimited
                        ? State.Issue
                        : null,
                }
            );
    }

    public void EditContext(string value)
    {
        if (!Busy && !State.Sent && IsCurrent)
            SetState(
                State with
                {
                    Context = value,
                    Issue = State.Issue
                        is ReportingIssue.AlreadyReported
                            or ReportingIssue.RateLimited
                        ? State.Issue
                        : null,
                }
            );
    }

    protected override ReportUserState WithIssue(
        ReportUserState state,
        ReportingIssue? issue,
        DateTimeOffset? retryAt
    ) => state with { Issue = issue, RetryAt = retryAt };

    protected override DateTimeOffset? RetryAt(ReportUserState state) => state.RetryAt;

    public Task<bool> SubmitAsync(CancellationToken token = default)
    {
        if (guard.Busy || !IsCurrent || State.Sent || State.Issue == ReportingIssue.AlreadyReported)
            return Task.FromResult(false);
        return Execute(
            async (account, ct) =>
            {
                ReportingRules.Target(account.UserId, Target.UserId);
                var reason = ReportingRules.Reason(State.Reason, State.Context);
                if (!await guard.AllowActionAsync(ct))
                    throw new ReportingException(
                        guard.State.Status?.Banned == true
                            ? ReportingIssue.Banned
                            : guard.State.Issue ?? ReportingIssue.Network
                    );
                ct.ThrowIfCancellationRequested();
                await api.CreateAsync(account, Target.UserId, reason, ct);
                return State with { Sent = true, Issue = null, RetryAt = null };
            },
            token
        );
    }
}
