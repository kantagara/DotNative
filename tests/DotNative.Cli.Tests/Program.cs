var devices = new List<Device>
{
    new("SIM-1", "Phone", "ios", "simulator", "Booted", "arm64", true),
    new("SIM-2", "Phone", "ios", "simulator", "Shutdown", "arm64", true),
    new("emulator-5554", "Android", "android", "emulator", "device", "arm64-v8a", true),
    new("USB-1", "Unauthorized", "android", "physical", "unauthorized", "unknown", false),
    new("mac", "Mac", "mac", "desktop", "ready", "arm64", true),
};
void Require(bool condition)
{
    if (!condition)
        throw new Exception("Device selection assertion failed.");
}
void Reject(string id, string? platform = null)
{
    try
    {
        Devices.Select(devices, id, platform);
    }
    catch (ArgumentException)
    {
        return;
    }
    throw new Exception("Invalid/ambiguous selection accepted: " + id);
}
Require(Devices.Select(devices, "sim-1", null).Platform == "ios");
Require(Devices.Select(devices, "emulator-5554", null).Platform == "android");
Require(Devices.Select(devices, "mac", null).Platform == "mac");
Require(Devices.Select(devices, "booted", null).Id == "SIM-1");
Require(Devices.Select(devices, null, "ios").Id == "SIM-1");
Require(Devices.Select(devices, "SIM-2", null).State == "Shutdown");
Require(!Devices.Select(devices, "USB-1", null).CanPreview);
Reject("Phone");
Reject("SIM-1", "android");
Reject("missing");
Console.WriteLine(
    "PASS device routing, booted selection, unavailable devices, duplicate names and platform mismatch"
);

devices.Add(new("windows", "Windows PC", "windows", "desktop", "ready", "x64", true));
devices.Add(new("linux", "Linux PC", "linux", "desktop", "ready", "arm64", true));
Require(Devices.Select(devices, "windows", null).Platform == "windows");
Require(Devices.Select(devices, null, "linux").Id == "linux");
Require(DesktopTarget.Resolve("windows", "x64") == ("x86_64-pc-windows-msvc", "WINDOWS", 4974));
Require(DesktopTarget.Resolve("windows", "arm64").Target == "aarch64-pc-windows-msvc");
Require(DesktopTarget.Resolve("linux", "x64").Target == "x86_64-unknown-linux-gnu");
Require(DesktopTarget.Resolve("linux", "arm64") == ("aarch64-unknown-linux-gnu", "LINUX", 4975));
try
{
    DesktopTarget.Resolve("linux", "x86");
    throw new Exception("Unsupported architecture accepted.");
}
catch (NotSupportedException) { }
Reject("windows", "linux");
Console.WriteLine("PASS Windows/Linux device selection, CPU targets and preview ports");

var pluginTemporary = Path.Combine(Path.GetTempPath(), "dotnative-plugin-tests-" + Guid.NewGuid());
try
{
    var nativeProject = PluginScaffold.Create(
        Path.Combine(pluginTemporary, "native"),
        "Tests.Native",
        false,
        "src/DotNative/DotNative.csproj"
    );
    var nativeDirectory = Path.GetDirectoryName(nativeProject)!;
    foreach (var platform in new[] { "Android", "iOS", "Mac", "Windows", "Linux" })
        Require(
            Directory.GetFiles(Path.Combine(nativeDirectory, "Platform", platform)).Length == 1
        );
    Require(!Directory.Exists(Path.Combine(nativeDirectory, "Generated")));
    try
    {
        PluginScaffold.Create(nativeDirectory, "Tests.Native", false, "core.csproj");
        throw new Exception("Overwrote existing plugin");
    }
    catch (IOException) { }
    try
    {
        PluginScaffold.Create(
            Path.Combine(pluginTemporary, "bad"),
            "bad/name",
            false,
            "core.csproj"
        );
        throw new Exception("Accepted invalid plugin name");
    }
    catch (ArgumentException) { }
    Require(!Directory.Exists(Path.Combine(pluginTemporary, "bad")));
    var managedProject = PluginScaffold.Create(
        Path.Combine(pluginTemporary, "managed"),
        "Tests.Managed",
        true,
        "src/DotNative/DotNative.csproj"
    );
    var managed = System.Xml.Linq.XDocument.Load(managedProject);
    Require(managed.Descendants("TargetFrameworks").First().Value.Split(';').Length == 5);
    Require(!managed.Descendants("UseMaui").Any());
    var selection = File.ReadAllText(
        Path.Combine(Path.GetDirectoryName(managedProject)!, "PlatformSources.targets")
    );
    Require(selection.Contains("Compile Remove=\"Platform/**/*.cs\""));
    Require(selection.Contains("DNPLUGIN001") && selection.Contains("DOTNATIVE_LINUX"));
    Console.WriteLine(
        "PASS native and managed plugin scaffolds: five platforms, named channels, platform source selection, experimental diagnostic, overwrite rejection"
    );
}
finally
{
    if (Directory.Exists(pluginTemporary))
        Directory.Delete(pluginTemporary, true);
}

Require(DependencyRevision.IsValid("v0.1.0-preview.1"));
Require(DependencyRevision.IsValid("v1.2.3"));
Require(!DependencyRevision.IsValid("main") && !DependencyRevision.IsValid("../unsafe"));
var lightweight = new string('a', 40);
var annotated = new string('b', 40);
Require(
    DependencyRevision.TagCommit("v1.2.3", lightweight + "\trefs/tags/v1.2.3\n") == lightweight
);
Require(
    DependencyRevision.TagCommit(
        "v1.2.3",
        lightweight + "\trefs/tags/v1.2.3\n" + annotated + "\trefs/tags/v1.2.3^{}\n"
    ) == annotated
);
try
{
    DependencyRevision.TagCommit("v1.2.3", lightweight + "\trefs/tags/v1.2.30\n");
    throw new Exception("Missing version tag accepted");
}
catch (InvalidDataException) { }
Console.WriteLine(
    "PASS dependency version validation and lightweight/annotated release tag resolution"
);
