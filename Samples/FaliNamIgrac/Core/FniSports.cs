namespace FaliNamIgrac;

public readonly record struct Sport(int Id, string ApiName, string LabelKey);

// One catalog. Declaration order is the profile payload order.
// LabelKey matches the ARB member resolved for the active locale.
public static class FniSports
{
    public static readonly IReadOnlyList<Sport> All =
    [
        new(1, "football", "football"),
        new(2, "basketball", "basketball"),
        new(3, "tennis", "tennis"),
        new(4, "table_tennis", "tableTennis"),
        new(5, "badminton", "badminton"),
        new(7, "padel", "padel"),
        new(8, "handball", "handball"),
        new(6, "volleyball", "volleyball"),
        new(9, "running", "running"),
        new(10, "cycling", "cycling"),
        new(11, "hiking", "hiking"),
        new(12, "swimming", "swimming"),
        new(13, "e_games", "eGames"),
        new(14, "board_games", "boardGames"),
    ];

    public static string Name(int id, AppLocalizations text)
    {
        foreach (var sport in All)
        {
            if (sport.Id == id)
                return Label(text, sport.LabelKey);
        }

        return "Sport";
    }

    private static string Label(AppLocalizations text, string key) =>
        key switch
        {
            "football" => text.Football,
            "basketball" => text.Basketball,
            "tennis" => text.Tennis,
            "tableTennis" => text.TableTennis,
            "badminton" => text.Badminton,
            "padel" => text.Padel,
            "handball" => text.Handball,
            "volleyball" => text.Volleyball,
            "running" => text.Running,
            "cycling" => text.Cycling,
            "hiking" => text.Hiking,
            "swimming" => text.Swimming,
            "eGames" => text.EGames,
            "boardGames" => text.BoardGames,
            _ => throw new KeyNotFoundException(key),
        };

    public static ProfileSport[] DefaultProfile() =>
        All.Select(sport => new ProfileSport(sport.ApiName)).ToArray();
}
