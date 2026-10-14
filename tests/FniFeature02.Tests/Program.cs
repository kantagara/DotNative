using System.Net;
using DotNative;
using FaliNamIgrac;

var context = new TestContext();
SynchronizationContext.SetSynchronizationContext(context);
context.Finish(Tests.Run());

static class Tests
{
    const string Id = "b3b293d4-15df-4287-bfd6-bfb662af339b";
    static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    static AppointmentSnapshot Game =>
        new(
            Id,
            1,
            "Same Name",
            new(2026, 10, 14),
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(11),
            1,
            false,
            false,
            [
                new(2, "Same Name", "PENDING"),
                new(3, "Same Name", "ACCEPTED"),
                new(4, "Other", "REJECTED"),
            ]
        );

    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }

    public static async Task Run()
    {
        Check(
            Game.Role(1) == AppointmentRole.Organizer
                && Game.Role(2) == AppointmentRole.Pending
                && Game.Role(3) == AppointmentRole.Accepted
                && Game.Role(5) == AppointmentRole.Visitor,
            "Identity by id"
        );
        Check(
            Game.Allows(AppointmentAction.Accept, 1, Now, 2)
                && !Game.Allows(AppointmentAction.Accept, 2, Now, 2),
            "Organizer only moderation"
        );
        Check(
            Game.Allows(AppointmentAction.Remove, 1, Now, 3)
                && !Game.Allows(AppointmentAction.Remove, 1, Now, 2),
            "Remove accepted only"
        );
        Check(
            Game.Allows(AppointmentAction.Withdraw, 2, Now)
                && Game.Allows(AppointmentAction.Withdraw, 3, Now)
                && !Game.Allows(AppointmentAction.Withdraw, 1, Now),
            "Withdraw membership"
        );
        Check(
            Game.Allows(AppointmentAction.Apply, 5, Now)
                && !Game.Allows(AppointmentAction.Apply, 4, Now),
            "Visitor apply only"
        );
        var past = Game with { Date = new(2026, 10, 12) };
        Check(
            !Game.Allows(AppointmentAction.Rate, 3, Now)
                && past.Allows(AppointmentAction.Rate, 3, Now)
                && !past.Allows(AppointmentAction.Rate, 2, Now),
            "Rating time and membership"
        );
        Check(
            !(past with { PlayerReviewed = null }).Allows(AppointmentAction.Rate, 3, Now)
                && !(past with { AdminReviewed = true }).Allows(AppointmentAction.Rate, 1, Now),
            "Review flags"
        );
        Check(
            past.RatingTargets(3).Select(p => p.Id).SequenceEqual([1]),
            "Only accepted others plus organizer"
        );
        var boundary = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        Check(
            Game.Ended(boundary) && !Game.Ended(boundary.AddSeconds(-1)),
            "Belgrade end boundary"
        );
        Check(new CatalogState() is { Sport: 0, MySport: 0 }, "All sports retained");
        await Notifier();
        await Transport();
        Console.WriteLine(
            "PASS feature 02: roles, time, confirmations, payloads, HTTP errors, cancellation, stale responses, duplicate clicks, refresh and ratings"
        );
    }

    static async Task Notifier()
    {
        int? user = 1;
        var fake = new Service { Snapshot = Game };
        using var n = new MyAppointmentsNotifier(
            new Dispatcher(),
            fake,
            () => user,
            new Clock(Now)
        );
        await n.OpenAsync(Id, default);
        var mutations = 0;
        n.Mutated += _ => mutations++;
        await n.ExecuteAsync(new(AppointmentAction.Delete), default);
        Check(fake.Writes == 0, "Delete requires confirmation");
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Write = () => blocked.Task;
        var accept = n.ExecuteAsync(new(AppointmentAction.Accept, 2), default);
        await n.ExecuteAsync(new(AppointmentAction.Accept, 2), default);
        Check(fake.Writes == 1, "Duplicate click blocked");
        blocked.SetResult();
        await accept;
        Check(mutations == 1 && fake.Reads == 2 && !n.State.Working, "Refresh and mutation event");
        fake.Write = () => Task.FromException(new HttpRequestException());
        await n.ExecuteAsync(new(AppointmentAction.Accept, 2), default);
        Check(n.State.Problem == AppointmentProblem.Mutation && mutations == 1, "No false success");
        fake.Write = () => Task.CompletedTask;
        await n.ReloadAsync(default);
        fake.FailRead = true;
        await n.ExecuteAsync(new(AppointmentAction.Accept, 2), default);
        Check(
            n.State.Problem == AppointmentProblem.Refresh && !n.Allows(AppointmentAction.Accept, 2),
            "Refresh error locks actions"
        );
        var writes = fake.Writes;
        fake.FailRead = false;
        await n.ReloadAsync(default);
        Check(
            fake.Writes == writes && n.State.Problem == AppointmentProblem.None,
            "Retry refresh never repeats write"
        );
        var delayed = new TaskCompletionSource<AppointmentSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        fake.Read = () => delayed.Task;
        var old = n.ReloadAsync(default);
        n.Clear();
        user = 5;
        delayed.SetResult(Game);
        await old;
        Check(n.State.Details is null && !n.State.Working, "Old account response discarded");
        fake.Read = null;
        user = 1;
        await n.OpenAsync(Id, default);
        var oldWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Write = () => oldWrite.Task;
        var oldMutation = n.ExecuteAsync(new(AppointmentAction.Accept, 2), default);
        var beforeLogoutEvents = mutations;
        n.Clear();
        user = 3;
        await n.OpenAsync(Id, default);
        oldWrite.SetResult();
        await oldMutation;
        Check(
            mutations == beforeLogoutEvents
                && n.UserId == 3
                && n.State.Details?.Id == Id
                && n.State.Details.Role(3) == AppointmentRole.Accepted,
            "Old mutation cannot publish or overwrite new account"
        );
        fake.Write = () => Task.CompletedTask;

        fake.Read = null;
        user = 3;
        fake.Snapshot = Game with { Date = new(2026, 10, 12) };
        await n.OpenAsync(Id, default);
        Check(!n.Request(new(AppointmentAction.Rate, Ratings: [new(3, 5)])), "No self rating");
        Check(!n.Request(new(AppointmentAction.Rate, Ratings: [new(1, 0)])), "Range validation");
        Check(
            !n.Request(new(AppointmentAction.Rate, Ratings: [new(1, 5), new(1, 4)])),
            "Duplicate targets"
        );
        Check(n.Request(new(AppointmentAction.Rate, Ratings: [new(1, 4)])), "Valid rating request");
        await n.ConfirmAsync(default);
        Check(
            n.State.RatingsSubmitted && !n.Allows(AppointmentAction.Rate),
            "Submitted rating locked"
        );
        user = 2;
        fake.Snapshot = Game;
        await n.OpenAsync(Id, default);
        Check(n.Request(new(AppointmentAction.Withdraw)), "Withdraw confirmation");
        using var cancel = new CancellationTokenSource();
        fake.Write = () =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        };
        await n.ConfirmAsync(cancel.Token);
        Check(
            n.State.Problem == AppointmentProblem.Refresh && !n.State.Working,
            "Canceled mutation reconciles"
        );
    }

    static async Task Transport()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/") };
        using var session = new Session();
        var api = new MyAppointmentsService(http, session);
        handler.Body = """
            {"appointment":{"appointment_id":"b3b293d4-15df-4287-bfd6-bfb662af339b","organizer_id":1,"owner_firstname":"Same","owner_lastname":"Name","appointment_date":"2026-10-14T00:00:00","start_time":"10:00:00","end_time":"11:00:00","sport_id":1,"admin_left_review":false},"players":[{"user_id":2,"firstname":"Same","lastname":"Name","acceptance_status":"PENDING"}]}
            """;
        var game = await api.LoadAsync(Id, 1, default);
        Check(
            game.Players[0].Id == 2 && game.Players[0].Status == "PENDING",
            "DTO identity preserved"
        );
        await api.ExecuteAsync("a/b", 1, new(AppointmentAction.Ban, 2), default);
        Check(
            handler.Method == HttpMethod.Put
                && handler.Path == "/game-data/a%2Fb/players/2"
                && handler.RequestBody
                    == "{\"acceptanceStatus\":\"REJECTED\",\"banFromFurtherAppointments\":true}",
            "Ban payload"
        );
        await api.ExecuteAsync(Id, 2, new(AppointmentAction.Withdraw), default);
        Check(
            handler.Method == HttpMethod.Delete
                && handler.Path == $"/game-data/{Id}/applications/2",
            "Withdrawal route"
        );
        await api.ExecuteAsync(Id, 1, new(AppointmentAction.Delete), default);
        Check(
            handler.Method == HttpMethod.Delete && handler.Path == $"/game-data/delete/{Id}",
            "Delete route"
        );
        await api.ExecuteAsync(Id, 3, new(AppointmentAction.Rate, Ratings: [new(1, 5)]), default);
        Check(
            handler.Path == "/reviews-data/ratings"
                && handler.RequestBody.Contains("\"ratingUserId\":3")
                && handler.RequestBody.Contains("\"ratedUserId\":1"),
            "Ratings payload"
        );
        Check(handler.Authorization == "Bearer test", "Authentication");
        handler.Body = handler.Body.Replace("PENDING", "ACCEPTED");
        handler.ReviewPages = true;
        var reviewed = await api.LoadAsync(Id, 2, default);
        Check(
            reviewed.PlayerReviewed == true && handler.ReviewReads == 2,
            "Review flag retrieved across pages"
        );
        handler.ReviewPages = false;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await api.ExecuteAsync(Id, 2, new(AppointmentAction.Withdraw), canceled.Token);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { }

        foreach (
            var status in new[]
            {
                HttpStatusCode.Forbidden,
                HttpStatusCode.NotFound,
                HttpStatusCode.InternalServerError,
            }
        )
        {
            handler.Status = status;
            try
            {
                await api.ExecuteAsync(Id, 1, new(AppointmentAction.Accept, 2), default);
                throw new Exception("HTTP failure swallowed");
            }
            catch (HttpRequestException e)
            {
                Check(e.StatusCode == status, "Status preserved");
            }
        }
    }

    sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class Dispatcher : IUiDispatcher
    {
        private readonly int owner = Environment.CurrentManagedThreadId;
        private readonly SynchronizationContext context = SynchronizationContext.Current!;

        public bool CheckAccess() => Environment.CurrentManagedThreadId == owner;

        public void Post(Action action) => context.Post(_ => action(), null);
    }

    sealed class Service : IMyAppointmentsService
    {
        public AppointmentSnapshot Snapshot = Game;
        public int Reads;
        public int Writes;
        public bool FailRead;
        public Func<Task>? Write;
        public Func<Task<AppointmentSnapshot>>? Read;

        public Task<AppointmentSnapshot> LoadAsync(string id, int user, CancellationToken token)
        {
            Reads++;
            return Read?.Invoke()
                ?? (
                    FailRead
                        ? Task.FromException<AppointmentSnapshot>(new Exception())
                        : Task.FromResult(Snapshot)
                );
        }

        public Task ExecuteAsync(
            string id,
            int user,
            AppointmentCommand command,
            CancellationToken token
        )
        {
            Writes++;
            return Write?.Invoke() ?? Task.CompletedTask;
        }
    }

    sealed class Handler : HttpMessageHandler
    {
        public bool ReviewPages;
        public int ReviewReads;
        public string Body = "{}",
            Path = "",
            RequestBody = "",
            Authorization = "";
        public HttpMethod? Method;
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization!.ToString();
            RequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(token);
            var body = Body;
            if (ReviewPages && Path.Contains("user-games"))
            {
                ReviewReads++;
                body =
                    ReviewReads == 1
                        ? "["
                            + string.Join(
                                ",",
                                Enumerable.Repeat(
                                    "{\"appointment_id\":\"other\",\"left_review\":false}",
                                    200
                                )
                            )
                            + "]"
                        : "[{\"appointment_id\":\"" + Id + "\",\"left_review\":true}]";
                if (
                    !request.RequestUri.Query.Contains("sportId=1")
                    || !request.RequestUri.Query.Contains("appointmentsPassed=true")
                )
                    throw new Exception("Review query contract");
            }
            return new(Status) { Content = new StringContent(body) };
        }
    }

    sealed class Session : ISessionService
    {
        public bool IsSignedIn => true;
        public string Email => "test@example.invalid";

        public void Dispose() { }

        public Task<string> AccessTokenAsync(CancellationToken t) => Task.FromResult("test");

        public Task<string> IdTokenAsync(CancellationToken t) => throw new NotSupportedException();

        public Task SignInAsync(string e, string p, CancellationToken t) =>
            throw new NotSupportedException();

        public Task<AccountInfo> AcceptWebSessionAsync(
            string a,
            string r,
            int e,
            string i,
            CancellationToken t
        ) => throw new NotSupportedException();

        public Task SignUpAsync(string e, string p, CancellationToken t) =>
            throw new NotSupportedException();

        public Task ConfirmSignUpAsync(string e, string c, CancellationToken t) =>
            throw new NotSupportedException();

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken t) =>
            throw new NotSupportedException();

        public Task RequestPasswordResetAsync(string e, CancellationToken t) =>
            throw new NotSupportedException();

        public Task ConfirmPasswordResetAsync(string e, string c, string p, CancellationToken t) =>
            throw new NotSupportedException();

        public Task<bool> RestoreAsync(CancellationToken t) => throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken t) => throw new NotSupportedException();
    }
}

sealed class TestContext : SynchronizationContext
{
    readonly System.Collections.Concurrent.ConcurrentQueue<(
        SendOrPostCallback Callback,
        object? State
    )> queue = new();

    public override void Post(SendOrPostCallback callback, object? state) =>
        queue.Enqueue((callback, state));

    public void Finish(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var item))
                item.Callback(item.State);
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException("Test UI pump timed out");
        task.GetAwaiter().GetResult();
    }
}
