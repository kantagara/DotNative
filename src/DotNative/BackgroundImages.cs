namespace DotNative;

public static class BackgroundImages
{
    /// <summary>Draws an image above the box background/gradient and below children. Null clears it.</summary>
    public static T BackgroundImage<T>(
        this T node,
        ImageSource? source,
        ImageFit fit = ImageFit.Cover
    )
        where T : ContainerNode
    {
        if (!Enum.IsDefined(fit))
            throw new ArgumentOutOfRangeException(nameof(fit));
        return (T)(
            node with
            {
                BackgroundImageSource = source,
                BackgroundImageFit = source is null ? ImageFit.Cover : fit,
            }
        );
    }
}
