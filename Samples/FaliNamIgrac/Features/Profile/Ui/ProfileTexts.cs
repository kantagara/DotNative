namespace FaliNamIgrac;

/// <summary>Bind these values to agent 04 localization keys whenever the locale changes.</summary>
public sealed record ProfileTexts(
    string Notifications,
    string SearchLocation,
    string NoLocations,
    string DeleteAccount,
    string DeleteConfirmation,
    string DeleteFailed,
    string DeletePartial,
    string DeleteUnavailable,
    string SkillLevel
);
