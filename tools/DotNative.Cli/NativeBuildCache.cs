using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class NativeBuildCache
{
    private sealed record Stamp(string Inputs, string Outputs);

    private readonly string stampPath;
    private readonly string inputs;
    private readonly string[] outputs;

    internal NativeBuildCache(
        string stampPath,
        IEnumerable<string> inputs,
        IEnumerable<string> outputs,
        params string[] configuration
    )
    {
        this.stampPath = stampPath;
        this.outputs = outputs.ToArray();
        this.inputs = Fingerprint(
            inputs.Append(Assembly.GetExecutingAssembly().Location),
            configuration
        );
    }

    internal bool IsCurrent
    {
        get
        {
            try
            {
                if (!outputs.All(path => File.Exists(path) || Directory.Exists(path)))
                    return false;
                var saved = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(stampPath));
                return saved?.Inputs == inputs && saved.Outputs == Fingerprint(outputs);
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    internal void Commit()
    {
        if (!outputs.All(path => File.Exists(path) || Directory.Exists(path)))
            throw new IOException("Native build did not produce its declared outputs.");
        Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
        var temporary = stampPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(new Stamp(inputs, Fingerprint(outputs)))
        );
        File.Move(temporary, stampPath, overwrite: true);
    }

    internal string Revision => Fingerprint(outputs);

    internal static string ContentRevision(string path) => Fingerprint([path]);

    private static string Fingerprint(IEnumerable<string> paths, params string[] configuration)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([0]);
        }
        foreach (var value in configuration)
            Add(value);
        foreach (
            var path in paths.Select(Path.GetFullPath).Distinct().Order(StringComparer.Ordinal)
        )
        {
            Add(path);
            var files = Directory.Exists(path)
                ? Directory
                    .GetFiles(path, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                : new[] { path }.AsEnumerable();
            foreach (var file in files)
            {
                Add(file);
                if (!File.Exists(file))
                {
                    Add("missing");
                    continue;
                }
                using var stream = File.OpenRead(file);
                hash.AppendData(SHA256.HashData(stream));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal static partial class Preview
{
    private static async Task CachedNativeBuild(
        string label,
        string stamp,
        string[] inputs,
        string[] outputs,
        string file,
        string[] args
    )
    {
        var compiler = (
            await Capture("xcrun", [args.Contains("swiftc") ? "swiftc" : "clang", "--version"])
        ).Trim();
        var cache = new NativeBuildCache(stamp, inputs, outputs, [compiler, .. args]);
        if (cache.IsCurrent)
        {
            Console.WriteLine($"[build] {label}: unchanged; using cached output.");
            return;
        }
        await Exec(file, args);
        cache.Commit();
    }

    private static void CopyFileIfChanged(string source, string target)
    {
        if (
            !File.Exists(target)
            || !SHA256
                .HashData(File.ReadAllBytes(source))
                .AsSpan()
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(target)))
        )
            File.Copy(source, target, true);
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content);
    }
}
