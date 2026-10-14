using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record GameApplicationRequest([property: JsonPropertyName("userId")] string UserId);
