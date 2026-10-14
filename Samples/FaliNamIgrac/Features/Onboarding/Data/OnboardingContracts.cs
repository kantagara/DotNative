using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record NicknameQuery(string Nickname);

internal sealed record PlacesAutocompleteQuery(
    string Key,
    string Input,
    string Components = "country:rs|country:me|country:ba|country:hr"
);

internal sealed record PlacesDetailsQuery(
    string Key,
    [property: JsonPropertyName("place_id")] string PlaceId
);

internal sealed record CreatedProfileResponse(
    [property: JsonPropertyName("userId"), JsonConverter(typeof(ScalarStringConverter))] string? Id,
    [property: JsonPropertyName("user_id"), JsonConverter(typeof(ScalarStringConverter))]
        string? AlternateId
)
{
    public string UserId =>
        Id ?? AlternateId ?? throw new InvalidDataException("Missing created user ID.");
}

internal sealed record PlacesAutocompleteResponse(PlacePrediction[] Predictions);

internal sealed record PlacePrediction(
    string Description,
    [property: JsonPropertyName("place_id")] string Id
);

internal sealed record PlacesDetailsResponse(PlaceDetails Result);

internal sealed record PlaceDetails(PlaceGeometry Geometry);

internal sealed record PlaceGeometry(PlaceCoordinates Location);

internal sealed record PlaceCoordinates(double Lat, double Lng);
