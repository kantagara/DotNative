using DotNative;

namespace FaliNamIgrac;

public static class FniAssets
{
    private static readonly Dictionary<string, ImageSource> images = new();

    public static ImageSource Image(string name)
    {
        lock (images)
        {
            if (!images.TryGetValue(name, out var image))
                images.Add(name, image = ImageSource.FromBytes(Read(name)));
            return image;
        }
    }

    public static FontSource Font(string face = "Regular") =>
        FontSource.FromResource(
            typeof(FniAssets).Assembly,
            face == "Icons" ? "Material Icons"
                : face == "Bold" ? "Rounded Mplus 1c Bold"
                : "Rounded Mplus 1c",
            face == "Icons"
                ? "fni/fonts/MaterialIcons-Regular.otf"
                : $"fni/fonts/MPLUSRounded1c-{face}.ttf"
        );

    public static byte[] FontData(string face = "Regular") => Font(face).ToArray();

    internal static byte[] Read(string name)
    {
        using var stream =
            typeof(FniAssets).Assembly.GetManifestResourceStream("fni/" + name)
            ?? throw new FileNotFoundException("Missing original application asset: " + name);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
