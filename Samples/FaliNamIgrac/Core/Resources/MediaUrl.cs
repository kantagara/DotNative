namespace FaliNamIgrac;

public static class MediaUrl
{
    private const string Bucket = "https://fali-nam-igrac-profile-images.s3.amazonaws.com/";

    // REST profiles return filenames; GraphQL group images return full S3 object keys.
    public static string? Profile(string? value) => Resolve(value, true);

    public static string? Group(string? value) => Resolve(value, false);

    private static string? Resolve(string? value, bool profile)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
        var path = value.TrimStart('/');
        if (path.Split('/').Any(part => part is ".." or "."))
            return null;
        if (profile && !path.StartsWith("public/", StringComparison.Ordinal))
            path = "public/" + path;
        return Bucket + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}
