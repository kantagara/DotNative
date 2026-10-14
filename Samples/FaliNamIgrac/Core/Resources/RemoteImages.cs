using System.Buffers.Binary;
using System.Collections.Concurrent;
using DotNative;
using DotNative.Hive;

namespace FaliNamIgrac;

// Public media only: auth tokens never become image URLs or disk keys.
public sealed class ImageRepository : IDisposable
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly string directory;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> requests = new();
    private readonly SemaphoreSlim downloads = new(4);
    private readonly SemaphoreSlim disk = new(1);

    public ImageRepository(string directory, HttpClient? http = null)
    {
        this.directory = directory;
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ownsHttp = http is null;
    }

    public async Task<ImageSource?> LoadAsync(string url, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        var request = requests.GetOrAdd(url, _ => new(() => LoadRequest(url, uri)));
        try
        {
            return await request.Value.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            if (request.IsValueCreated && request.Value.IsCompleted)
                requests.TryRemove(
                    new KeyValuePair<string, Lazy<Task<ImageSource?>>>(url, request)
                );
        }
    }

    private async Task<ImageSource?> LoadRequest(string key, Uri uri)
    {
        await Task.Yield(); // Publish the Lazy before completion/removal, including disk hits.
        try
        {
            return await LoadCore(uri, lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            requests.TryRemove(key, out _);
        }
    }

    private async Task<ImageSource?> LoadCore(Uri uri, CancellationToken token)
    {
        var file = Path.Combine(directory, HiveKeys.Hash(uri.AbsoluteUri) + ".image");
        byte[]? cached = null;
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length <= MaxBytes)
            {
                cached = await File.ReadAllBytesAsync(file, token).ConfigureAwait(false);
                if (!Valid(cached))
                    cached = null;
                if (
                    cached is not null
                    && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(1)
                )
                    return ImageSource.FromBytes(cached);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        await downloads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var response = await http.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    token
                )
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (
                response.RequestMessage?.RequestUri is { } finalUri
                    && finalUri.Scheme != Uri.UriSchemeHttps
                || response.Content.Headers.ContentLength > MaxBytes
            )
                return cached is null ? null : ImageSource.FromBytes(cached);
            await using var stream = await response
                .Content.ReadAsStreamAsync(token)
                .ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(stream, token).ConfigureAwait(false);
            if (!Valid(bytes))
                return cached is null ? null : ImageSource.FromBytes(cached);
            try
            {
                await CommitAsync(file, bytes, token).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return ImageSource.FromBytes(bytes);
        }
        catch (Exception error)
            when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            return cached is null ? null : ImageSource.FromBytes(cached);
        }
        finally
        {
            downloads.Release();
        }
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaxBytes)
                throw new InvalidDataException("Image exceeds 8 MiB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public static bool Valid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 12 or > MaxBytes)
            return false;
        static bool Size(uint width, uint height) =>
            width is > 0 and <= 8192
            && height is > 0 and <= 8192
            && (long)width * height <= 16_777_216;
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return bytes.Length >= 45
                && bytes.Slice(12, 4).SequenceEqual("IHDR"u8)
                && Size(
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)),
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4))
                )
                && bytes.Slice(bytes.Length - 8, 4).SequenceEqual("IEND"u8);
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return Size(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2))
            );
        if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
        {
            var offset = 2;
            while (offset + 4 <= bytes.Length)
            {
                if (bytes[offset++] != 255)
                    return false;
                while (offset < bytes.Length && bytes[offset] == 255)
                    offset++;
                if (offset >= bytes.Length)
                    return false;
                var marker = bytes[offset++];
                if (marker is 0xda or 0xd9)
                    return false;
                if (marker is 0x01 or >= 0xd0 and <= 0xd8)
                    continue;
                if (offset + 2 > bytes.Length)
                    return false;
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
                if (length < 2 || offset + length > bytes.Length)
                    return false;
                if (
                    marker
                    is 0xc0
                        or 0xc1
                        or 0xc2
                        or 0xc3
                        or 0xc5
                        or 0xc6
                        or 0xc7
                        or 0xc9
                        or 0xca
                        or 0xcb
                        or 0xcd
                        or 0xce
                        or 0xcf
                )
                    return length >= 8
                        && Size(
                            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)),
                            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2))
                        );
                offset += length;
            }
            return false;
        }
        if (
            !bytes.StartsWith("RIFF"u8)
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            || bytes.Length < 30
        )
            return false;
        if (bytes.Slice(12, 4).SequenceEqual("VP8X"u8))
            return Size(
                1u + bytes[24] + ((uint)bytes[25] << 8) + ((uint)bytes[26] << 16),
                1u + bytes[27] + ((uint)bytes[28] << 8) + ((uint)bytes[29] << 16)
            );
        if (
            bytes.Slice(12, 4).SequenceEqual("VP8 "u8)
            && bytes.Slice(23, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a })
        )
            return Size(
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3fff),
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3fff)
            );
        if (bytes.Slice(12, 4).SequenceEqual("VP8L"u8) && bytes[20] == 0x2f)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(21, 4));
            return Size((bits & 0x3fff) + 1, ((bits >> 14) & 0x3fff) + 1);
        }
        return false;
    }

    public async Task StoreAsync(string url, byte[] bytes, CancellationToken token)
    {
        if (!Valid(bytes))
            throw new InvalidDataException("Unsupported image.");
        if (requests.TryGetValue(url, out var active))
            await active.Value.WaitAsync(token).ConfigureAwait(false);
        await CommitAsync(Path.Combine(directory, HiveKeys.Hash(url) + ".image"), bytes, token)
            .ConfigureAwait(false);
    }

    private async Task CommitAsync(string file, byte[] bytes, CancellationToken token)
    {
        await disk.WaitAsync(token).ConfigureAwait(false);
        var temporary = file + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            File.Move(temporary, file, true);
            var files = new DirectoryInfo(directory)
                .GetFiles("*.image")
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .ToArray();
            long total = 0;
            for (var i = 0; i < files.Length; i++)
            {
                total += files[i].Length;
                if (i >= 128 || total > 64L * 1024 * 1024)
                    files[i].Delete();
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            finally
            {
                disk.Release();
            }
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        if (ownsHttp)
            http.Dispose();
    }
}

// UI-scoped reactive entries: virtualized rows rebuild when their own image arrives.
public sealed class RemoteImages(
    ImageRepository repository,
    IUiDispatcher dispatcher,
    CancellationToken token
)
{
    private readonly Dictionary<
        string,
        (WeakReference<State<ImageSource?>> Image, DateTime At)
    > entries = new();

    public ImageSource? Get(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (!entries.TryGetValue(url, out var entry) || !entry.Image.TryGetTarget(out var state))
        {
            // Render dependencies retain visible states. Offscreen media can be collected.
            if (entries.Count >= 128)
                foreach (
                    var key in entries
                        .Where(item => !item.Value.Image.TryGetTarget(out _))
                        .Select(item => item.Key)
                        .ToArray()
                )
                    entries.Remove(key);
            state = new(null);
            entries[url] = (new(state), DateTime.UtcNow);
            replaced.Remove(url);
            var target = state;
            dispatcher.Post(() =>
            {
                if (!token.IsCancellationRequested)
                    _ = Fetch(url, target);
            });
        }
        else if (
            DateTime.UtcNow - entry.At
            > (state.Value is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromDays(1))
        )
        {
            entries[url] = (entry.Image, DateTime.UtcNow);
            replaced.Remove(url);
            var target = state;
            dispatcher.Post(() =>
            {
                if (!token.IsCancellationRequested)
                    _ = Fetch(url, target);
            });
        }
        return state.Value;
    }

    public async Task RememberAsync(string? url, byte[] bytes)
    {
        if (url is null)
            return;
        await dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested)
                return;
            if (entries.TryGetValue(url, out var entry) && entry.Image.TryGetTarget(out var state))
            {
                state.Value = ImageSource.FromBytes(bytes);
                entries[url] = (entry.Image, DateTime.UtcNow);
            }
            else
                entries[url] = (
                    new(new State<ImageSource?>(ImageSource.FromBytes(bytes))),
                    DateTime.UtcNow
                );
            replaced.Add(url);
        });
        try
        {
            await repository.StoreAsync(url, bytes, token);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private readonly HashSet<string> replaced = new();

    private async Task Fetch(string url, State<ImageSource?> image)
    {
        try
        {
            var result = await repository.LoadAsync(url, token);
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && !replaced.Contains(url))
                    image.Value = result;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        { /* Keep initials for unavailable/invalid media. */
        }
    }
}
