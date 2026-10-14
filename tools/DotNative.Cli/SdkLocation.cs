using System.Reflection;

internal static class SdkLocation
{
    private static bool IsSdk(string path) =>
        File.Exists(Path.Combine(path, "tools/DotNative.DevHost/DotNative.DevHost.csproj"))
        && File.Exists(Path.Combine(path, "native/core/Cargo.toml"));

    internal static string Resolve(string workingDirectory, string toolDirectory)
    {
        foreach (var start in new[] { workingDirectory, toolDirectory })
            for (
                var directory = new DirectoryInfo(start);
                directory is not null;
                directory = directory.Parent
            )
                if (IsSdk(directory.FullName))
                    return directory.FullName;

        var bundled = Path.Combine(toolDirectory, "sdk");
        if (!IsSdk(bundled))
            throw new InvalidOperationException(
                "DotNative SDK payload is missing. Reinstall DotNative.Cli, or run the source CLI inside the DotNative repository."
            );

        // Builds write intermediates. Keep the installed tool's SDK payload immutable.
        var version =
            typeof(SdkLocation)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? throw new InvalidOperationException("CLI SDK version is missing.");
        var key = string.Concat(
            version.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_')
        );
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotNative",
            "sdk",
            key
        );
        if (IsSdk(cache) && File.Exists(Path.Combine(cache, ".complete")))
            return cache;
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var staging = cache + "." + Guid.NewGuid().ToString("N");
        try
        {
            foreach (
                var file in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories)
            )
            {
                var destination = Path.Combine(staging, Path.GetRelativePath(bundled, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            File.WriteAllText(Path.Combine(staging, ".complete"), version);
            try
            {
                Directory.Move(staging, cache);
            }
            catch (IOException) when (IsSdk(cache) && File.Exists(Path.Combine(cache, ".complete")))
            {
                // Another invocation finished installing the same SDK first.
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        return cache;
    }
}
