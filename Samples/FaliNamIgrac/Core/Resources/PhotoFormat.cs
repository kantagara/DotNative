namespace FaliNamIgrac;

public sealed record PhotoFormat(string Extension, string ContentType)
{
    public static PhotoFormat Detect(byte[] bytes)
    {
        if (!ImageRepository.Valid(bytes))
            throw new InvalidDataException(
                "Choose a PNG, JPEG, GIF or WebP image up to 8 MiB and 16 megapixels."
            );
        return bytes[0] switch
        {
            137 => new(".png", "image/png"),
            255 => new(".jpg", "image/jpeg"),
            71 => new(".gif", "image/gif"),
            _ => new(".webp", "image/webp"),
        };
    }
}
