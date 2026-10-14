using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed record CatalogSearchState(
    CatalogSearchQuery Query,
    CatalogFilterDraft Draft,
    IReadOnlyList<Game> Games,
    bool Editing = false,
    bool Loading = false,
    bool HasMore = false,
    bool InvalidDraft = false,
    Exception? Error = null
);

/// <summary>Call commands on the UI dispatcher. Clear on every session transition, including logout/login of the same user.</summary>
public sealed class CatalogSearchNotifier(
    IUiDispatcher dispatcher,
    ICatalogSearchApi api,
    Func<PlayerProfile?> profile
) : Notifier<CatalogSearchState>(dispatcher, new(new(), new(), [])), IDisposable
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private CancellationTokenSource? request;
    private PlayerProfile? requestProfile;
    private long revision;
    private Task active = Task.CompletedTask;
    private bool disposed;

    public void BeginEdit() =>
        SetState(
            State with
            {
                Editing = true,
                Draft = CatalogFilterDraft.From(State.Query.EffectiveFilters),
                InvalidDraft = false,
            }
        );

    public void Edit(CatalogFilterDraft draft) =>
        SetState(State with { Draft = draft, InvalidDraft = false });

    public void CancelEdit() =>
        SetState(
            State with
            {
                Editing = false,
                Draft = CatalogFilterDraft.From(State.Query.EffectiveFilters),
                InvalidDraft = false,
            }
        );

    // Reset is draft-only; closing without Apply preserves the applied query.
    public void ResetDraft() => Edit(new());

    public Task ApplyAsync(CancellationToken token = default)
    {
        if (!State.Draft.TryApply(out var filters))
        {
            SetState(State with { InvalidDraft = true });
            return Task.CompletedTask;
        }
        SetState(State with { Editing = false, InvalidDraft = false });
        return ChangeAsync(State.Query with { Filters = filters }, token);
    }

    public Task ChangeAsync(CatalogSearchQuery query, CancellationToken token = default) =>
        LoadAsync(query.FirstPage(), token);

    public Task RetryAsync(CancellationToken token = default) => LoadAsync(State.Query, token);

    public Task NextAsync(CancellationToken token = default) =>
        State.Loading || !State.HasMore
            ? Task.CompletedTask
            : LoadAsync(State.Query with { Page = State.Query.Page + 1 }, token);

    public Task PreviousAsync(CancellationToken token = default) =>
        State.Loading || State.Query.Page <= 1
            ? Task.CompletedTask
            : LoadAsync(State.Query with { Page = State.Query.Page - 1 }, token);

    public Task LoadAsync(CatalogSearchQuery query, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        query.Validate();
        var user = profile();
        if (user is null)
        {
            Clear();
            return Task.CompletedTask;
        }
        if (
            State.Loading
            && requestProfile == user
            && State.Query == query
            && request is { IsCancellationRequested: false }
        )
            return active;
        revision++;
        request?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        request = source;
        requestProfile = user;
        SetState(
            State with
            {
                Query = query,
                Games = [],
                Loading = true,
                Error = null,
                HasMore = false,
            }
        );
        return active = FetchAsync(user, query, revision, source);
    }

    private async Task FetchAsync(
        PlayerProfile user,
        CatalogSearchQuery query,
        long version,
        CancellationTokenSource source
    )
    {
        try
        {
            var rows = await api.SearchAsync(user, query, source.Token);
            await uiDispatcher.InvokeAsync(() =>
            {
                if (version != revision || profile() != user || source.IsCancellationRequested)
                    return;
                SetState(
                    State with
                    {
                        Games = rows,
                        Loading = false,
                        HasMore = rows.Count == query.Limit && query.Page < 10000,
                    }
                );
            });
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error)
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (version == revision && profile() == user && !source.IsCancellationRequested)
                    SetState(State with { Error = error, Loading = false });
            });
        }
        finally
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (version == revision)
                {
                    request = null;
                    requestProfile = null;
                    if (State.Loading)
                        SetState(State with { Loading = false });
                }
            });
            source.Dispose();
        }
    }

    public void Clear()
    {
        revision++;
        request?.Cancel();
        request = null;
        requestProfile = null;
        SetState(new(new(), new(), []));
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Clear();
        disposed = true;
    }
}
