using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record EmailQuery(string Email);

internal sealed record TemporaryUserResponse(
    [property: JsonPropertyName("temp_user_id"), JsonConverter(typeof(ScalarStringConverter))]
        string? Id,
    [property: JsonPropertyName("tempUserId"), JsonConverter(typeof(ScalarStringConverter))]
        string? AlternateId
)
{
    public string? UserId => string.IsNullOrEmpty(Id ?? AlternateId) ? null : Id ?? AlternateId;
}

internal sealed record CognitoIdentity(string? ProviderName, string? ProviderType, string? UserId);
