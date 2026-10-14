using System.Text.Json;

namespace FaliNamIgrac;

// Only public mobile client configuration belongs in this resource. Never embed .env.
internal static class PublicConfiguration
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> bundled = new(Read);

    public static string? Get(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? (bundled.Value.TryGetValue(name, out var value) ? value : null);

    private static IReadOnlyDictionary<string, string> Read()
    {
        using var stream = typeof(PublicConfiguration).Assembly.GetManifestResourceStream(
            "fni/public-configuration.json"
        );
        if (stream is null)
            return new Dictionary<string, string>();
        using var document = JsonDocument.Parse(stream);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (
                !Allowed.Contains(entry.Name)
                || entry.Value.ValueKind != JsonValueKind.String
                || !values.TryAdd(entry.Name, entry.Value.GetString()!)
            )
                throw new InvalidDataException("Invalid public application configuration.");
        }
        return values;
    }

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "FNI_API_URL",
        "FNI_CHAT_URL",
        "FNI_PLACES_KEY",
        "FNI_REGION",
        "FNI_POOL_ID",
        "FNI_IDENTITY_POOL_ID",
        "FNI_CLIENT_ID",
        "FNI_FIREBASE_PROJECT_ID",
        "FNI_FIREBASE_SENDER_ID",
        "FNI_FIREBASE_STORAGE_BUCKET",
        "FNI_IOS_BUNDLE_ID",
        "FNI_IOS_FIREBASE_API_KEY",
        "FNI_IOS_FIREBASE_APP_ID",
        "FNI_ANDROID_FIREBASE_API_KEY",
        "FNI_ANDROID_FIREBASE_APP_ID",
        "FNI_PROFILE_IDENTITY_POOL_ID",
        "FNI_PROFILE_STORAGE_REGION",
        "FNI_PROFILE_IMAGE_BUCKET",
    };
}
