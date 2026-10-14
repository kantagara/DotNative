namespace FaliNamIgrac;

// Platform launchers supply the application sandbox; the storage plugin is app-independent.
internal static class FniStoragePaths
{
    public static string DataDirectory
    {
        get
        {
            var native = Environment.GetEnvironmentVariable("FNI_DATA_DIRECTORY");
            if (!string.IsNullOrEmpty(native) && Path.IsPathFullyQualified(native))
                return native;
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Application storage unavailable.");
            return Path.Combine(root, "FaliNamIgrac");
        }
    }
}
