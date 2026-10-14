using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DotNative;
using FaliNamIgrac;

var tests = new (string, Action)[]
{
    ("create payload, session credentials and context validation", Payload),
    ("all moderation HTTP contracts and ordering", ModerationContracts),
    ("HTTP failures, Retry-After and malformed JSON are explicit", Failures),
    ("invalid reason/self report never reach server", Validation),
    ("double submit and repeat success are suppressed", DoubleSubmit),
    ("409 stays terminal; 429 cooldown and retry", RateLimit),
    ("network and unauthorized failure never produce success", ErrorRetry),
    ("known ban blocks actions and survives failed refresh", Bans),
    ("server refresh controls unban, not client clock", UnbanRefresh),
    ("unknown status fails open but authentication fails closed", GuardPolicy),
    ("logout/A and switching accounts discard late report responses", AccountSwitch),
    ("late token resolution cannot authorize old account", LateCredentials),
    ("cancellation and disposal suppress success", Cancellation),
    ("admin access is server derived and rechecked before mutation", AdminAuthorization),
    ("moderation confirmation and duplicate-submit protection", AdminDoubleSubmit),
    ("successful decision with failed list refresh cannot repeat write", AdminRefreshFailure),
    ("revoke, dismiss and unban with zero-count outcome", AdminActions),
    ("admin logout drops private reports and late mutation", AdminLogout),
    ("ban refresh does not cross account epochs", BanLogout),
    ("guarded delegate is cancelled by logout", GuardedDelegate),
    ("admin menu uses server answer without reading private reports", AdminMenu),
};
foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"Passed {tests.Length} reporting test groups.");

static void Check(bool condition, string message = "assertion failed")
{
    if (!condition)
        throw new Exception(message);
}
static void Throws(ReportingIssue issue, Action action)
{
    try
    {
        action();
    }
    catch (ReportingException error)
    {
        Check(error.Issue == issue, "wrong issue: " + error.Issue);
        return;
    }
    throw new Exception("Expected " + issue);
}
static void Payload()
{
    using var handler = new Handler { Status = HttpStatusCode.Created };
    using var http = new HttpClient(handler)
    {
        BaseAddress = new("https://example.invalid/stage/"),
    };
    var api = new ReportingApi(http);
    var account = new ReportingAccount(7, "fixture-7");
    api.CreateAsync(account, 8, ReportingRules.Reason(" reason ", " chat context "), default)
        .GetAwaiter()
        .GetResult();
    var request = handler.Requests.Single();
    Check(
        request.Path == "/stage/reports/create"
            && request.Method == HttpMethod.Post
            && request.Auth == "Bearer fixture-7"
    );
    using var json = JsonDocument.Parse(request.Body!);
    Check(json.RootElement.EnumerateObject().Count() == 3);
    Check(
        json.RootElement.GetProperty("reportingUserId").GetInt32() == 7
            && json.RootElement.GetProperty("reportedUserId").GetInt32() == 8
    );
    Check(json.RootElement.GetProperty("reportReason").GetString() == "reason\n\nchat context");
    Check(!account.ToString().Contains("fixture"));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try
    {
        api.CreateAsync(account, 8, "reason", cancelled.Token).GetAwaiter().GetResult();
        throw new Exception("ignored cancel");
    }
    catch (OperationCanceledException)
    {
        Check(handler.Requests.Count == 1);
    }
}
static void ModerationContracts()
{
    using var handler = new Handler();
    using var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/api/") };
    var api = new ReportingApi(http);
    var account = new ReportingAccount(7, "fixture");
    handler.Body =
        "{\"banned\":true,\"reason\":\"temporary\",\"unbannedDate\":\"2026-01-01T00:00:00Z\"}";
    Check(api.BanAsync(account, default).Result.Banned);
    handler.Body = "{\"is_admin\":true}";
    Check(api.AdminAsync(account, default).Result);
    handler.Body =
        """[{"report_id":1,"reporting_user_id":2,"reported_user_id":3,"reason":"old","report_status":"PENDING","created_at":null},{"report_id":2,"reporting_user_id":2,"reported_user_id":3,"reason":"new","report_status":"PENDING","created_at":"2026-10-10T10:00:00Z"},{"report_id":3,"reporting_user_id":2,"reported_user_id":3,"reason":"tie","report_status":"PENDING","created_at":"2026-10-10T10:00:00Z"},{"report_id":4,"reporting_user_id":2,"reported_user_id":3,"reason":"legacy","report_status":"PENDING","created_at":"malformed"}]""";
    Check(
        api.ReportsAsync(account, default)
            .Result.Select(r => r.Id)
            .SequenceEqual(new[] { 3, 2, 4, 1 })
    );
    handler.Body = "{}";
    api.UpdateAsync(
            account,
            1,
            new("APPROVED", 0, new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(2))),
            default
        )
        .Wait();
    using (var json = JsonDocument.Parse(handler.Requests.Last().Body!))
    {
        Check(json.RootElement.GetProperty("report_status").GetString() == "APPROVED");
        Check(json.RootElement.GetProperty("ban_duration_days").GetInt32() == 0);
        Check(
            json.RootElement.GetProperty("ban_start_date").GetDateTimeOffset().Offset
                == TimeSpan.Zero
        );
    }
    api.UpdateAsync(account, 1, new("DISMISSED"), default).Wait();
    using (var json = JsonDocument.Parse(handler.Requests.Last().Body!))
        Check(json.RootElement.EnumerateObject().Count() == 1);
    api.RevokeAsync(account, 1, default).Wait();
    Check(handler.Requests.Last().Body is null);
    handler.Body = "{\"revoked_count\":0}";
    Check(api.UnbanAsync(account, 3, default).Result == 0);
    Check(
        handler
            .Requests.Select(r => r.Path)
            .SequenceEqual(
                new[]
                {
                    "/api/reviews-data/reports/7",
                    "/api/reports/am-i-admin",
                    "/api/reports/all-reports",
                    "/api/reports/update/1",
                    "/api/reports/update/1",
                    "/api/reports/revoke/1",
                    "/api/reports/unban/3",
                }
            )
    );
    Check(handler.Requests.Skip(3).All(r => r.Method == HttpMethod.Put));
    Throws(
        ReportingIssue.InvalidDecision,
        () => api.UpdateAsync(account, 1, new("APPROVED", -1), default).GetAwaiter().GetResult()
    );
}
static void Failures()
{
    var clock = new FakeClock();
    using var handler = new Handler();
    using var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/") };
    var api = new ReportingApi(http, clock);
    var account = new ReportingAccount(1, "fixture");
    foreach (
        var (status, issue) in new[]
        {
            (401, ReportingIssue.Unauthorized),
            (403, ReportingIssue.Forbidden),
            (409, ReportingIssue.AlreadyReported),
            (429, ReportingIssue.RateLimited),
            (404, ReportingIssue.NotFound),
            (500, ReportingIssue.Network),
        }
    )
    {
        handler.Status = (HttpStatusCode)status;
        Throws(
            issue,
            () => api.CreateAsync(account, 2, "reason", default).GetAwaiter().GetResult()
        );
    }
    handler.Status = HttpStatusCode.TooManyRequests;
    handler.RetryAfter = "90";
    try
    {
        api.CreateAsync(account, 2, "r", default).GetAwaiter().GetResult();
    }
    catch (ReportingException e)
    {
        Check(e.RetryAt == clock.GetUtcNow().AddSeconds(90));
    }
    handler.RetryAfter = clock.GetUtcNow().AddHours(1).ToString("R");
    try
    {
        api.CreateAsync(account, 2, "r", default).GetAwaiter().GetResult();
    }
    catch (ReportingException e)
    {
        Check(e.RetryAt == clock.GetUtcNow().AddHours(1));
    }
    handler.Status = HttpStatusCode.OK;
    foreach (var body in new[] { "{}", "null", "{\"banned\":\"false\"}", "bad" })
    {
        handler.Body = body;
        Throws(
            ReportingIssue.InvalidResponse,
            () => api.BanAsync(account, default).GetAwaiter().GetResult()
        );
    }
    handler.Body = "{}";
    Throws(
        ReportingIssue.InvalidResponse,
        () => api.AdminAsync(account, default).GetAwaiter().GetResult()
    );
    handler.Status = HttpStatusCode.Accepted;
    Throws(
        ReportingIssue.InvalidResponse,
        () => api.CreateAsync(account, 2, "reason", default).GetAwaiter().GetResult()
    );
}
static void Validation()
{
    using var f = new Fixture();
    using var form = f.Form();
    f.Ui.Finish(form.SubmitAsync());
    Check(form.State.Issue == ReportingIssue.InvalidReason && f.Api.Creates == 0);
    form.EditReason(new string('x', 500));
    form.EditContext("y");
    f.Ui.Finish(form.SubmitAsync());
    Check(form.State.Issue == ReportingIssue.InvalidReason);
    form.EditContext("");
    f.Ui.Finish(form.SubmitAsync());
    Check(form.State.Sent && f.Api.Creates == 1);
    using var self = f.Form(1);
    self.EditReason("valid");
    f.Ui.Finish(self.SubmitAsync());
    Check(self.State.Issue == ReportingIssue.InvalidTarget);
}
static void DoubleSubmit()
{
    using var f = new Fixture();
    using var form = f.Form();
    var wait = new TaskCompletionSource();
    f.Api.Create = _ => wait.Task;
    form.EditReason("reason");
    var first = form.SubmitAsync();
    f.Ui.Finish(form.SubmitAsync());
    Check(f.Api.Creates == 1 && form.Busy);
    form.EditReason("changed");
    Check(form.State.Reason == "reason");
    wait.SetResult();
    f.Ui.Finish(first);
    Check(form.State.Sent);
    f.Ui.Finish(form.SubmitAsync());
    Check(f.Api.Creates == 1);
}
static void RateLimit()
{
    using var f = new Fixture();
    using var form = f.Form();
    form.EditReason("reason");
    f.Api.Create = _ =>
        Task.FromException(
            new ReportingException(ReportingIssue.RateLimited, f.Clock.GetUtcNow().AddMinutes(1))
        );
    f.Ui.Finish(form.SubmitAsync());
    Check(!form.State.Sent && form.State.Issue == ReportingIssue.RateLimited);
    f.Ui.Finish(form.SubmitAsync());
    Check(f.Api.Creates == 1);
    f.Clock.Now = f.Clock.Now.AddMinutes(1);
    f.Api.Create = _ => Task.FromException(new ReportingException(ReportingIssue.AlreadyReported));
    f.Ui.Finish(form.SubmitAsync());
    Check(
        f.Api.Creates == 2 && form.State.Issue == ReportingIssue.AlreadyReported && !form.State.Sent
    );
    form.EditReason("different");
    f.Ui.Finish(form.SubmitAsync());
    Check(f.Api.Creates == 2);
}
static void ErrorRetry()
{
    using var f = new Fixture();
    using var form = f.Form();
    form.EditReason("reason");
    f.Api.Create = _ => Task.FromException(new HttpRequestException());
    f.Ui.Finish(form.SubmitAsync());
    Check(!form.State.Sent && form.State.Issue == ReportingIssue.Network);
    f.Api.Create = _ => Task.FromException(new ReportingException(ReportingIssue.Unauthorized));
    f.Ui.Finish(form.SubmitAsync());
    Check(!form.State.Sent && form.State.Issue == ReportingIssue.Unauthorized);
    f.Api.Create = _ => Task.CompletedTask;
    f.Ui.Finish(form.SubmitAsync());
    Check(form.State.Sent);
}
static void Bans()
{
    using var f = new Fixture();
    f.Api.Ban = _ => Task.FromResult(new ReportingBanStatus(true, "permanent"));
    f.Ui.Finish(f.Guard.RefreshAsync());
    Check(f.Guard.BlocksActions);
    using var form = f.Form();
    form.EditReason("reason");
    f.Ui.Finish(form.SubmitAsync());
    Check(f.Api.Creates == 0 && form.State.Issue == ReportingIssue.Banned);
    f.Api.Ban = _ => Task.FromException<ReportingBanStatus>(new HttpRequestException());
    var allowed = f.Guard.AllowActionAsync();
    f.Ui.Finish(allowed);
    Check(!allowed.Result && f.Guard.State.Status?.Banned == true);
}
static void UnbanRefresh()
{
    using var f = new Fixture();
    f.Api.Ban = _ =>
        Task.FromResult(new ReportingBanStatus(true, "temporary", "2000-01-01T00:00:00Z"));
    f.Ui.Finish(f.Guard.RefreshAsync());
    Check(f.Guard.BlocksActions, "expired-looking date must not override server");
    f.Api.Ban = _ => Task.FromResult(new ReportingBanStatus(false));
    f.Ui.Finish(f.Guard.RefreshAsync());
    Check(!f.Guard.BlocksActions);
}
static void GuardPolicy()
{
    using var f = new Fixture();
    f.Api.Ban = _ => Task.FromException<ReportingBanStatus>(new HttpRequestException());
    var allow = f.Guard.AllowActionAsync();
    f.Ui.Finish(allow);
    Check(allow.Result && f.Guard.State.Issue == ReportingIssue.Network);
    f.Api.Ban = _ =>
        Task.FromException<ReportingBanStatus>(new ReportingException(ReportingIssue.Unauthorized));
    allow = f.Guard.AllowActionAsync();
    f.Ui.Finish(allow);
    Check(!allow.Result);
    var wait = new TaskCompletionSource<ReportingBanStatus>();
    f.Api.Ban = _ => wait.Task;
    var refresh = f.Guard.RefreshAsync();
    allow = f.Guard.AllowActionAsync();
    Check(allow.Result == false);
    f.Guard.Cancel();
    wait.SetResult(new(false));
    f.Ui.Finish(refresh);
}
static void AccountSwitch()
{
    foreach (var next in new[] { 1, 2 })
    {
        using var f = new Fixture();
        using var form = f.Form();
        form.EditReason("reason");
        var wait = new TaskCompletionSource();
        CancellationToken captured = default;
        f.Api.Create = token =>
        {
            captured = token;
            return wait.Task;
        };
        var sending = form.SubmitAsync();
        f.Session.SetUser(null);
        f.Session.SetUser(next);
        Check(captured.IsCancellationRequested && !form.IsCurrent && form.State.Reason == "");
        wait.SetResult();
        f.Ui.Finish(sending);
        Check(!form.State.Sent);
        f.Ui.Finish(form.SubmitAsync());
        Check(f.Api.Creates == 1);
    }
}
static void LateCredentials()
{
    var ui = new Dispatcher();
    var wait = new TaskCompletionSource<string>();
    using var session = new ReportingSession(_ => wait.Task);
    session.SetUser(1);
    var api = new Api();
    using var guard = new BanGuardNotifier(ui, session, api);
    var request = guard.RefreshAsync();
    session.SetUser(2);
    wait.SetResult("old-credential");
    ui.Finish(request);
    Check(api.BanCalls == 0 && guard.State.Status is null);
}
static void Cancellation()
{
    foreach (var dispose in new[] { false, true })
    {
        using var f = new Fixture();
        using var form = f.Form();
        form.EditReason("reason");
        var wait = new TaskCompletionSource();
        f.Api.Create = _ => wait.Task;
        using var cancel = new CancellationTokenSource();
        var sending = form.SubmitAsync(cancel.Token);
        if (dispose)
            form.Dispose();
        else
            cancel.Cancel();
        wait.SetResult();
        f.Ui.Finish(sending);
        Check(!form.State.Sent && !form.Busy);
    }
}
static void AdminAuthorization()
{
    using var f = new Fixture();
    using var admin = f.Admin();
    f.Api.IsAdmin = false;
    f.Ui.Finish(admin.LoadAsync());
    Check(admin.State.Issue == ReportingIssue.Forbidden && f.Api.Lists == 0);
    admin.Ask(ModerationAction.Approve, 10, 7);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Writes == 0);
    f.Api.IsAdmin = true;
    f.Ui.Finish(admin.LoadAsync());
    admin.Ask(ModerationAction.Approve, 10, 7);
    f.Api.IsAdmin = false;
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Writes == 0 && !admin.State.IsAdmin && admin.State.Reports is null);
}
static void AdminDoubleSubmit()
{
    using var f = new Fixture();
    using var admin = f.Admin();
    f.Ui.Finish(admin.LoadAsync());
    admin.Ask(ModerationAction.Approve, 10, -1);
    Check(admin.State.Confirmation is null);
    admin.Ask(ModerationAction.Approve, 10, 7);
    Check(f.Api.Writes == 0);
    var wait = new TaskCompletionSource();
    f.Api.Write = _ => wait.Task;
    var first = admin.ConfirmAsync();
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Writes == 1);
    Check(
        f.Api.Decision?.Status == "APPROVED"
            && f.Api.Decision.Days == 7
            && f.Api.Decision.Start == f.Clock.Now
    );
    wait.SetResult();
    f.Ui.Finish(first);
    Check(admin.State.Applied && admin.State.Confirmation is null);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Writes == 1);
}
static void AdminRefreshFailure()
{
    using var f = new Fixture();
    using var admin = f.Admin();
    f.Ui.Finish(admin.LoadAsync());
    admin.Ask(ModerationAction.Dismiss, 10);
    f.Api.ListFailure = true;
    f.Ui.Finish(admin.ConfirmAsync());
    Check(
        admin.State.Applied && !admin.State.Loaded && admin.State.Issue == ReportingIssue.Network
    );
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Writes == 1);
    f.Api.ListFailure = false;
    f.Ui.Finish(admin.LoadAsync());
    Check(admin.State.Loaded && f.Api.Writes == 1);
}
static void AdminActions()
{
    using var f = new Fixture();
    using var admin = f.Admin();
    f.Ui.Finish(admin.LoadAsync());
    admin.Ask(ModerationAction.Dismiss, 10);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Decision == new UpdateUserReport("DISMISSED"));
    admin.Ask(ModerationAction.Revoke, 11);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.LastAction == "revoke:11");
    admin.Ask(ModerationAction.Unban, 11);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.LastAction == "unban:3" && admin.State.RevokedCount == 0);
    admin.Ask(ModerationAction.Approve, 10, 0);
    f.Ui.Finish(admin.ConfirmAsync());
    Check(f.Api.Decision?.Days == 0);
    admin.Ask(ModerationAction.Approve, 10, 7);
    f.Api.Write = _ => Task.FromException(new ReportingException(ReportingIssue.Forbidden));
    f.Ui.Finish(admin.ConfirmAsync());
    Check(
        admin.State.Issue == ReportingIssue.Forbidden
            && admin.State.Reports is null
            && !admin.State.Applied
    );
}
static void AdminLogout()
{
    using var f = new Fixture();
    using var admin = f.Admin();
    f.Ui.Finish(admin.LoadAsync());
    admin.Ask(ModerationAction.Dismiss, 10);
    var wait = new TaskCompletionSource();
    f.Api.Write = _ => wait.Task;
    var request = admin.ConfirmAsync();
    f.Session.SetUser(null);
    f.Session.SetUser(1);
    Check(!admin.State.IsAdmin && admin.State.Reports is null);
    wait.SetResult();
    f.Ui.Finish(request);
    Check(!admin.State.Applied && admin.State.Reports is null && f.Api.Lists == 1);
}
static void BanLogout()
{
    using var f = new Fixture();
    var wait = new TaskCompletionSource<ReportingBanStatus>();
    f.Api.Ban = _ => wait.Task;
    var request = f.Guard.RefreshAsync();
    f.Session.SetUser(2);
    wait.SetResult(new(true, "permanent"));
    f.Ui.Finish(request);
    Check(f.Guard.State.Status is null && !f.Guard.BlocksActions);
}
static void AdminMenu()
{
    using var f = new Fixture();
    using var menu = new AdminAccessNotifier(f.Ui, f.Session, f.Api, f.Clock);
    f.Api.IsAdmin = false;
    f.Ui.Finish(menu.RefreshAsync());
    Check(menu.State.Checked && !menu.State.IsAdmin && menu.State.Issue is null);
    f.Api.IsAdmin = true;
    f.Ui.Finish(menu.RefreshAsync());
    Check(menu.State.IsAdmin && f.Api.Lists == 0);
    f.Session.SetUser(null);
    Check(!menu.State.IsAdmin && !menu.State.Checked);
}
static void GuardedDelegate()
{
    using var f = new Fixture();
    var wait = new TaskCompletionSource();
    CancellationToken captured = default;
    var request = f.Guard.RunAllowedAsync(token =>
    {
        captured = token;
        return wait.Task;
    });
    f.Session.SetUser(null);
    Check(captured.IsCancellationRequested);
    wait.SetResult();
    try
    {
        f.Ui.Finish(request);
        throw new Exception("expected cancel");
    }
    catch (OperationCanceledException) { }
}

sealed class Fixture : IDisposable
{
    public readonly Dispatcher Ui = new();
    public readonly FakeClock Clock = new();
    public readonly Api Api = new();
    public readonly ReportingSession Session;
    public readonly BanGuardNotifier Guard;

    public Fixture()
    {
        Session = new(_ => Task.FromResult("fixture"));
        Session.SetUser(1);
        Guard = new(Ui, Session, Api, Clock);
    }

    public ReportUserNotifier Form(int target = 2) =>
        new(Ui, Session, Api, Guard, new(target, ReportOrigin.Chat, "chat-fixture"), Clock);

    public ModerationNotifier Admin() => new(Ui, Session, Api, Clock);

    public void Dispose()
    {
        Guard.Dispose();
        Session.Dispose();
    }
}

sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class Api : IReportingApi
{
    public bool IsAdmin = true,
        ListFailure;
    public int Creates,
        BanCalls,
        Lists,
        Writes;
    public UpdateUserReport? Decision;
    public string? LastAction;
    public Func<CancellationToken, Task> Create = _ => Task.CompletedTask,
        Write = _ => Task.CompletedTask;
    public Func<CancellationToken, Task<ReportingBanStatus>> Ban = _ =>
        Task.FromResult(new ReportingBanStatus(false));

    public Task CreateAsync(
        ReportingAccount account,
        int user,
        string reason,
        CancellationToken token
    )
    {
        Creates++;
        return Create(token);
    }

    public Task<ReportingBanStatus> BanAsync(ReportingAccount account, CancellationToken token)
    {
        BanCalls++;
        return Ban(token);
    }

    public Task<bool> AdminAsync(ReportingAccount account, CancellationToken token) =>
        Task.FromResult(IsAdmin);

    public Task<IReadOnlyList<ModerationReport>> ReportsAsync(
        ReportingAccount account,
        CancellationToken token
    )
    {
        Lists++;
        return ListFailure
            ? Task.FromException<IReadOnlyList<ModerationReport>>(new HttpRequestException())
            : Task.FromResult<IReadOnlyList<ModerationReport>>([
                new(10, 2, 3, "reason", "PENDING"),
                new(11, 2, 3, "reason", "APPROVED"),
            ]);
    }

    public Task UpdateAsync(
        ReportingAccount account,
        int id,
        UpdateUserReport decision,
        CancellationToken token
    )
    {
        Writes++;
        Decision = decision;
        return Write(token);
    }

    public Task RevokeAsync(ReportingAccount account, int id, CancellationToken token)
    {
        Writes++;
        LastAction = "revoke:" + id;
        return Write(token);
    }

    public async Task<int> UnbanAsync(ReportingAccount account, int id, CancellationToken token)
    {
        Writes++;
        LastAction = "unban:" + id;
        await Write(token);
        return 0;
    }
}

sealed class Handler : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Body = "{}";
    public string? RetryAfter;
    public readonly List<(string Path, HttpMethod Method, string? Auth, string? Body)> Requests =
    [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(
            (
                request.RequestUri!.AbsolutePath,
                request.Method,
                request.Headers.Authorization?.ToString(),
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
            )
        );
        var response = new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        if (RetryAfter is not null)
            response.Headers.TryAddWithoutValidation("Retry-After", RetryAfter);
        return response;
    }
}

sealed class Dispatcher : IUiDispatcher
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void Finish(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var action))
                action();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException("Test task did not complete");
        task.GetAwaiter().GetResult();
        while (queue.TryDequeue(out var action))
            action();
    }
}
