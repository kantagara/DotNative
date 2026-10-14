namespace FaliNamIgrac;

// Public client identifiers. Values live in .env (see .env.example), not in source.
internal static class BackendConfiguration
{
    public static string ChatUrl => Required("FNI_CHAT_URL");
    public static string ApiUrl => Required("FNI_API_URL");
    public static string PlacesKey => Required("FNI_PLACES_KEY");
    public static string Region => Required("FNI_REGION");
    public static string IdentityPoolId => Required("FNI_IDENTITY_POOL_ID");
    public static string PoolId => Required("FNI_POOL_ID");
    public static string ClientId => Required("FNI_CLIENT_ID");
    public static string FirebaseProjectId => Required("FNI_FIREBASE_PROJECT_ID");
    public static string FirebaseSenderId => Required("FNI_FIREBASE_SENDER_ID");
    public static string FirebaseStorageBucket => Required("FNI_FIREBASE_STORAGE_BUCKET");
    public static string IosBundleId => Required("FNI_IOS_BUNDLE_ID");
    public static string IosFirebaseApiKey => Required("FNI_IOS_FIREBASE_API_KEY");
    public static string IosFirebaseAppId => Required("FNI_IOS_FIREBASE_APP_ID");
    public static string AndroidFirebaseApiKey => Required("FNI_ANDROID_FIREBASE_API_KEY");
    public static string AndroidFirebaseAppId => Required("FNI_ANDROID_FIREBASE_APP_ID");

    internal static string Required(string name)
    {
        var value = PublicConfiguration.Get(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing {name}. Provide approved public configuration for this build."
            );
        return value;
    }
}
