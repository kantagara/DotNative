using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = Path.Combine(
    Path.GetTempPath(),
    "dotnative-apple-tests-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "test.c");
    File.WriteAllText(source, "int sdk_value(void) { return 42; }\n");
    var obj = Path.Combine(root, "test.o");
    var binary = Path.Combine(root, "SDK");
    await Run(
        "xcrun",
        [
            "--sdk",
            "iphonesimulator",
            "clang",
            "-target",
            "arm64-apple-ios16.0-simulator",
            "-c",
            source,
            "-o",
            obj,
        ]
    );
    await Run("xcrun", ["ar", "rcs", binary, obj]);
    var archive = MakeZip(File.ReadAllBytes(binary));
    var handler = new Download(archive);
    using var http = new HttpClient(handler);
    var cache = Path.Combine(root, "cache");
    var dep = Dependency(archive);
    var manifest = WriteManifest("manifest", dep);
    var plan = await AppleDependencies.Resolve([manifest], "iossimulator-arm64", cache, http);
    Check(
        plan.FrameworkPaths.Length == 1 && plan.BundleInputs.Length == 1,
        "framework and embedded privacy bundle resolved"
    );
    Check(plan.FrameworkPaths.Single().Contains("iossimulator-arm64"), "simulator isolated cache");
    Check(handler.Requests == 1, "one archive download");
    await AppleDependencies.Resolve([manifest, manifest], "iossimulator-arm64", cache, http);
    Check(handler.Requests == 1, "verified cache reuse");
    var merged = await AppleDependencies.Resolve(
        [
            WriteManifest("system-one", dep with { SystemFrameworks = ["Security"] }),
            WriteManifest("system-two", dep with { SystemFrameworks = ["Foundation", "Security"] }),
        ],
        "iossimulator-arm64",
        cache,
        http
    );
    Check(
        merged.FrameworkPaths.Length == 1
            && merged.SystemFrameworks.SequenceEqual(new[] { "Foundation", "Security" }),
        "compatible shared SDK declarations merge once"
    );
    await Reject(
        () => AppleDependencies.Resolve([manifest], "iossimulator-arm64", cache, http, "15.0"),
        "requires iOS",
        "binary deployment target mismatch"
    );
    var archivePath = Path.Combine(cache, dep.Sha256.ToLowerInvariant() + ".zip");
    File.WriteAllText(archivePath, "corrupt cached archive");
    await Reject(
        () => AppleDependencies.Resolve([manifest], "iossimulator-arm64", cache, http),
        "SHA-256",
        "cached archive corruption"
    );
    File.WriteAllBytes(archivePath, archive);
    File.WriteAllText(Path.Combine(plan.FrameworkPaths[0], "SDK"), "damaged binary");
    plan = await AppleDependencies.Resolve([manifest], "iossimulator-arm64", cache, http);
    Check(
        File.ReadAllBytes(Path.Combine(plan.FrameworkPaths[0], "SDK"))
            .SequenceEqual(File.ReadAllBytes(binary)),
        "corrupt selected cache rebuilt"
    );
    await Task.WhenAll(
        Enumerable
            .Range(0, 3)
            .Select(_ => AppleDependencies.Resolve([manifest], "iossimulator-arm64", cache, http))
    );
    Check(handler.Requests == 1, "concurrent resolver cache locking");
    var device = await AppleDependencies.Resolve([manifest], "ios-arm64", cache, http);
    Check(
        File.ReadAllText(
            Path.Combine(device.FrameworkPaths[0], "SDK_Privacy.bundle", "platform.txt")
        ) == "device",
        "device slice selected by metadata"
    );
    var output = Path.Combine(root, "output");
    var bundle = Path.Combine(root, "Test.app");
    Directory.CreateDirectory(bundle);
    File.WriteAllText(Path.Combine(bundle, "app-owned.txt"), "keep");
    AppleDependencies.WritePlan(output, plan);
    await AppleDependencies.StageBundle(output, bundle, "-");
    Check(
        File.Exists(Path.Combine(bundle, "SDK_Privacy.bundle", "PrivacyInfo.xcprivacy")),
        "static framework privacy bundled into app"
    );
    Directory.CreateDirectory(Path.Combine(bundle, "SDK_Privacy.bundle.dotnative-staging"));
    AppleDependencies.WritePlan(output, new([], [], []));
    await AppleDependencies.StageBundle(output, bundle, "-");
    Check(
        !Directory.Exists(Path.Combine(bundle, "SDK_Privacy.bundle"))
            && !Directory.Exists(Path.Combine(bundle, "SDK_Privacy.bundle.dotnative-staging"))
            && File.Exists(Path.Combine(bundle, "app-owned.txt")),
        "removed dependency cleanup preserves app resources"
    );
    var retryApp = Path.Combine(root, "Retry.app");
    Directory.CreateDirectory(retryApp);
    Directory.CreateDirectory(Path.Combine(root, "missing"));
    AppleDependencies.WritePlan(
        output,
        plan with
        {
            BundleInputs =
            [
                .. plan.BundleInputs,
                new(Path.Combine(root, "missing"), "Missing.bundle"),
            ],
        }
    );
    Directory.Delete(Path.Combine(root, "missing"));
    await Reject(
        () => AppleDependencies.StageBundle(output, retryApp, "-"),
        "Could not find",
        "partial resource stage"
    );
    AppleDependencies.WritePlan(output, plan);
    await AppleDependencies.StageBundle(output, retryApp, "-");
    Check(
        Directory.Exists(Path.Combine(retryApp, "SDK_Privacy.bundle")),
        "retry recovers after partial resource stage"
    );
    Directory.CreateDirectory(Path.Combine(bundle, "SDK_Privacy.bundle"));
    AppleDependencies.WritePlan(output, plan);
    await Reject(
        () => AppleDependencies.StageBundle(output, bundle, "-"),
        "app-owned",
        "app resource collision"
    );
    await Reject(
        () =>
            AppleDependencies.Resolve(
                [manifest, WriteManifest("conflict", dep with { Version = "2" })],
                "ios-arm64",
                cache,
                http
            ),
        "Conflicting",
        "version conflict"
    );
    await Reject(
        () => AppleDependencies.Resolve([manifest], "osx-arm64", cache, http),
        "support",
        "unsupported RID"
    );
    await Reject(
        () =>
            AppleDependencies.Resolve(
                [WriteManifest("bad-digest", dep with { Sha256 = new string('0', 64) })],
                "ios-arm64",
                cache,
                http
            ),
        "SHA-256",
        "checksum mismatch"
    );
    await Reject(
        () =>
            AppleDependencies.Resolve(
                [WriteManifest("bad-path", dep with { Xcframeworks = ["../SDK.xcframework"] })],
                "ios-arm64",
                cache,
                http
            ),
        "Unsafe",
        "manifest traversal"
    );
    foreach (
        var (name, zip, expected) in new[]
        {
            ("zip-traversal", MakeZip(File.ReadAllBytes(binary), unsafePath: true), "Unsafe"),
            (
                "missing-slice",
                MakeZip(File.ReadAllBytes(binary), missingSlice: true),
                "exactly one"
            ),
            ("symlink", MakeZip(File.ReadAllBytes(binary), symlink: true), "Symlink"),
        }
    )
    {
        using var client = new HttpClient(new Download(zip));
        await Reject(
            () =>
                AppleDependencies.Resolve(
                    [WriteManifest(name, Dependency(zip))],
                    "iossimulator-arm64",
                    cache,
                    client
                ),
            expected,
            name
        );
    }
    var dynamicBinary = Path.Combine(root, "Dynamic");
    await Run(
        "xcrun",
        [
            "--sdk",
            "iphonesimulator",
            "clang",
            "-target",
            "arm64-apple-ios16.0-simulator",
            "-dynamiclib",
            source,
            "-install_name",
            "@rpath/SDK.framework/SDK",
            "-o",
            dynamicBinary,
        ]
    );
    var dynamicArchive = MakeZip(File.ReadAllBytes(dynamicBinary));
    using var dynamicClient = new HttpClient(new Download(dynamicArchive));
    var dynamicPlan = await AppleDependencies.Resolve(
        [WriteManifest("dynamic", Dependency(dynamicArchive))],
        "iossimulator-arm64",
        cache,
        dynamicClient
    );
    Check(dynamicPlan.BundleInputs.Single().Sign, "dynamic framework selected for embedding");
    var dynamicApp = Path.Combine(root, "Dynamic.app");
    Directory.CreateDirectory(dynamicApp);
    AppleDependencies.WritePlan(output, dynamicPlan);
    await AppleDependencies.StageBundle(output, dynamicApp, "-");
    await Run(
        "codesign",
        ["--verify", "--strict", Path.Combine(dynamicApp, "Frameworks/SDK.framework")]
    );
    var unsafeApp = Path.Combine(root, "Unsafe.app");
    Directory.CreateDirectory(unsafeApp);
    var outside = Path.Combine(root, "external");
    Directory.CreateDirectory(outside);
    Directory.CreateSymbolicLink(Path.Combine(unsafeApp, "Frameworks"), outside);
    await Reject(
        () => AppleDependencies.StageBundle(output, unsafeApp, "-"),
        "Symlink",
        "bundle ancestor symlink"
    );
    Check(Directory.GetFileSystemEntries(outside).Length == 0, "no writes outside app bundle");
    Check(
        File.Exists(
            Path.Combine(
                dynamicApp,
                "Frameworks/SDK.framework/SDK_Privacy.bundle/PrivacyInfo.xcprivacy"
            )
        ),
        "dynamic framework preserves resources and signature"
    );
    Console.WriteLine("PASS Apple dependencies integration tests");

    string WriteManifest(string name, AppleDependencies.Dependency dependency)
    {
        var path = Path.Combine(root, name + ".json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new AppleDependencies.Manifest(1, [dependency]),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            )
        );
        return path;
    }
}
finally
{
    Directory.Delete(root, true);
}

static AppleDependencies.Dependency Dependency(byte[] archive) =>
    new(
        "test-sdk",
        "1.0",
        "https://sdk.example.invalid/sdk.zip",
        Convert.ToHexString(SHA256.HashData(archive)),
        ["SDK.xcframework"]
    );
static byte[] MakeZip(
    byte[] binary,
    bool unsafePath = false,
    bool missingSlice = false,
    bool symlink = false
)
{
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        void Text(string path, string value)
        {
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(value);
        }
        var libraries = "";
        foreach (var (id, variant) in new[] { ("device", ""), ("sim", "simulator") })
        {
            libraries +=
                $"<dict><key>LibraryIdentifier</key><string>{id}</string><key>LibraryPath</key><string>SDK.framework</string><key>SupportedPlatform</key><string>ios</string><key>SupportedArchitectures</key><array><string>{(missingSlice ? "x86_64" : "arm64")}</string></array>"
                + (
                    variant == ""
                        ? ""
                        : $"<key>SupportedPlatformVariant</key><string>{variant}</string>"
                )
                + "</dict>";
            var prefix = $"SDK.xcframework/{id}/SDK.framework/";
            var entry = zip.CreateEntry(prefix + "SDK");
            using (var stream = entry.Open())
                stream.Write(binary);
            if (symlink)
                entry.ExternalAttributes = 0xa1ff << 16;
            Text(
                prefix + "Info.plist",
                "<plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>org.example.SDK</string><key>CFBundleExecutable</key><string>SDK</string><key>CFBundlePackageType</key><string>FMWK</string></dict></plist>"
            );
            Text(
                prefix + "SDK_Privacy.bundle/PrivacyInfo.xcprivacy",
                "<plist version=\"1.0\"><dict/></plist>"
            );
            Text(prefix + "SDK_Privacy.bundle/platform.txt", id);
        }
        Text(
            "SDK.xcframework/Info.plist",
            "<plist version=\"1.0\"><dict><key>AvailableLibraries</key><array>"
                + libraries
                + "</array></dict></plist>"
        );
        if (unsafePath)
            Text("../escape.txt", "invalid");
    }
    return memory.ToArray();
}
static void Check(bool condition, string label)
{
    if (!condition)
        throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
static async Task Reject(Func<Task> action, string expected, string label)
{
    try
    {
        await action();
    }
    catch (Exception error)
        when (error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("PASS rejected " + label);
        return;
    }
    throw new Exception("Expected rejection: " + label);
}
static async Task Run(string file, string[] args)
{
    var info = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = false };
    foreach (var arg in args)
        info.ArgumentList.Add(arg);
    using var process = System.Diagnostics.Process.Start(info)!;
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
        throw new Exception(file + " failed");
}

sealed class Download(byte[] archive) : HttpMessageHandler
{
    internal int Requests;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken token
    )
    {
        Interlocked.Increment(ref Requests);
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
                RequestMessage = request,
            }
        );
    }
}
