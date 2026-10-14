using System.Globalization;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public record GameEditorUpdate
{
    public int PlayersNeeded { get; init; }
    public int PlayersTotal { get; init; }
    public double LocationLat { get; init; }
    public double LocationLng { get; init; }
    public string AppointmentAddress { get; init; } = "";
    public DateTime AppointmentDate { get; init; }
    public string StartTime { get; init; } = "";
    public string EndTime { get; init; } = "";
    public string TerrainType { get; init; } = "";
    public string VenueType { get; init; } = "";
    public decimal Price { get; init; }
    public string Currency { get; init; } = "";
    public int MinSkillLevel { get; init; }
    public int MaxSkillLevel { get; init; }
    public int MinAge { get; init; }
    public int MaxAge { get; init; }
    public string? AdditionalInfo { get; init; }
}

public sealed record GameEditorCreate : GameEditorUpdate
{
    public GameEditorCreate(GameEditorUpdate fields, int organizerId, int sportId)
        : base(fields) => (OrganizerId, SportId) = (organizerId, sportId);

    public int OrganizerId { get; init; }
    public int SportId { get; init; }
}

public sealed record GameEditorCreated(string AppointmentId);

public sealed record GameEditorDetails(GameEditorAppointment Appointment);

// Required properties deliberately reject incomplete edit data instead of silently overwriting it with defaults.
public sealed record GameEditorAppointment
{
    [JsonPropertyName("appointment_id")]
    public required string Id { get; init; }

    [JsonPropertyName("sport_id")]
    public required int Sport { get; init; }

    [JsonPropertyName("organizer_id")]
    public required int Organizer { get; init; }

    [JsonPropertyName("appointment_address")]
    public required string Address { get; init; }

    [JsonPropertyName("appointment_date")]
    public required DateTime Date { get; init; }

    [JsonPropertyName("start_time")]
    public required string Start { get; init; }

    [JsonPropertyName("end_time")]
    public required string End { get; init; }

    [JsonPropertyName("location_lat")]
    public required double Lat { get; init; }

    [JsonPropertyName("location_lon")]
    public required double Lng { get; init; }

    [JsonPropertyName("players_needed")]
    public required int Needed { get; init; }

    [JsonPropertyName("players_total")]
    public required int Total { get; init; }

    [JsonPropertyName("price")]
    public required decimal Price { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("terrain_type")]
    public required string Terrain { get; init; }

    [JsonPropertyName("venue_type")]
    public required string Venue { get; init; }

    [JsonPropertyName("additional_info")]
    public required string? Info { get; init; }

    [JsonPropertyName("min_skill_level")]
    public required int MinSkill { get; init; }

    [JsonPropertyName("max_skill_level")]
    public required int MaxSkill { get; init; }

    [JsonPropertyName("min_age")]
    public required int MinAge { get; init; }

    [JsonPropertyName("max_age")]
    public required int MaxAge { get; init; }

    public GameEditorDraft ToDraft() =>
        new()
        {
            SportId = Sport,
            Address = Address,
            Date = DateOnly.FromDateTime(Date),
            Start = TimeOnly.Parse(Start, CultureInfo.InvariantCulture),
            End = TimeOnly.Parse(End, CultureInfo.InvariantCulture),
            Latitude = Lat,
            Longitude = Lng,
            Needed = Needed.ToString(CultureInfo.InvariantCulture),
            Total = Total.ToString(CultureInfo.InvariantCulture),
            Price = Price.ToString(CultureInfo.InvariantCulture),
            Currency = Currency,
            Terrain = Terrain,
            Venue = Venue,
            Description = Info,
            MinSkill = MinSkill,
            MaxSkill = MaxSkill,
            MinAge = MinAge,
            MaxAge = MaxAge,
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
)]
[JsonSerializable(typeof(GameEditorCreate))]
[JsonSerializable(typeof(GameEditorUpdate))]
[JsonSerializable(typeof(GameEditorCreated))]
[JsonSerializable(typeof(GameEditorDetails))]
public partial class GameEditorJson : JsonSerializerContext;
