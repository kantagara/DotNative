using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(DotNative.Development.PreviewRevision))]

namespace DotNative.Development;

internal static class PreviewRevision
{
    private static long current;
    internal static long Current => Interlocked.Read(ref current);
    internal static string BuildId =>
        Environment.GetEnvironmentVariable("DOTNATIVE_PREVIEW_BUILD_ID") ?? "standalone";

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        var revision = Interlocked.Increment(ref current);
        Console.WriteLine(
            $"[preview] Managed update {revision} applied to build {BuildId}; waiting for native confirmation."
        );
    }
}
