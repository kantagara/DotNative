using System.Text.Json;
using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

/// <summary>One instance per editor. sessionKey must include an authentication generation, not just email.</summary>
public sealed class GameEditorNotifier : Notifier<GameEditorState>, IDisposable
{
    private readonly IUiDispatcher dispatcher;
    private readonly IGameEditorApi api;
    private readonly IPlacesService places;
    private readonly Func<string?> sessionKey;
    private readonly Func<DateOnly> today;
    private readonly string? ownerSession;
    private readonly int organizer;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? placeRequest;
    private int placeRevision;
    public string? GameId { get; }
    public CancellationToken Token => lifetime.Token;
    public bool IsCurrent =>
        !lifetime.IsCancellationRequested
        && ownerSession is not null
        && sessionKey() == ownerSession;

    public GameEditorNotifier(
        IUiDispatcher dispatcher,
        IGameEditorApi api,
        IPlacesService places,
        Func<string?> sessionKey,
        int organizerId,
        string? gameId = null,
        Func<DateOnly>? today = null
    )
        : base(dispatcher, new(new()) { Loaded = gameId is null })
    {
        this.dispatcher = dispatcher;
        this.api = api;
        this.places = places;
        this.sessionKey = sessionKey;
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        ownerSession = sessionKey();
        organizer = organizerId;
        GameId = gameId;
    }

    public void Change(Func<GameEditorDraft, GameEditorDraft> change)
    {
        if (!IsCurrent || Busy || State.Saved || !State.Loaded)
            return;
        var next = change(State.Draft);
        if (GameId is not null)
            next = next with { SportId = State.Draft.SportId };
        SetState(State with { Draft = next, Error = null, Validation = [] });
    }

    public void ReportPickerError()
    {
        if (IsCurrent)
            SetState(State with { Error = GameEditorIssue.Picker });
    }

    public Task LoadAsync(CancellationToken token = default) =>
        RunExclusive(async () =>
        {
            if (GameId is null || State.Loaded || !IsCurrent)
                return;
            SetState(State with { Error = null });
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            try
            {
                var result = await api.LoadAsync(GameId, linked.Token);
                var draft = result.ToDraft();
                await dispatcher.InvokeAsync(() =>
                {
                    if (!IsCurrent || linked.IsCancellationRequested)
                        return;
                    if (result.Organizer != organizer)
                        SetState(State with { Error = GameEditorIssue.Unauthorized });
                    else
                        SetState(State with { Draft = draft, Loaded = true });
                });
            }
            catch (Exception e)
            {
                await Failure(e, linked.Token);
            }
        });

    public Task<GameEditorSaved?> SaveAsync(CancellationToken token = default) =>
        RunExclusive<GameEditorSaved>(async () =>
        {
            if (!IsCurrent || !State.Loaded || State.Saved || State.PlacesBusy)
                return null;
            var draft = State.Draft;
            var errors = draft.Validate(today());
            SetState(State with { Validation = errors, Error = null });
            if (errors.Count > 0)
                return null;
            if (organizer <= 0)
            {
                SetState(State with { Error = GameEditorIssue.Unauthorized });
                return null;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            try
            {
                var body = draft.ToUpdate();
                string id;
                if (GameId is null)
                    id = await api.CreateAsync(new(body, organizer, draft.SportId), linked.Token);
                else
                {
                    await api.UpdateAsync(GameId, body, linked.Token);
                    id = GameId;
                }
                GameEditorSaved? saved = null;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!IsCurrent || linked.IsCancellationRequested)
                        return;
                    SetState(State with { Saved = true });
                    saved = new(id, GameId is not null, draft);
                });
                return saved;
            }
            catch (Exception e)
            {
                await Failure(e, linked.Token);
                return null;
            }
        });

    private Task Failure(Exception e, CancellationToken token) =>
        dispatcher.InvokeAsync(() =>
        {
            if (IsCurrent && !token.IsCancellationRequested)
                SetState(State with { Error = Issue(e) });
        });

    private static GameEditorIssue Issue(Exception e) =>
        e switch
        {
            GameEditorException failure => failure.Issue,
            JsonException or FormatException => GameEditorIssue.InvalidResponse,
            _ => GameEditorIssue.Network,
        };

    public Task SearchAsync(string query)
    {
        if (!IsCurrent || Busy || State.Saved)
            return Task.CompletedTask;
        Change(d => d with { Address = query, Latitude = null, Longitude = null });
        return PlacesOperation(null);
    }

    public Task RetryPlacesAsync() => PlacesOperation(null);

    public Task SelectPlaceAsync(PlaceSuggestion place) => PlacesOperation(place);

    private async Task PlacesOperation(PlaceSuggestion? selection)
    {
        if (!IsCurrent || Busy || State.Saved)
            return;
        placeRequest?.Cancel();
        var revision = ++placeRevision;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Token);
        placeRequest = request;
        SetState(
            State with
            {
                PlacesBusy = true,
                PlacesError = null,
                Suggestions = selection is null ? [] : State.Suggestions,
            }
        );
        try
        {
            if (selection is null)
            {
                var query = State.Draft.Address;
                IReadOnlyList<PlaceSuggestion> results = string.IsNullOrWhiteSpace(query)
                    ? []
                    : await places.SearchAsync(query, request.Token);
                await dispatcher.InvokeAsync(() =>
                {
                    if (Current())
                        SetState(State with { Suggestions = results });
                });
            }
            else
            {
                var result = await places.GetDetailsAsync(selection, request.Token);
                await dispatcher.InvokeAsync(() =>
                {
                    if (Current())
                        SetState(
                            State with
                            {
                                Draft = State.Draft with
                                {
                                    Address = result.Address,
                                    Latitude = result.Latitude,
                                    Longitude = result.Longitude,
                                },
                                Suggestions = [],
                                Validation = [],
                            }
                        );
                });
            }
        }
        catch (Exception e)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (Current())
                    SetState(State with { PlacesError = Issue(e) });
            });
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (Current())
                    SetState(State with { PlacesBusy = false });
            });
            if (revision == placeRevision)
                placeRequest = null;
        }
        bool Current() =>
            IsCurrent && !request.IsCancellationRequested && revision == placeRevision;
    }

    public void Dispose()
    {
        // Cancel on Leave AND every session transition (including logout followed by same-account login).
        if (!lifetime.IsCancellationRequested)
            lifetime.Cancel();
        placeRequest?.Cancel();
    }
}
