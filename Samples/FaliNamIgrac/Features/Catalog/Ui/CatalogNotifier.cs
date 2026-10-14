using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class CatalogNotifier(
    IUiDispatcher dispatcher,
    SessionNotifier session,
    IGamesApi api,
    IPresentationState presentation,
    CatalogSearchApi? search = null
) : Notifier<CatalogState>(dispatcher, new())
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private int revision;
    private (int Sport, int Page, bool Passed, CancellationToken Token)? pendingMyLoad;
    private CancellationToken? pendingSessionLoad;
    private (string Id, CancellationToken Token, bool CanApply)? pendingDetails;

    public void Clear()
    {
        revision++;
        pendingSessionLoad = null;
        pendingMyLoad = null;
        pendingDetails = null;
        SetState(new());
    }

    public void SynchronizeSession(CancellationToken token)
    {
        Clear();
    }

    public Task LoadAsync(int sport, int page, CancellationToken token) =>
        Execute(
            async profile =>
                State with
                {
                    Games = await api.ListAsync(profile, sport, page, token),
                    Sport = sport,
                    Page = page,
                    Message = "",
                },
            token
        );

    public Task LoadMyGamesAsync(int sport, int page, bool passed, CancellationToken token)
    {
        if (Busy)
        {
            revision++;
            SetState(
                State with
                {
                    MyGames = null,
                    MySport = sport,
                    MyPage = page,
                    MyPassed = passed,
                }
            );
            pendingMyLoad = (sport, page, passed, token);
            return Task.CompletedTask;
        }
        return Execute(
            async profile =>
            {
                // Publish the requested selection first, so failures never show another filter's rows.
                var view = State with
                {
                    MyGames =
                        State.MySport == sport && State.MyPage == page && State.MyPassed == passed
                            ? State.MyGames
                            : null,
                    MySport = sport,
                    MyPage = page,
                    MyPassed = passed,
                    Message = "",
                };
                SetState(view);
                var games = search is null
                    ? await api.MyGamesAsync(profile, sport, page, passed, token)
                    : await search.MyGamesAsync(profile, sport, page, passed, token);
                return view with { MyGames = games, Message = "" };
            },
            token
        );
    }

    public Task OpenDetailsAsync(string id, CancellationToken token, bool canApply = true)
    {
        if (Busy)
        {
            pendingDetails = (id, token, canApply);
            return Task.CompletedTask;
        }
        return Execute(
            async _ =>
                State with
                {
                    Details = await api.DetailsAsync(id, token),
                    Applied = false,
                    CanApply = canApply,
                    Message = "",
                },
            token
        );
    }

    public void ShowResolvedDetails(GameDetails details, bool canApply = true)
    {
        revision++;
        pendingDetails = null;
        SetState(
            State with
            {
                Details = details,
                Applied = false,
                CanApply = canApply,
                Message = "",
            }
        );
    }

    public void ReportError(string message) => SetState(State with { Message = message });

    public void BackToList()
    {
        if (!Busy)
            SetState(State with { Details = null, Message = "" });
    }

    public Task ApplyAsync(CancellationToken token) =>
        Execute(
            async profile =>
            {
                var view = State;
                if (view.Details is not { } details || view.Applied || !view.CanApply)
                    return view;
                await api.ApplyAsync(details.Game.Id, profile.Id, token);
                try
                {
                    return view with
                    {
                        Details = await api.DetailsAsync(details.Game.Id, token),
                        Applied = true,
                        Message = presentation.Text.ApplicationSuccess,
                    };
                }
                catch (Exception error)
                    when (error is HttpRequestException or InvalidOperationException)
                {
                    return view with
                    {
                        Applied = true,
                        Message = presentation.Text.ApplicationDetailsStale,
                    };
                }
            },
            token
        );

    private async Task Execute(
        Func<PlayerProfile, Task<CatalogState>> operation,
        CancellationToken token
    )
    {
        await RunExclusive(async () =>
        {
            if (session.State is not SessionState.SignedIn signedIn)
            {
                Clear();
                return;
            }
            var requestRevision = revision;
            try
            {
                var next = await operation(signedIn.Profile);
                token.ThrowIfCancellationRequested();
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (requestRevision == revision && session.State == signedIn)
                        SetState(next);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (requestRevision == revision && session.State == signedIn)
                        SetState(
                            State with
                            {
                                Message =
                                    error is AllSportsCapacityException
                                        ? presentation.Text.FilterAllSportsCapacity
                                        : FniErrors.UserMessage(presentation.Text, error),
                            }
                        );
                });
            }
        });
        await uiDispatcher.InvokeAsync(() =>
        {
            if (!Busy && pendingDetails is { } details)
            {
                pendingDetails = null;
                if (
                    !details.Token.IsCancellationRequested
                    && session.State is SessionState.SignedIn
                )
                {
                    _ = OpenDetailsAsync(details.Id, details.Token, details.CanApply);
                    return;
                }
            }
            if (!Busy && pendingSessionLoad is { } pending)
            {
                pendingSessionLoad = null;
                if (!pending.IsCancellationRequested && session.State is SessionState.SignedIn)
                    _ = LoadAsync(0, 1, pending);
            }
            if (!Busy && pendingMyLoad is { } mine)
            {
                pendingMyLoad = null;
                if (!mine.Token.IsCancellationRequested && session.State is SessionState.SignedIn)
                    _ = LoadMyGamesAsync(mine.Sport, mine.Page, mine.Passed, mine.Token);
            }
        });
    }
}
