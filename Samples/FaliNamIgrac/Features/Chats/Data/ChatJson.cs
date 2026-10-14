using System.Text.Json.Serialization;

namespace FaliNamIgrac;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
)]
[JsonSerializable(typeof(ChatGraphRequest))]
[JsonSerializable(typeof(ChatGraphResponse))]
[JsonSerializable(typeof(ChatSocketAuth))]
[JsonSerializable(typeof(ChatSocketFrame))]
internal partial class ChatJson : JsonSerializerContext;
