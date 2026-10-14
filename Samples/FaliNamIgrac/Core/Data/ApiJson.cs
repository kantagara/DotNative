using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FaliNamIgrac;

internal static class ApiJson
{
    public static JsonTypeInfo<T> Type<T>() =>
        BackendJson.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException(
            $"Missing generated JSON contract for {typeof(T).Name}."
        );

    public static async Task<T> ReadAsync<T>(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        return await JsonSerializer.DeserializeAsync(stream, Type<T>(), token)
            ?? throw new InvalidDataException($"Missing {typeof(T).Name} response.");
    }

    public static IReadOnlyDictionary<string, string> Query<T>(T query) =>
        JsonSerializer
            .SerializeToElement(query, Type<T>())
            .EnumerateObject()
            .Where(property => property.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(
                property => property.Name,
                property =>
                    property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString()!,
                        JsonValueKind.Number
                        or JsonValueKind.True
                        or JsonValueKind.False
                        or JsonValueKind.Object
                        or JsonValueKind.Array => property.Value.GetRawText(),
                        _ => throw new InvalidOperationException("Unsupported query parameter."),
                    }
            );

    public static string QueryString<T>(T query) =>
        string.Join(
            "&",
            Query(query)
                .Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)
                )
        );
}
