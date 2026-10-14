using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed record PluginProject(
    string Project,
    string Entry,
    string? PackageId = null,
    string? PackageVersion = null
)
{
    internal string Directory =>
        PackageId is null
            ? Path.GetDirectoryName(Project)!
            : Path.GetDirectoryName(Path.GetDirectoryName(Project)!)!;
}

internal static partial class Preview
{
    private static void StageAndroidPlugins(
        List<PluginProject> plugins,
        string native,
        string? originalGradle = null,
        string? bundle = null
    )
    {
        var destination = Path.Combine(native, "app/src/main/java/com/dotnative/plugins");
        Directory.CreateDirectory(destination);
        var desired = new HashSet<string>(StringComparer.Ordinal);
        void Stage(string folder)
        {
            if (!Directory.Exists(folder))
                return;
            foreach (var source in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(folder, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyFileIfChanged(source, target);
                desired.Add(target);
            }
        }
        Stage(Path.Combine(Root, "native/plugins/android"));
        var dependencies = new List<string>();
        foreach (var plugin in plugins.Where(plugin => plugin.Entry.Length != 0))
        {
            var source = Path.Combine(plugin.Directory, "Platform/Android");
            Stage(source);
            var script = Path.Combine(source, "dependencies.gradle");
            if (File.Exists(script))
                dependencies.Add(File.ReadAllText(script));
        }
        var registration = Path.Combine(destination, "GeneratedPlugins.kt");
        WriteIfChanged(
            registration,
            "package com.dotnative.plugins\nimport android.app.Activity\nobject GeneratedPlugins {\n@JvmStatic fun register(activity: Activity) {\n"
                + string.Join(
                    "\n",
                    plugins
                        .Where(plugin => plugin.Entry.Length != 0)
                        .Select(plugin => $"{plugin.Entry}(activity)")
                )
                + "\n}\n}\n"
        );
        desired.Add(registration);
        foreach (var old in Directory.GetFiles(destination, "*", SearchOption.AllDirectories))
            if (!desired.Contains(old))
                File.Delete(old);
        var gradle = Path.Combine(native, "app/build.gradle");
        var content = File.ReadAllText(originalGradle ?? gradle);
        if (bundle is not null)
            content = Regex.Replace(content, "applicationId '[^']+'", $"applicationId '{bundle}'");
        WriteIfChanged(
            gradle,
            content
                + "\n"
                + string.Join("\n", dependencies)
                + "\nandroid { compileOptions { sourceCompatibility JavaVersion.VERSION_17; targetCompatibility JavaVersion.VERSION_17 } }\nkotlin { jvmToolchain(17) }\n"
        );
    }

    private static async Task<List<PluginProject>> DiscoverPlugins(
        string appProject,
        bool refreshAssets = false
    )
    {
        appProject = Path.GetFullPath(appProject);
        var assets = Path.Combine(Path.GetDirectoryName(appProject)!, "obj", "project.assets.json");
        if (refreshAssets || !File.Exists(assets))
            await Exec("dotnet", ["restore", appProject], Path.GetDirectoryName(appProject));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var plugins = new List<PluginProject>();
        await Visit(appProject);
        AddNuGetPlugins(assets);
        return plugins;
        async Task Visit(string path)
        {
            path = Path.GetFullPath(path);
            if (!seen.Add(path))
                return;
            using var evaluated = JsonDocument.Parse(
                await Capture(
                    "dotnet",
                    [
                        "msbuild",
                        path,
                        "-nologo",
                        "-getProperty:DotNativePluginEntry,DotNativePluginKind",
                        "-getItem:ProjectReference",
                    ]
                )
            );
            var properties = evaluated.RootElement.GetProperty("Properties");
            var entry = properties.GetProperty("DotNativePluginEntry").GetString() ?? "";
            var managed = properties.GetProperty("DotNativePluginKind").GetString() == "Managed";
            if (managed && entry.Length == 0)
                plugins.Add(new(path, ""));
            if (entry.Length != 0)
            {
                if (!Regex.IsMatch(entry, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new InvalidDataException("Invalid plugin entry point.");
                plugins.Add(new(path, entry));
            }
            foreach (
                var reference in evaluated
                    .RootElement.GetProperty("Items")
                    .GetProperty("ProjectReference")
                    .EnumerateArray()
            )
            {
                var include = reference.GetProperty("FullPath").GetString();
                if (!string.IsNullOrEmpty(include))
                    await Visit(include);
            }
        }

        void AddNuGetPlugins(string assetsPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
            if (
                !document.RootElement.TryGetProperty("libraries", out var libraries)
                || !document.RootElement.TryGetProperty("packageFolders", out var folders)
            )
                return;
            foreach (var library in libraries.EnumerateObject())
            {
                if (
                    !library.Value.TryGetProperty("type", out var type)
                    || type.GetString() != "package"
                    || !library.Value.TryGetProperty("path", out var relative)
                )
                    continue;
                var separator = library.Name.LastIndexOf('/');
                if (separator <= 0)
                    continue;
                var packageId = library.Name[..separator];
                var version = library.Name[(separator + 1)..];
                foreach (var folder in folders.EnumerateObject())
                {
                    var packageRoot = Path.GetFullPath(
                        Path.Combine(
                            folder.Name,
                            relative.GetString()!.Replace('/', Path.DirectorySeparatorChar)
                        )
                    );
                    var manifest = Path.Combine(packageRoot, "dotnative", "plugin.json");
                    if (!File.Exists(manifest))
                        continue;
                    using var metadata = JsonDocument.Parse(File.ReadAllBytes(manifest));
                    var root = metadata.RootElement;
                    if (
                        !root.TryGetProperty("id", out var id)
                        || !string.Equals(
                            id.GetString(),
                            packageId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        throw new InvalidDataException(
                            $"Plugin manifest package ID does not match {packageId}."
                        );
                    var kind = root.TryGetProperty("kind", out var kindProperty)
                        ? kindProperty.GetString()
                        : null;
                    var entry = root.TryGetProperty("entry", out var entryProperty)
                        ? entryProperty.GetString() ?? ""
                        : "";
                    if (kind is not ("managed" or "native"))
                        throw new InvalidDataException(
                            $"Plugin {packageId} must declare kind managed or native."
                        );
                    if (kind == "native" && !Regex.IsMatch(entry, "^[A-Za-z_][A-Za-z0-9_]*$"))
                        throw new InvalidDataException(
                            $"Native plugin {packageId} has no valid entry point."
                        );
                    var identity = packageId + "/" + version;
                    if (!seen.Add(identity))
                        break;
                    plugins.Add(new(manifest, entry, packageId, version));
                    break;
                }
            }
        }
    }

    private static string WritePreviewPluginReferences(
        List<PluginProject> plugins,
        string output,
        string? appProject = null
    )
    {
        var packages = new List<XElement>();
        if (appProject is not null)
        {
            var assets = Path.Combine(
                Path.GetDirectoryName(appProject)!,
                "obj",
                "project.assets.json"
            );
            using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
            var root = document.RootElement;
            var frameworks = root.GetProperty("project").GetProperty("frameworks");
            var framework = frameworks.TryGetProperty("net10.0", out var current)
                ? current
                : frameworks.EnumerateObject().First().Value;
            if (framework.TryGetProperty("dependencies", out var dependencies))
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Name is "DotNative" or "DotNative.App")
                        continue;
                    var library = root.GetProperty("libraries")
                        .EnumerateObject()
                        .FirstOrDefault(p =>
                            p.Name.StartsWith(
                                dependency.Name + "/",
                                StringComparison.OrdinalIgnoreCase
                            )
                            && p.Value.GetProperty("type").GetString() == "package"
                        );
                    if (library.Name is not null)
                        packages.Add(
                            new XElement(
                                "PackageReference",
                                new XAttribute("Include", dependency.Name),
                                new XAttribute(
                                    "Version",
                                    library.Name[(library.Name.LastIndexOf('/') + 1)..]
                                )
                            )
                        );
                }
        }
        var file = Path.Combine(output, "Plugins.props");
        var references = plugins
            .Select(p =>
                p.PackageId is null
                    ? new XElement("ProjectReference", new XAttribute("Include", p.Project))
                    : new XElement(
                        "PackageReference",
                        new XAttribute("Include", p.PackageId),
                        new XAttribute("Version", p.PackageVersion!)
                    )
            )
            .Concat(packages)
            .GroupBy(
                x => x.Name.LocalName + "/" + x.Attribute("Include")!.Value,
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.First());
        new XDocument(new XElement("Project", new XElement("ItemGroup", references))).Save(file);
        return file;
    }

    private static async Task<string?> BuildApplePlugins(
        List<PluginProject> plugins,
        string output,
        bool device,
        string platform = "ios"
    )
    {
        string Folder(PluginProject plugin) =>
            Path.Combine(plugin.Directory, "Platform", platform == "mac" ? "Mac" : "iOS");
        var active = plugins
            .Where(p =>
                p.Entry.Length != 0
                && Directory.Exists(Folder(p))
                && Directory
                    .GetFiles(Folder(p), "*.swift", SearchOption.AllDirectories)
                    .Any(f => !f.EndsWith(".g.swift", StringComparison.Ordinal))
            )
            .ToList();
        var sources = active
            .SelectMany(p => Directory.GetFiles(Folder(p), "*.swift", SearchOption.AllDirectories))
            .ToList();
        var appleSdk =
            platform == "mac" ? "macosx"
            : device ? "iphoneos"
            : "iphonesimulator";
        var triple =
            platform == "mac" ? "arm64-apple-macos13.0"
            : device ? "arm64-apple-ios16.0"
            : "arm64-apple-ios16.0-simulator";
        if (sources.Count == 0)
        {
            AppleDependencies.WritePlan(output, new([], [], []));
            // A weak declaration in the runner still needs a linkable symbol.
            // Apps without plugins have a real, empty registration entry point.
            var stub = Path.Combine(output, "PluginRegistration.c");
            var stubObject = Path.Combine(output, "Plugins.o");
            WriteIfChanged(
                stub,
                "void dotnative_register_plugins(void) {}\nvoid dotnative_apns_registered(const unsigned char *bytes, long count) {}\nvoid dotnative_apns_failed(const char *message) {}\n"
            );
            File.WriteAllLines(Path.Combine(output, "apple-frameworks.args"), []);
            await CachedNativeBuild(
                "Plugin registration",
                Path.Combine(output, "plugins.stamp.json"),
                [stub],
                [stubObject],
                "xcrun",
                ["--sdk", appleSdk, "clang", "-target", triple, "-c", stub, "-o", stubObject]
            );
            return stubObject;
        }
        sources.AddRange(Directory.GetFiles(Path.Combine(Root, "native/plugins/apple"), "*.swift"));
        var registration = Path.Combine(output, "PluginRegistration.swift");
        WriteIfChanged(
            registration,
            "@_cdecl(\"dotnative_register_plugins\")\n@MainActor public func registerPlugins() {\n"
                + string.Join("\n", active.Select(p => $"    {p.Entry}.register()"))
                + "\n}\n"
        );
        sources.Add(registration);
        var manifests =
            platform == "mac"
                ? []
                : active
                    .Select(p => Path.Combine(Folder(p), "apple-dependencies.json"))
                    .Where(File.Exists)
                    .ToArray();
        var dependencyPlan = await AppleDependencies.Resolve(
            manifests,
            device ? "ios-arm64" : "iossimulator-arm64",
            AppleDependencies.DefaultCache
        );
        AppleDependencies.WritePlan(output, dependencyPlan);
        var frameworkDirs = dependencyPlan
            .FrameworkPaths.Select(p => Path.GetDirectoryName(p)!)
            .Distinct()
            .ToArray();
        var frameworkArgs = new List<string>();
        if (frameworkDirs.Length > 0)
            frameworkArgs.Add("-ObjC");
        foreach (var dir in frameworkDirs)
            frameworkArgs.Add("-F" + dir);
        foreach (var framework in dependencyPlan.FrameworkPaths)
        {
            frameworkArgs.Add("-framework");
            frameworkArgs.Add(Path.GetFileNameWithoutExtension(framework));
        }
        foreach (var system in dependencyPlan.SystemFrameworks)
            frameworkArgs.AddRange(["-framework", system]);
        if (dependencyPlan.BundleInputs.Any(b => b.Sign))
            frameworkArgs.AddRange(["-rpath", "@executable_path/Frameworks"]);
        var frameworkFile = Path.Combine(output, "apple-frameworks.args");
        File.WriteAllLines(frameworkFile, frameworkArgs);
        var sdk = (await Capture("xcrun", ["--sdk", appleSdk, "--show-sdk-path"])).Trim();
        var obj = Path.Combine(output, "Plugins.o");
        var swiftArgs = new List<string>
        {
            "swiftc",
            "-swift-version",
            "5",
            "-parse-as-library",
            "-whole-module-optimization",
            "-O",
            "-emit-object",
            "-target",
            triple,
            "-sdk",
            sdk,
        };
        foreach (var dir in frameworkDirs)
            swiftArgs.AddRange(["-F", dir]);
        swiftArgs.AddRange(sources);
        swiftArgs.AddRange(["-o", obj]);
        await CachedNativeBuild(
            "Swift plugins",
            Path.Combine(output, "plugins.stamp.json"),
            [
                .. sources,
                frameworkFile,
                .. dependencyPlan.FrameworkPaths,
                Path.Combine(sdk, "SDKSettings.json"),
            ],
            [obj],
            "xcrun",
            swiftArgs.ToArray()
        );
        return obj;
    }

    private static string[] AppleDependencyInputs(string output)
    {
        var file = Path.Combine(output, "apple-dependencies.json");
        if (!File.Exists(file))
            return [];
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        return json
            .RootElement.GetProperty("frameworkPaths")
            .EnumerateArray()
            .Select(p => p.GetString()!)
            .Concat(
                json.RootElement.GetProperty("bundleInputs")
                    .EnumerateArray()
                    .Select(p => p.GetProperty("source").GetString()!)
            )
            .Distinct()
            .ToArray();
    }

    private static string[] AppleFrameworkLink(string output)
    {
        var file = Path.Combine(output, "apple-frameworks.args");
        return File.Exists(file)
            ? File.ReadAllLines(file).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
            : [];
    }
}
