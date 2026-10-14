namespace FaliNamIgrac;

public enum ReportingIssue
{
    InvalidTarget,
    InvalidReason,
    Unauthorized,
    Forbidden,
    AlreadyReported,
    RateLimited,
    NotFound,
    Network,
    InvalidResponse,
    Banned,
    InvalidDecision,
}

public sealed class ReportingException(ReportingIssue issue, DateTimeOffset? retryAt = null)
    : Exception(issue.ToString())
{
    public ReportingIssue Issue { get; } = issue;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public enum ReportOrigin
{
    PlayerProfile,
    Chat,
    GameDetails,
}

public sealed record ReportTarget(int UserId, ReportOrigin Origin, string? ContextId = null);

public static class ReportingRules
{
    public const int MaxReasonLength = 500;

    // Context is included in the server's single reason field. There are no context/category endpoints.
    public static string Reason(string reason, string? context)
    {
        reason = reason.Trim();
        if (reason.Length == 0)
            throw new ReportingException(ReportingIssue.InvalidReason);
        var value = string.IsNullOrWhiteSpace(context) ? reason : reason + "\n\n" + context.Trim();
        if (value.Length > MaxReasonLength)
            throw new ReportingException(ReportingIssue.InvalidReason);
        return value;
    }

    public static void Target(int reporter, int reported)
    {
        if (reporter <= 0 || reported <= 0 || reporter == reported)
            throw new ReportingException(ReportingIssue.InvalidTarget);
    }

    public static void Decision(UpdateUserReport value)
    {
        if (value.Status == "DISMISSED" && value.Days is null && value.Start is null)
            return;
        if (value.Status == "APPROVED" && value.Days is >= 0 && value.Start is not null)
            return;
        throw new ReportingException(ReportingIssue.InvalidDecision);
    }
}

public interface IReportingApi
{
    Task CreateAsync(
        ReportingAccount account,
        int reportedUserId,
        string reason,
        CancellationToken token
    );
    Task<ReportingBanStatus> BanAsync(ReportingAccount account, CancellationToken token);
    Task<bool> AdminAsync(ReportingAccount account, CancellationToken token);
    Task<IReadOnlyList<ModerationReport>> ReportsAsync(
        ReportingAccount account,
        CancellationToken token
    );
    Task UpdateAsync(
        ReportingAccount account,
        int reportId,
        UpdateUserReport decision,
        CancellationToken token
    );
    Task RevokeAsync(ReportingAccount account, int reportId, CancellationToken token);
    Task<int> UnbanAsync(ReportingAccount account, int userId, CancellationToken token);
}
