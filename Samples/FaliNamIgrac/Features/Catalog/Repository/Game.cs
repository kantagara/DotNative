namespace FaliNamIgrac;

public sealed record PlayerProfile(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    int Age
);

public sealed record Game(
    string Id,
    string Address,
    DateTimeOffset Date,
    string Start,
    string End,
    int SportId,
    int Needed,
    int Total,
    int Price,
    string Currency,
    string Organizer,
    string? Info
)
{
    public int OrganizerId { get; init; }
    public string? OrganizerPhoto { get; init; }
    public string? OrganizerPhone { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public decimal? TotalPrice { get; init; }
    public string? Terrain { get; init; }
    public string? Venue { get; init; }
}

public sealed record GameParticipant(
    int Id,
    string Firstname,
    string Lastname,
    string Status,
    string? Photo = null,
    string? Phone = null
)
{
    public string Name => (Firstname + " " + Lastname).Trim();
}

public sealed record GameDetails(Game Game, IReadOnlyList<string> Players)
{
    public IReadOnlyList<GameParticipant> Participants { get; init; } = [];
}
