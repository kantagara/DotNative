using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace DotNative;

/// <summary>A single-face TTF/OTF font with its declared internal family name. Registered for the process lifetime.</summary>
public sealed class FontSource
{
    private static readonly ConditionalWeakTable<
        Assembly,
        ConcurrentDictionary<(string Family, string Resource), Lazy<FontSource>>
    > resources = new();

    public string Family { get; }

    /// <summary>Loads and caches an embedded font. Native registration is owned by the render session.</summary>
    public static FontSource FromResource(Assembly assembly, string family, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return resources
            .GetOrCreateValue(assembly)
            .GetOrAdd(
                (family, resourceName),
                key => new Lazy<FontSource>(() =>
                {
                    using var stream =
                        assembly.GetManifestResourceStream(key.Resource)
                        ?? throw new FileNotFoundException(
                            $"Missing embedded font: {key.Resource}"
                        );
                    using var output = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = stream.Read(chunk)) != 0)
                    {
                        if (output.Length + read > 8 * 1024 * 1024)
                            throw new ArgumentOutOfRangeException(nameof(resourceName));
                        output.Write(chunk, 0, read);
                    }
                    return FromBytes(
                        key.Family,
                        output.GetBuffer().AsSpan(0, checked((int)output.Length))
                    );
                })
            )
            .Value;
    }

    /// <summary>Returns a copy for native plugin APIs that accept encoded font data.</summary>
    public byte[] ToArray() => (byte[])Data.Clone();

    internal byte[] Data { get; }

    internal (string Family, string Hash) CacheKey { get; }

    private FontSource(string family, byte[] data)
    {
        Family = family;
        Data = data;
        CacheKey = (family.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(data)));
    }

    public static FontSource FromBytes(string family, ReadOnlySpan<byte> encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        new TypographyStyle(FontFamily: family).Validate();
        if (encoded.Length < 12 || encoded.Length > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(encoded));
        return new(family, encoded.ToArray());
    }

    /// <summary>Extracts a zero-based face from a TTC/OTC collection (up to 32 MiB).</summary>
    public static FontSource FromCollectionBytes(
        string family,
        ReadOnlySpan<byte> encoded,
        int faceIndex
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        new TypographyStyle(FontFamily: family).Validate();
        return new(family, FontCollections.Extract(encoded, faceIndex));
    }

    public static async Task<FontSource> FromCollectionFileAsync(
        string family,
        string path,
        int faceIndex,
        CancellationToken cancellation = default
    )
    {
        await using var stream = File.OpenRead(path);
        return await FromCollectionStreamAsync(family, stream, faceIndex, cancellation);
    }

    /// <summary>Extracts one face without closing the caller's stream.</summary>
    public static async Task<FontSource> FromCollectionStreamAsync(
        string family,
        Stream stream,
        int faceIndex,
        CancellationToken cancellation = default
    )
    {
        if (faceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        var encoded = await ReadAsync(stream, FontCollections.MaximumCollectionBytes, cancellation);
        return FromCollectionBytes(family, encoded, faceIndex);
    }

    public static async Task<FontSource> FromFileAsync(
        string family,
        string path,
        CancellationToken cancellation = default
    )
    {
        await using var stream = File.OpenRead(path);
        return await FromStreamAsync(family, stream, cancellation);
    }

    /// <summary>Reads an embedded resource or other stream without closing it.</summary>
    public static async Task<FontSource> FromStreamAsync(
        string family,
        Stream stream,
        CancellationToken cancellation = default
    )
    {
        return FromBytes(family, await ReadAsync(stream, 8 * 1024 * 1024, cancellation));
    }

    private static async Task<byte[]> ReadAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellation
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var result = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation)) != 0)
        {
            if (result.Length + read > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(stream));
            result.Write(chunk, 0, read);
        }
        return result.ToArray();
    }
}

public static class AssetTypography
{
    public static T FontSource<T>(this T node, FontSource? source)
        where T : Node, ITextual =>
        (T)(
            node with
            {
                FontAsset = source,
                Typography = node.Typography with { FontFamily = source?.Family ?? "" },
            }
        );
}
