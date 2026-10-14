using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record AppointmentEnvelope(
    AppointmentDto Appointment,
    AppointmentPlayerDto[] Players
);

internal sealed record AppointmentDto(
    [property: JsonPropertyName("appointment_id")] string Id,
    [property: JsonPropertyName("organizer_id")] int OrganizerId,
    [property: JsonPropertyName("owner_firstname")] string Firstname,
    [property: JsonPropertyName("owner_lastname")] string Lastname,
    [property: JsonPropertyName("appointment_date")] DateTime Date,
    [property: JsonPropertyName("start_time")] TimeSpan Start,
    [property: JsonPropertyName("end_time")] TimeSpan End,
    [property: JsonPropertyName("sport_id")] int SportId,
    [property: JsonPropertyName("admin_left_review")] bool AdminReviewed
);

internal sealed record AppointmentPlayerDto(
    [property: JsonPropertyName("user_id")] int Id,
    string Firstname,
    string Lastname,
    [property: JsonPropertyName("acceptance_status")] string Status
);

internal sealed record AppointmentReviewDto(
    [property: JsonPropertyName("appointment_id")] string Id,
    [property: JsonPropertyName("left_review")] bool? Reviewed
);

internal sealed record AppointmentStatusRequest(
    string AcceptanceStatus,
    bool BanFromFurtherAppointments
);

internal sealed record AppointmentApplyRequest(string UserId);

internal sealed record AppointmentRatingsRequest(
    int RatingUserId,
    string GameId,
    AppointmentRating[] Ratings
);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(AppointmentEnvelope))]
[JsonSerializable(typeof(AppointmentReviewDto[]))]
[JsonSerializable(typeof(AppointmentStatusRequest))]
[JsonSerializable(typeof(AppointmentApplyRequest))]
[JsonSerializable(typeof(AppointmentRatingsRequest))]
internal partial class AppointmentJson : JsonSerializerContext;
