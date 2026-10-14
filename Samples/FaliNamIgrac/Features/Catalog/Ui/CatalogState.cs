namespace FaliNamIgrac;

public sealed record CatalogState(
    IReadOnlyList<Game>? Games = null,
    GameDetails? Details = null,
    int Sport = 0,
    int Page = 1,
    bool Applied = false,
    string Message = "",
    IReadOnlyList<Game>? MyGames = null,
    int MySport = 0,
    int MyPage = 1,
    bool MyPassed = false,
    bool CanApply = true
);
