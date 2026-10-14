using System.Text.RegularExpressions;
using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class OnboardingController(
    IUiDispatcher dispatcher,
    IProfileService profileService,
    IPlacesService placesService,
    IRegistrationService registration,
    IGamesApi api,
    IOnboardingDraftStore? drafts = null,
    AuthFlowText? text = null
) : Notifier<RegistrationIdentity?>(dispatcher, null), IOnboardingController
{
    private readonly State<string?> validationError = new(null);
    public string? ValidationError => validationError.Value;
    public Func<AuthFlowText> Texts { get; set; } = () => text ?? AuthFlowText.English;
    private AuthFlowText messages => Texts();

    private readonly State<string> firstName = new("");
    private readonly State<string> lastName = new("");
    private readonly State<string> nickname = new("");
    private readonly State<bool> nicknameChecking = new(false);
    private readonly State<bool> nicknameAvailable = new(false);
    private readonly State<bool> nicknameExists = new(false);
    private readonly State<string> phone = new("");
    private readonly State<string> bio = new("");
    private readonly State<string?> gender = new(null);
    private readonly State<DateTime?> birthDate = new(null);
    private readonly State<string> address = new("");
    private readonly State<IReadOnlyList<PlaceSuggestion>> placeSuggestions = new(
        Array.Empty<PlaceSuggestion>()
    );
    private readonly State<SelectedPlace?> selectedPlace = new(null);
    private readonly State<ImageSource?> photo = new(null);

    public string FirstName
    {
        get => firstName.Value;
        set => firstName.Value = value;
    }

    public string LastName
    {
        get => lastName.Value;
        set => lastName.Value = value;
    }

    public string Nickname
    {
        get => nickname.Value;
        set
        {
            if (nickname.Value == value)
                return;
            nickname.Value = value;
            nicknameRevision++;
            NicknameAvailable = false;
            NicknameExists = false;
            NicknameChecking = false;
        }
    }

    public bool NicknameChecking
    {
        get => nicknameChecking.Value;
        set => nicknameChecking.Value = value;
    }

    public bool NicknameAvailable
    {
        get => nicknameAvailable.Value;
        set => nicknameAvailable.Value = value;
    }

    public bool NicknameExists
    {
        get => nicknameExists.Value;
        set => nicknameExists.Value = value;
    }

    public string Phone
    {
        get => phone.Value;
        set => phone.Value = value;
    }

    public string Bio
    {
        get => bio.Value;
        set => bio.Value = value;
    }

    public string? Gender
    {
        get => gender.Value;
        set => gender.Value = value;
    }

    public DateTime? BirthDate
    {
        get => birthDate.Value;
        set => birthDate.Value = value;
    }

    public string Address
    {
        get => address.Value;
        set
        {
            if (address.Value == value)
                return;
            address.Value = value;
            locationRevision++;
            SelectedPlace = null;
        }
    }

    public IReadOnlyList<PlaceSuggestion> PlaceSuggestions
    {
        get => placeSuggestions.Value;
        set => placeSuggestions.Value = value;
    }

    public SelectedPlace? SelectedPlace
    {
        get => selectedPlace.Value;
        set => selectedPlace.Value = value;
    }

    public ImageSource? Photo
    {
        get => photo.Value;
        set => photo.Value = value;
    }

    private byte[]? photoBytes;
    private int nicknameRevision;
    private int locationRevision;
    private int identityRevision;
    private string? createdUserId;
    private bool creationStarted;
    private bool completed;

    public void Initialize(RegistrationIdentity identity)
    {
        if (State == identity)
            return;
        identityRevision++;
        createdUserId = null;
        creationStarted = false;
        completed = false;
        SetState(identity);
        FirstName = identity.Account.GivenName ?? "";
        LastName = identity.Account.FamilyName ?? "";
        Nickname = "";
        NicknameAvailable = false;
        NicknameExists = false;
        NicknameChecking = false;
        Gender = null;
        BirthDate = null;
        Phone = "";
        Bio = "";
        Address = "";
        SelectedPlace = null;
        Photo = null;
        photoBytes = null;
        PlaceSuggestions = Array.Empty<PlaceSuggestion>();
        nicknameRevision++;
        locationRevision++;
    }

    public async Task RestoreDraftAsync(CancellationToken token)
    {
        if (drafts is null || State is not { } identity)
            return;
        var revision = identityRevision;
        var draft = await drafts.ReadAsync(identity, token);
        token.ThrowIfCancellationRequested();
        if (draft is null || revision != identityRevision)
            return;
        FirstName = draft.FirstName;
        LastName = draft.LastName;
        Nickname = draft.Nickname;
        Phone = draft.Phone;
        Bio = draft.Bio;
        Gender = draft.Gender;
        BirthDate = draft.BirthDate;
        Address = draft.Address;
        SelectedPlace = draft.Place;
        if (draft.Photo is { } bytes)
            SetPhoto(bytes);
        createdUserId = draft.CreatedUserId;
        creationStarted = draft.CreationStarted;
        // A profile already created must not be rejected by its own nickname uniqueness check.
        if (createdUserId is not null || creationStarted)
            NicknameAvailable = true;
        else
            await CheckNicknameAsync(token);
    }

    public Task SaveDraftAsync(CancellationToken token)
    {
        if (completed || drafts is null || State is not { } identity)
            return Task.CompletedTask;
        return drafts.WriteAsync(
            new(
                identity.Email,
                identity.TemporaryUserId,
                FirstName,
                LastName,
                Nickname,
                Phone,
                Bio,
                Gender,
                BirthDate,
                Address,
                SelectedPlace,
                photoBytes,
                createdUserId,
                creationStarted
            ),
            token
        );
    }

    public void CancelPending()
    {
        identityRevision++;
        nicknameRevision++;
        locationRevision++;
    }

    public bool IsAgeValid =>
        BirthDate is not { } birth
        || birth
            <= new DateTime(DateTime.Today.Year - 18, DateTime.Today.Month, 1).AddDays(
                DateTime.Today.Day - 1
            );

    public bool CanSave =>
        !Busy
        && !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && NicknameAvailable
        && IsAgeValid
        && SelectedPlace is { Address.Length: > 0 };

    public NicknameIssue? NicknameProblem => ValidateNickname(Nickname);

    private static NicknameIssue? ValidateNickname(string name) =>
        name.Length == 0 ? NicknameIssue.Required
        : name.Length < 3 ? NicknameIssue.TooShort
        : !Regex.IsMatch(name, "^[a-zA-Zčćžšđа-яА-ЯёЁ0-9]+(?:\\.[a-zA-Zčćžšđа-яА-ЯёЁ0-9]+)*$")
            ? NicknameIssue.InvalidFormat
        : null;

    public void SetPhoto(byte[] bytes)
    {
        photoBytes = bytes;
        Photo = ImageSource.FromBytes(bytes);
    }

    public async Task CheckNicknameAsync(CancellationToken token)
    {
        validationError.Value = null;
        var revision = ++nicknameRevision;
        var name = Nickname.Trim();
        NicknameAvailable = false;
        NicknameExists = false;
        NicknameChecking = ValidateNickname(name) is null;
        if (!NicknameChecking)
            return;
        var exists = true;
        try
        {
            await Task.Delay(500, token);
            if (revision != nicknameRevision)
                return;
            exists = !await profileService.IsNicknameAvailableAsync(name, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (revision == nicknameRevision)
                NicknameChecking = false;
            return;
        }
        catch (Exception)
        {
            if (revision == nicknameRevision)
            {
                NicknameChecking = false;
                validationError.Value = messages.NicknameCheckFailed;
            }
            return;
        }
        if (token.IsCancellationRequested || revision != nicknameRevision)
            return;
        NicknameChecking = false;
        NicknameExists = exists;
        NicknameAvailable = !exists;
    }

    public async Task SearchPlacesAsync(CancellationToken token)
    {
        var revision = ++locationRevision;
        SelectedPlace = null;
        PlaceSuggestions = Array.Empty<PlaceSuggestion>();
        var text = Address;
        if (string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            await Task.Delay(400, token);
            if (revision != locationRevision)
                return;
            var places = await placesService.SearchAsync(text, token);
            if (!token.IsCancellationRequested && revision == locationRevision)
                PlaceSuggestions = places;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (revision == locationRevision)
                validationError.Value = messages.PlacesFailed;
        }
    }

    public async Task<SelectedPlace?> SelectPlaceAsync(
        PlaceSuggestion place,
        CancellationToken token
    )
    {
        Address = place.Description;
        var revision = ++locationRevision;
        PlaceSuggestions = Array.Empty<PlaceSuggestion>();
        try
        {
            var value = await placesService.GetDetailsAsync(place, token);
            if (!token.IsCancellationRequested && revision == locationRevision)
                SelectedPlace = value;
            return !token.IsCancellationRequested && revision == locationRevision ? value : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
    }

    public Task<PlayerProfile?> SaveAsync(CancellationToken token)
    {
        if (!CanSave || State is not { } identity || SelectedPlace is not { } place)
            return Task.FromResult<PlayerProfile?>(null);
        var submittedPhoto = photoBytes;
        var revision = identityRevision;
        return RunExclusive<PlayerProfile>(async () =>
        {
            // Current add-user assigns defaults from the backend sports table.
            // It does not persist the legacy Flutter sport-selection payload.
            var sports = Array.Empty<ProfileSport>();
            var body = new NewProfile(
                int.TryParse(identity.TemporaryUserId, out var id) && id > 0
                    ? id
                    : throw new InvalidDataException("Invalid temporary user ID."),
                identity.Email,
                Nickname.Trim(),
                FirstName,
                LastName,
                Bio,
                BirthDate?.ToString("yyyy-MM-dd'T'00:00:00.000"),
                Gender,
                Phone.Length == 0 ? null : Phone,
                sports,
                place.Latitude,
                place.Longitude,
                place.Address,
                place.Address,
                ""
            );
            void Check()
            {
                token.ThrowIfCancellationRequested();
                if (revision != identityRevision)
                    throw new OperationCanceledException("Account changed.");
            }
            Check();
            var userId = createdUserId;
            if (userId is null && creationStarted)
            {
                if (await registration.IsFullyRegisteredAsync(token))
                {
                    var existing = await api.ProfileAsync(token);
                    Check();
                    createdUserId = userId = existing.Id;
                }
                else
                    throw new OnboardingCreationUncertainException(messages.CreationUncertain);
            }
            if (userId is null)
            {
                creationStarted = true;
                await SaveDraftAsync(token);
                Check();
                try
                {
                    userId = await profileService.CreateAsync(body, token);
                }
                catch (HttpRequestException error)
                    when (error.StatusCode is { } status && (int)status is >= 400 and < 500)
                {
                    Check();
                    creationStarted = false;
                    await SaveDraftAsync(token);
                    throw;
                }
                Check();
                createdUserId = userId;
                await SaveDraftAsync(token);
            }
            if (submittedPhoto is { } photo)
            {
                await profileService.UploadPhotoAsync(userId, photo, token);
                Check();
            }
            var profile = await api.ProfileAsync(token);
            Check();
            if (profile.Id != userId)
                throw new InvalidDataException("Profile account mismatch.");
            if (drafts is not null)
                await drafts.DeleteAsync(identity, token);
            Check();
            await registration.ClearTemporaryUserAsync(token);
            Check();
            completed = true;
            return profile;
        });
    }
}

public sealed class OnboardingCreationUncertainException(string message)
    : InvalidOperationException(message);
