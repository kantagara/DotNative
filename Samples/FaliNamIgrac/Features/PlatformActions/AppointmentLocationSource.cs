using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public sealed class AppointmentLocationSource(IApiClient api) : IAppointmentLocationSource
{
    public async Task<AppointmentLocation> LoadAsync(string id, CancellationToken token)
    {
        using var response = await api.GetAsync(
            "games/" + PlatformActionLinks.AppointmentId(id),
            token,
            authenticated: true
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync(
            PlatformActionsJson.Default.LocationEnvelope,
            token
        );
        token.ThrowIfCancellationRequested();
        var row = body?.Appointment ?? throw new InvalidDataException("Appointment is missing.");
        var location = new AppointmentLocation(row.Latitude, row.Longitude, row.Address);
        location.Validate();
        return location;
    }
}

internal sealed record LocationEnvelope(
    [property: JsonPropertyName("appointment")] LocationRow? Appointment
);

internal sealed record LocationRow
{
    [
        JsonPropertyName("location_lat"),
        JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)
    ]
    public required double Latitude { get; init; }

    [
        JsonPropertyName("location_lon"),
        JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)
    ]
    public required double Longitude { get; init; }

    [JsonPropertyName("appointment_address")]
    public required string Address { get; init; }
}

[JsonSerializable(typeof(LocationEnvelope))]
internal partial class PlatformActionsJson : JsonSerializerContext;
