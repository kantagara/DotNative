internal static partial class Preview
{
    private static async Task BuildWinUIRenderer(string dotnet, string rid, string output)
    {
        await Exec(
            dotnet,
            [
                "publish",
                Path.Combine(Root, "native/winui/DotNative.WinUI.csproj"),
                "-c",
                "Release",
                "-r",
                rid,
                "-o",
                output,
            ]
        );
    }
}
