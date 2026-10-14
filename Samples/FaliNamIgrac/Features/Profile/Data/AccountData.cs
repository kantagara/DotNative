using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public sealed record AccountData
{
    [JsonPropertyName("user_id"), JsonConverter(typeof(ScalarStringConverter))]
    public required string Id { get; init; }
    public string Username { get; init; } = "";
    public string Firstname { get; init; } = "";
    public string Lastname { get; init; } = "";
    public string Nickname { get; init; } = "";

    [JsonPropertyName("biography")]
    public string? Bio { get; init; }

    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; init; }
    public string? Gender { get; init; }

    [JsonPropertyName("phone_number")]
    public string? Phone { get; init; }

    [JsonPropertyName("location_lat")]
    public double Latitude { get; init; }

    [JsonPropertyName("location_lon")]
    public double Longitude { get; init; }
    public string? City { get; init; }
    public string? Address { get; init; }

    [JsonPropertyName("profile_img_url")]
    public string? Photo { get; init; }
    public ProfileSport[] Sports { get; init; } = [];

    [JsonPropertyName("games_played")]
    public int? GamesPlayed { get; init; }

    [JsonPropertyName("average_rating")]
    public double? Rating { get; init; }

    [JsonPropertyName("sport_ratings")]
    public PlayerSportRating[] SportRatings { get; init; } = [];
}

internal sealed record AccountResponse(AccountData UserData);

internal sealed record UpdateAccountRequest(
    int UserId,
    string Username,
    string Firstname,
    string Lastname,
    string Nickname,
    string? Bio,
    string? BirthDate,
    string? Gender,
    string? PhoneNumber,
    double LocationLat,
    double LocationLong,
    string? City,
    string? Address,
    string? ProfileImageUrl,
    ProfileSport[] Sports
);

public sealed record PlayerSportRating(int SportId, int NumberOfRatings, double AverageRating);
