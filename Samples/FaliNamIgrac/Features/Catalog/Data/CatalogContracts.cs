using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record GamesQuery(
    string UserId,
    double UserLat,
    double UserLong,
    int Age,
    int Page,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SportId = null,
    int Distance = 50000
)
{
    public GameFilters Filters { get; init; } = new();
}

internal sealed record GameFilters(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MinPrice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxPrice = null
);

internal sealed record MyGamesQuery(int SportId, int Page, bool AppointmentsPassed, int Limit = 30);

internal sealed record ProfileQuery(string Username);

internal sealed record GameResponse
{
    [JsonPropertyName("appointment_id"), JsonConverter(typeof(ScalarStringConverter))]
    public required string Id { get; init; }

    [JsonPropertyName("appointment_address")]
    public required string Address { get; init; }

    [JsonPropertyName("appointment_date")]
    public required DateTimeOffset Date { get; init; }

    [JsonPropertyName("start_time")]
    public required string Start { get; init; }

    [JsonPropertyName("end_time")]
    public required string End { get; init; }

    [JsonPropertyName("sport_id")]
    public required double SportId { get; init; }

    [JsonPropertyName("players_needed")]
    public required double Needed { get; init; }

    [JsonPropertyName("players_total")]
    public required double Total { get; init; }
    public double? Price { get; init; }

    [JsonPropertyName("price_per_user")]
    public double? PricePerUser { get; init; }
    public string? Currency { get; init; }

    [JsonPropertyName("owner_firstname")]
    public required string OwnerFirstName { get; init; }

    [JsonPropertyName("owner_lastname")]
    public required string OwnerLastName { get; init; }

    [JsonPropertyName("additional_info")]
    public string? Info { get; init; }

    [JsonPropertyName("terrain_type")]
    public string? Terrain { get; init; }

    [JsonPropertyName("venue_type")]
    public string? Venue { get; init; }

    [JsonPropertyName("organizer_id")]
    public int OrganizerId { get; init; }

    [JsonPropertyName("owner_profile_img_url")]
    public string? OrganizerPhoto { get; init; }

    [JsonPropertyName("owner_phone_number")]
    public string? OrganizerPhone { get; init; }

    [JsonPropertyName("location_lat")]
    public double Latitude { get; init; }

    [JsonPropertyName("location_lon")]
    public double Longitude { get; init; }

    public Game ToModel() =>
        new(
            Id,
            Address,
            Date,
            Start,
            End,
            (int)SportId,
            (int)Needed,
            (int)Total,
            PricePerUser is { } perUser
                ? (int)perUser
                : (int)(Price ?? throw new InvalidDataException("Missing game price."))
                    / Math.Max(1, (int)Total),
            Currency ?? "RSD",
            OwnerFirstName + " " + OwnerLastName,
            Info
        )
        {
            OrganizerId = OrganizerId,
            OrganizerPhoto = OrganizerPhoto,
            OrganizerPhone = OrganizerPhone,
            Latitude = Latitude,
            Longitude = Longitude,
            TotalPrice = Price is { } totalPrice ? (decimal)totalPrice : null,
            Terrain = Terrain,
            Venue = Venue,
        };
}

internal sealed record GameDetailsResponse(GameResponse Appointment, PlayerResponse[] Players);

internal sealed record PlayerResponse(
    string Firstname,
    string Lastname,
    [property: JsonPropertyName("user_id")] int Id = 0,
    [property: JsonPropertyName("acceptance_status")] string Status = "PENDING",
    [property: JsonPropertyName("profile_img_url")] string? Photo = null,
    [property: JsonPropertyName("phone_number")] string? Phone = null
);

internal sealed record ProfileResponse(UserProfileResponse UserData);

internal sealed record UserProfileResponse(
    [property: JsonPropertyName("user_id"), JsonConverter(typeof(ScalarStringConverter))] string Id,
    string? Firstname,
    [property: JsonPropertyName("location_lat")] double Latitude,
    [property: JsonPropertyName("location_lon")] double Longitude,
    [property: JsonPropertyName("birth_date")] string? BirthDate
);
