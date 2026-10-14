using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public enum AppointmentProblem
{
    None,
    Load,
    Mutation,
    Refresh,
    InvalidAction,
}

public sealed record MyAppointmentsState(
    AppointmentSnapshot? Details = null,
    bool Working = false,
    AppointmentProblem Problem = AppointmentProblem.None,
    AppointmentCommand? Confirmation = null,
    bool Deleted = false,
    bool RatingsSubmitted = false
);

public sealed class MyAppointmentsNotifier(
    IUiDispatcher dispatcher,
    IMyAppointmentsService service,
    Func<int?> currentUser,
    TimeProvider? clock = null
) : Notifier<MyAppointmentsState>(dispatcher, new()), IDisposable
{
    private readonly IUiDispatcher ui = dispatcher;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private CancellationTokenSource scope = new();
    private long revision;
    private string? gameId;
    private int userId;
    private int gate;
    public event Action<AppointmentMutation>? Mutated;
    public DateTimeOffset Now => time.GetUtcNow();
    public int UserId => userId;
    public string? GameId => gameId;

    // Call on every session transition (including logout/login with the same account).
    public void Clear()
    {
        revision++;
        scope.Cancel();
        scope.Dispose();
        scope = new();
        gameId = null;
        userId = 0;
        gate = 0;
        SetState(new());
    }

    public Task OpenAsync(string id, CancellationToken token)
    {
        Clear();
        gameId = id;
        userId = currentUser() ?? 0;
        return ReloadAsync(token);
    }

    public bool Allows(AppointmentAction action, int? player = null) =>
        !State.Working
        && State.Problem == AppointmentProblem.None
        && currentUser() == userId
        && State.Details?.Allows(action, userId, Now, player) == true
        && !(action == AppointmentAction.Rate && State.RatingsSubmitted);

    public bool Request(AppointmentCommand command)
    {
        if (!Valid(command))
            return false;
        // Clone caller-owned arrays so confirmed payload cannot change underneath the user.
        SetState(
            State with
            {
                Confirmation = command with { Ratings = command.Ratings?.ToArray() },
                Problem = AppointmentProblem.None,
            }
        );
        return true;
    }

    public void CancelConfirmation()
    {
        if (!State.Working)
            SetState(State with { Confirmation = null });
    }

    private bool Valid(AppointmentCommand command)
    {
        if (!Allows(command.Action, command.PlayerId))
            return false;
        if (command.Action != AppointmentAction.Rate)
            return true;
        var ratings = command.Ratings;
        var targets = State.Details!.RatingTargets(userId).Select(p => p.Id).ToHashSet();
        return ratings is { Length: > 0 and <= 100 }
            && Guid.TryParse(gameId, out var id)
            && id != Guid.Empty
            && ratings.All(r => targets.Contains(r.RatedUserId) && r.Rating is >= 1 and <= 5)
            && ratings.Select(r => r.RatedUserId).Distinct().Count() == ratings.Length;
    }

    public Task ConfirmAsync(CancellationToken token) =>
        State.Confirmation is { } command
            ? ExecuteAsync(command, token, confirmed: true)
            : Task.CompletedTask;

    public Task ExecuteAsync(
        AppointmentCommand command,
        CancellationToken token,
        bool confirmed = false
    )
    {
        if (
            !Valid(command)
            || command.NeedsConfirmation && (!confirmed || State.Confirmation != command)
        )
            return Task.CompletedTask;
        return RunAsync(command, token);
    }

    public Task ReloadAsync(CancellationToken token) => RunAsync(null, token);

    private async Task RunAsync(AppointmentCommand? command, CancellationToken token)
    {
        if (
            gameId is null
            || userId <= 0
            || currentUser() != userId
            || Interlocked.CompareExchange(ref gate, 1, 0) != 0
        )
            return;
        var version = revision;
        var id = gameId;
        var actor = userId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, scope.Token);
        bool Current() => version == revision && currentUser() == actor;
        bool changed = false;
        async Task Publish(Action action) =>
            await ui.InvokeAsync(() =>
            {
                if (Current())
                    action();
            });
        await Publish(() =>
            SetState(State with { Working = true, Problem = AppointmentProblem.None })
        );
        try
        {
            if (command is not null)
            {
                await service.ExecuteAsync(id, actor, command, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                changed = true;
                await Publish(() =>
                {
                    SetState(
                        State with
                        {
                            Confirmation = null,
                            RatingsSubmitted =
                                State.RatingsSubmitted || command.Action == AppointmentAction.Rate,
                        }
                    );
                    Mutated?.Invoke(new(id, actor, command.Action));
                });
                if (command.Action == AppointmentAction.Delete)
                {
                    await Publish(() => SetState(State with { Details = null, Deleted = true }));
                    return;
                }
            }
            var details = await service.LoadAsync(id, actor, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            await Publish(() =>
                SetState(State with { Details = details, Problem = AppointmentProblem.None })
            );
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // A sent mutation may have reached the server. Require reconciliation before further actions.
            if (command is not null)
                await Publish(() =>
                    SetState(
                        State with
                        {
                            Confirmation = null,
                            Problem = AppointmentProblem.Refresh,
                        }
                    )
                );
        }
        catch (Exception)
        {
            await Publish(() =>
                SetState(
                    State with
                    {
                        Problem =
                            changed ? AppointmentProblem.Refresh
                            : command is null ? AppointmentProblem.Load
                            : AppointmentProblem.Mutation,
                    }
                )
            );
        }
        finally
        {
            if (version == revision)
                Interlocked.Exchange(ref gate, 0);
            await Publish(() => SetState(State with { Working = false }));
        }
    }

    public void Dispose()
    {
        Clear();
        scope.Dispose();
    }
}
