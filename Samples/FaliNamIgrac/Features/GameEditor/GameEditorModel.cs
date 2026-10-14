using System.Globalization;

namespace FaliNamIgrac;

public enum GameEditorIssue
{
    Sport,
    Location,
    Date,
    Time,
    Capacity,
    Price,
    Currency,
    Terrain,
    Venue,
    Limits,
    Unauthorized,
    Rejected,
    Server,
    Network,
    InvalidResponse,
    Session,
    Picker,
}

/// <summary>Raw strings preserve invalid input while the user corrects it. Price is the total venue price.</summary>
public sealed record GameEditorDraft
{
    public int SportId { get; init; }
    public string Address { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateOnly? Date { get; init; }
    public TimeOnly? Start { get; init; }
    public TimeOnly? End { get; init; }
    public string Needed { get; init; } = "";
    public string Total { get; init; } = "";
    public string Price { get; init; } = "";
    public string Currency { get; init; } = "RSD";
    public string Terrain { get; init; } = "";
    public string Venue { get; init; } = "indoor";
    public string? Description { get; init; }
    public int MinSkill { get; init; } = 1;
    public int MaxSkill { get; init; } = 5;
    public int MinAge { get; init; } = 16;
    public int MaxAge { get; init; } = 100;
    public static readonly string[] Terrains =
    [
        "",
        "grass",
        "concrete",
        "parquet",
        "clinker",
        "sand",
    ];
    public static readonly string[] Currencies = ["RSD", "EUR", "KM"];

    public IReadOnlyList<GameEditorIssue> Validate(DateOnly today)
    {
        var issues = new List<GameEditorIssue>();
        if (!FniSports.All.Any(s => s.Id == SportId))
            issues.Add(GameEditorIssue.Sport);
        if (
            string.IsNullOrWhiteSpace(Address)
            || Latitude is not { } lat
            || Longitude is not { } lng
            || !double.IsFinite(lat)
            || !double.IsFinite(lng)
            || lat is < -90 or > 90
            || lng is < -180 or > 180
        )
            issues.Add(GameEditorIssue.Location);
        if (Date is null || Date < today)
            issues.Add(GameEditorIssue.Date);
        // Backend stores both times on the same appointment date; midnight rollover is not supported.
        if (Start is null || End is null || End <= Start)
            issues.Add(GameEditorIssue.Time);
        if (
            !int.TryParse(Needed, out var needed)
            || !int.TryParse(Total, out var total)
            || needed <= 0
            || total <= 0
            || needed > total
        )
            issues.Add(GameEditorIssue.Capacity);
        if (!TryPrice(out _))
            issues.Add(GameEditorIssue.Price);
        if (!Currencies.Contains(Currency))
            issues.Add(GameEditorIssue.Currency);
        if (!Terrains.Contains(Terrain))
            issues.Add(GameEditorIssue.Terrain);
        if (Venue is not ("indoor" or "outdoor"))
            issues.Add(GameEditorIssue.Venue);
        if (MinSkill < 1 || MaxSkill > 5 || MinSkill > MaxSkill || MinAge < 0 || MaxAge < MinAge)
            issues.Add(GameEditorIssue.Limits);
        return issues;
    }

    public bool TryPrice(out decimal price) =>
        decimal.TryParse(
            Price.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out price
        )
        && price >= 0;

    public GameEditorUpdate ToUpdate()
    {
        if (!TryPrice(out var price))
            throw new InvalidOperationException("Invalid price");
        return new()
        {
            PlayersNeeded = int.Parse(Needed, CultureInfo.InvariantCulture),
            PlayersTotal = int.Parse(Total, CultureInfo.InvariantCulture),
            LocationLat = Latitude!.Value,
            LocationLng = Longitude!.Value,
            AppointmentAddress = Address,
            AppointmentDate = Date!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            StartTime = Start!.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
            EndTime = End!.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
            TerrainType = Terrain,
            VenueType = Venue,
            Price = price,
            Currency = Currency,
            AdditionalInfo = Description,
            MinSkillLevel = MinSkill,
            MaxSkillLevel = MaxSkill,
            MinAge = MinAge,
            MaxAge = MaxAge,
        };
    }
}

public sealed record GameEditorSaved(string Id, bool IsEdit, GameEditorDraft Draft);

public sealed record GameEditorState(GameEditorDraft Draft)
{
    public bool Loaded { get; init; }
    public bool PlacesBusy { get; init; }
    public IReadOnlyList<PlaceSuggestion> Suggestions { get; init; } = [];
    public IReadOnlyList<GameEditorIssue> Validation { get; init; } = [];
    public GameEditorIssue? Error { get; init; }
    public GameEditorIssue? PlacesError { get; init; }
    public bool Saved { get; init; }
}
