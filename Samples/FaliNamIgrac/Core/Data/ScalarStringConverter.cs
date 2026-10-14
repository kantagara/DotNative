using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

// IDs can arrive as JSON numbers or strings; normalize only at the wire boundary.
internal sealed class ScalarStringConverter : JsonConverter<string>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type type,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(
                reader.HasValueSequence
                    ? reader.ValueSequence.ToArray()
                    : reader.ValueSpan.ToArray()
            ),
            _ => throw new JsonException("Expected a string or numeric identifier."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);
}
