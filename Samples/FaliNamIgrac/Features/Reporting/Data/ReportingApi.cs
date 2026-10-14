using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FaliNamIgrac;

/// <summary>Caller owns HttpClient. BaseAddress must include the API stage and trailing slash.</summary>
public sealed class ReportingApi(HttpClient http, TimeProvider? time = null) : IReportingApi
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public async Task CreateAsync(
        ReportingAccount account,
        int reportedUserId,
        string reason,
        CancellationToken token
    )
    {
        ReportingRules.Target(account.UserId, reportedUserId);
        reason = ReportingRules.Reason(reason, null);
        using var content = JsonContent.Create(
            new CreateUserReport(account.UserId, reportedUserId, reason),
            ReportingJsonContext.Default.CreateUserReport
        );
        using var response = await Send(HttpMethod.Post, "reports/create", account, content, token);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
            throw new ReportingException(ReportingIssue.InvalidResponse);
    }

    public Task<ReportingBanStatus> BanAsync(ReportingAccount account, CancellationToken token) =>
        Read(
            HttpMethod.Get,
            $"reviews-data/reports/{account.UserId}",
            account,
            null,
            ReportingJsonContext.Default.ReportingBanStatus,
            token
        );

    public async Task<bool> AdminAsync(ReportingAccount account, CancellationToken token) =>
        (
            await Read(
                HttpMethod.Get,
                "reports/am-i-admin",
                account,
                null,
                ReportingJsonContext.Default.ReportingAdminAccess,
                token
            )
        ).IsAdmin;

    public async Task<IReadOnlyList<ModerationReport>> ReportsAsync(
        ReportingAccount account,
        CancellationToken token
    )
    {
        var reports = await Read(
            HttpMethod.Get,
            "reports/all-reports",
            account,
            null,
            ReportingJsonContext.Default.ModerationReportArray,
            token
        );
        if (
            reports.Any(r =>
                r is null
                || r.Id <= 0
                || r.ReportedUserId <= 0
                || r.ReportingUserId <= 0
                || r.Status is null
                || r.Reason is null
            )
        )
            throw new ReportingException(ReportingIssue.InvalidResponse);
        return reports.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToArray();
    }

    public async Task UpdateAsync(
        ReportingAccount account,
        int reportId,
        UpdateUserReport decision,
        CancellationToken token
    )
    {
        Positive(reportId);
        ReportingRules.Decision(decision);
        using var content = JsonContent.Create(
            decision with
            {
                Start = decision.Start?.ToUniversalTime(),
            },
            ReportingJsonContext.Default.UpdateUserReport
        );
        using var response = await Send(
            HttpMethod.Put,
            $"reports/update/{reportId}",
            account,
            content,
            token
        );
        RequireOk(response);
    }

    public async Task RevokeAsync(ReportingAccount account, int reportId, CancellationToken token)
    {
        Positive(reportId);
        using var response = await Send(
            HttpMethod.Put,
            $"reports/revoke/{reportId}",
            account,
            null,
            token
        );
        RequireOk(response);
    }

    public async Task<int> UnbanAsync(ReportingAccount account, int userId, CancellationToken token)
    {
        Positive(userId);
        var result = await Read(
            HttpMethod.Put,
            $"reports/unban/{userId}",
            account,
            null,
            ReportingJsonContext.Default.ReportingUnbanResult,
            token
        );
        if (result.RevokedCount < 0)
            throw new ReportingException(ReportingIssue.InvalidResponse);
        return result.RevokedCount;
    }

    private static void Positive(int id)
    {
        if (id <= 0)
            throw new ReportingException(ReportingIssue.InvalidTarget);
    }

    private static void RequireOk(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new ReportingException(ReportingIssue.InvalidResponse);
    }

    private async Task<T> Read<T>(
        HttpMethod method,
        string path,
        ReportingAccount account,
        HttpContent? content,
        JsonTypeInfo<T> type,
        CancellationToken token
    )
    {
        using var response = await Send(method, path, account, content, token);
        RequireOk(response);
        try
        {
            return await response.Content.ReadFromJsonAsync(type, token)
                ?? throw new ReportingException(ReportingIssue.InvalidResponse);
        }
        catch (JsonException)
        {
            throw new ReportingException(ReportingIssue.InvalidResponse);
        }
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        ReportingAccount account,
        HttpContent? content,
        CancellationToken token
    )
    {
        Positive(account.UserId);
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new ReportingException(ReportingIssue.Unauthorized);
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            account.AccessToken
        );
        var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
        if (token.IsCancellationRequested)
        {
            response.Dispose();
            token.ThrowIfCancellationRequested();
        }
        if (response.IsSuccessStatusCode)
            return response;
        var issue = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ReportingIssue.Unauthorized,
            HttpStatusCode.Forbidden => ReportingIssue.Forbidden,
            HttpStatusCode.Conflict => ReportingIssue.AlreadyReported,
            HttpStatusCode.TooManyRequests => ReportingIssue.RateLimited,
            HttpStatusCode.NotFound => ReportingIssue.NotFound,
            _ => ReportingIssue.Network,
        };
        var retry = response.Headers.RetryAfter;
        var retryAt =
            retry?.Date
            ?? (retry?.Delta is { } delta ? clock.GetUtcNow() + delta : (DateTimeOffset?)null);
        response.Dispose();
        throw new ReportingException(issue, retryAt);
    }
}
