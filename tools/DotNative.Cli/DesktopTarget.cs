internal static class DesktopTarget
{
    public static (string Target, string Managed, int Port) Resolve(
        string platform,
        string architecture
    )
    {
        var cpu = architecture switch
        {
            "x64" => "x86_64",
            "arm64" => "aarch64",
            _ => throw new NotSupportedException(
                $"Desktop preview does not support {architecture}."
            ),
        };
        return platform switch
        {
            "windows" => ($"{cpu}-pc-windows-msvc", "WINDOWS", 4974),
            "linux" => ($"{cpu}-unknown-linux-gnu", "LINUX", 4975),
            _ => throw new ArgumentException($"Unsupported desktop platform: {platform}"),
        };
    }
}
