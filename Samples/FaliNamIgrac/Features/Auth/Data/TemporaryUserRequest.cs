using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record TemporaryUserRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("authType")] string AuthType
);
