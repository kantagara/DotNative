using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed record AccountState(
    AccountData? Saved = null,
    AccountData? Draft = null,
    string Message = "",
    string LocationQuery = "",
    IReadOnlyList<PlaceSuggestion>? Suggestions = null,
    bool LocationPending = false
);

public sealed class AccountNotifier(
    IUiDispatcher dispatcher,
    SessionNotifier session,
    IAccountService api,
    IPresentationState presentation
) : Notifier<AccountState>(dispatcher, new())
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private int revision;
    private CancellationTokenSource? active;

    public void Clear()
    {
        revision++;
        active?.Cancel();
        SetState(new());
    }

    public void Edit(Func<AccountData, AccountData> change)
    {
        if (!Busy && State.Draft is { } draft)
            SetState(State with { Draft = change(draft), Message = "" });
    }

    public void CancelEdit()
    {
        if (!Busy)
            SetState(
                State with
                {
                    Draft = State.Saved,
                    Message = "",
                    LocationPending = false,
                    LocationQuery = "",
                    Suggestions = null,
                }
            );
    }

    public void SetBirthDate(DateOnly value) =>
        Edit(data => data with { BirthDate = value.ToString("yyyy-MM-dd") });

    public void ToggleSport(string sport) =>
        Edit(data =>
            data with
            {
                Sports = data.Sports.Any(item => item.SportName == sport)
                    ? data.Sports.Where(item => item.SportName != sport).ToArray()
                    :
                    [
                        .. data.Sports,
                        State.Saved?.Sports.FirstOrDefault(item => item.SportName == sport)
                            ?? new ProfileSport(sport),
                    ],
            }
        );

    public void ToggleNotifications(string sport) =>
        Edit(data =>
            data with
            {
                Sports = data
                    .Sports.Select(item =>
                        item.SportName == sport
                            ? item with
                            {
                                NotificationType = item.NotificationType == "ON" ? "OFF" : "ON",
                            }
                            : item
                    )
                    .ToArray(),
            }
        );

    public void SetLocationQuery(string value)
    {
        // Location input is locked during requests; a changed query must be resolved before saving.
        if (Busy)
            return;
        SetState(
            State with
            {
                LocationQuery = value,
                Suggestions = null,
                LocationPending = true,
                Message = "",
            }
        );
    }

    public Task SearchPlacesAsync(IPlacesService places, CancellationToken token) =>
        Execute(
            async ct =>
            {
                var suggestions = string.IsNullOrWhiteSpace(State.LocationQuery)
                    ? []
                    : await places.SearchAsync(State.LocationQuery.Trim(), ct);
                ct.ThrowIfCancellationRequested();
                return State with { Suggestions = suggestions };
            },
            token
        );

    public Task SelectPlaceAsync(
        IPlacesService places,
        PlaceSuggestion suggestion,
        CancellationToken token
    ) =>
        Execute(
            async ct =>
            {
                var place = await places.GetDetailsAsync(suggestion, ct);
                ct.ThrowIfCancellationRequested();
                if (
                    !double.IsFinite(place.Latitude)
                    || !double.IsFinite(place.Longitude)
                    || place.Latitude is < -90 or > 90
                    || place.Longitude is < -180 or > 180
                    || string.IsNullOrWhiteSpace(place.Address)
                )
                    throw new InvalidDataException();
                return State with
                {
                    Draft = State.Draft! with
                    {
                        Address = place.Address,
                        City = place.Address,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                    },
                    LocationQuery = place.Address,
                    LocationPending = false,
                    Suggestions = null,
                };
            },
            token
        );

    public Task LoadAsync(CancellationToken token) =>
        Execute(
            async ct =>
            {
                var account = await api.LoadAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (
                    session.State is not SessionState.SignedIn signed
                    || signed.Profile.Id != account.Id
                )
                    throw new InvalidDataException("Profile identity mismatch.");
                return new(account, account);
            },
            token
        );

    public Task SaveAsync(CancellationToken token) =>
        Execute(
            async ct =>
            {
                var draft = State.Draft;
                if (draft is null)
                    return State;
                if (
                    State.LocationPending
                    || !AccountValidation.Valid(draft, DateOnly.FromDateTime(DateTime.Today))
                )
                    return State with { Message = presentation.Text.EditProfileMissingInfo };
                if (
                    session.State is not SessionState.SignedIn signed
                    || signed.Profile.Id != draft.Id
                )
                    throw new InvalidDataException("Profile identity mismatch.");
                var normalized = draft with
                {
                    Firstname = draft.Firstname.Trim(),
                    Lastname = draft.Lastname.Trim(),
                    Nickname = draft.Nickname.Trim(),
                };
                await api.SaveAsync(normalized, ct);
                ct.ThrowIfCancellationRequested();
                return new(normalized, normalized, presentation.Text.EditProfileSuccess);
            },
            token
        );

    public Task UploadPhotoAsync(
        Func<CancellationToken, Task<byte[]?>> pick,
        Func<string, byte[], CancellationToken, Task> upload,
        Func<string?, byte[], Task> remember,
        CancellationToken token
    ) =>
        Execute(
            async ct =>
            {
                if (State.Saved is not { } saved)
                    return State;
                var draft = State.Draft;
                var photo = await pick(ct);
                ct.ThrowIfCancellationRequested();
                if (photo is null)
                    return State;
                _ = PhotoFormat.Detect(photo);
                await upload(saved.Id, photo, ct);
                ct.ThrowIfCancellationRequested();
                var updated = await api.LoadAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (updated.Id != saved.Id || string.IsNullOrWhiteSpace(updated.Photo))
                    throw new InvalidDataException();
                await remember(updated.Photo, photo);
                ct.ThrowIfCancellationRequested();
                return State with
                {
                    Saved = saved with { Photo = updated.Photo },
                    Draft = draft is null ? null : draft with { Photo = updated.Photo },
                    Message = presentation.Text.EditProfileSuccess,
                };
            },
            token
        );

    private Task Execute(
        Func<CancellationToken, Task<AccountState>> operation,
        CancellationToken token
    ) =>
        RunExclusive(async () =>
        {
            if (session.State is not SessionState.SignedIn signed || token.IsCancellationRequested)
                return;
            var request = revision;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            active = linked;
            // Observing the transition also rejects A -> signed-out -> A, not just different user IDs.
            var subscription = session.Listen(() =>
            {
                if (
                    session.State is not SessionState.SignedIn current
                    || current.Profile.Id != signed.Profile.Id
                )
                {
                    revision++;
                    linked.Cancel();
                    SetState(new());
                }
            });
            SetState(State with { Message = "" });
            try
            {
                var next = await operation(linked.Token);
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested && request == revision)
                    {
                        SetState(next);
                        if (
                            next.Saved is { } saved
                            && AccountValidation.TryBirthDate(saved.BirthDate, out var birth)
                            && session.State is SessionState.SignedIn current
                        )
                        {
                            var today = DateOnly.FromDateTime(DateTime.Today);
                            var age = today.Year - birth.Year;
                            if (birth > today.AddYears(-age))
                                age--;
                            session.UpdateProfile(
                                current.Profile with
                                {
                                    Name = saved.Firstname,
                                    Age = age,
                                    Latitude = saved.Latitude,
                                    Longitude = saved.Longitude,
                                }
                            );
                        }
                    }
                });
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
            catch (Exception error)
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested && request == revision)
                        SetState(
                            State with
                            {
                                Message = FniErrors.UserMessage(presentation.Text, error),
                            }
                        );
                });
            }
            finally
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    subscription.Dispose();
                    if (ReferenceEquals(active, linked))
                        active = null;
                });
            }
        });
}
