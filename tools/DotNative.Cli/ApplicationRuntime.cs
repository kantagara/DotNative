using System.Text.Json;

internal enum ApplicationRuntime
{
    NativeAot,
    Managed,
}

internal static partial class Preview
{
    private static async Task<ApplicationRuntime> ResolveRuntime(
        string project,
        string dotnet,
        string? option
    )
    {
        if (option is not null)
            return ParseRuntime(option);

        // Evaluate imports and conditions rather than inspecting raw project XML.
        using var evaluation = JsonDocument.Parse(
            await Capture(
                dotnet,
                ["msbuild", project, "-nologo", "-getProperty:DotNativeRuntime,PublishAot"]
            )
        );
        var properties = evaluation.RootElement.GetProperty("Properties");
        var configured = properties.GetProperty("DotNativeRuntime").GetString();
        if (!string.IsNullOrWhiteSpace(configured))
            return ParseRuntime(configured);
        var publishAot = properties.GetProperty("PublishAot").GetString();
        if (string.IsNullOrWhiteSpace(publishAot))
            return ApplicationRuntime.NativeAot;
        if (!bool.TryParse(publishAot, out var enabled))
            throw new ArgumentException("PublishAot must be true or false.");
        return enabled ? ApplicationRuntime.NativeAot : ApplicationRuntime.Managed;
    }

    private static ApplicationRuntime ParseRuntime(string value) =>
        value.ToLowerInvariant() switch
        {
            "nativeaot" => ApplicationRuntime.NativeAot,
            "managed" => ApplicationRuntime.Managed,
            _ => throw new ArgumentException("Runtime must be nativeaot or managed."),
        };
}
