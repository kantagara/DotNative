using DotNative;

namespace FaliNamIgrac;

public enum NicknameIssue
{
    Required,
    TooShort,
    InvalidFormat,
}

public interface IOnboardingController
{
    string FirstName { get; set; }
    string LastName { get; set; }
    string Nickname { get; set; }
    bool NicknameChecking { get; set; }
    bool NicknameAvailable { get; set; }
    bool NicknameExists { get; set; }
    string Phone { get; set; }
    string Bio { get; set; }
    string? Gender { get; set; }
    DateTime? BirthDate { get; set; }
    string Address { get; set; }
    IReadOnlyList<PlaceSuggestion> PlaceSuggestions { get; set; }
    SelectedPlace? SelectedPlace { get; set; }
    bool Busy { get; }
    string? ValidationError => null;
    ImageSource? Photo { get; set; }
    bool IsAgeValid { get; }
    bool CanSave { get; }
    NicknameIssue? NicknameProblem { get; }
    void Initialize(RegistrationIdentity identity);
    Task RestoreDraftAsync(CancellationToken token) => Task.CompletedTask;
    Task SaveDraftAsync(CancellationToken token) => Task.CompletedTask;
    void CancelPending() { }
    void SetPhoto(byte[] bytes);
    Task CheckNicknameAsync(CancellationToken token);
    Task SearchPlacesAsync(CancellationToken token);
    Task<SelectedPlace?> SelectPlaceAsync(PlaceSuggestion place, CancellationToken token);
    Task<PlayerProfile?> SaveAsync(CancellationToken token);
}
