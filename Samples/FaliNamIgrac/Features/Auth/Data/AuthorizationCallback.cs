namespace FaliNamIgrac;

internal sealed record AuthorizationCallback(
    string? State,
    string? Code,
    string? Error,
    string? ErrorDescription
)
{
    public static AuthorizationCallback FromUri(Uri uri)
    {
        var query = uri
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1].Replace('+', ' ')) : ""
            );
        return new(
            query.GetValueOrDefault("state"),
            query.GetValueOrDefault("code"),
            query.GetValueOrDefault("error"),
            query.GetValueOrDefault("error_description")
        );
    }
}
