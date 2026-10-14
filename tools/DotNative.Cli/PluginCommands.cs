internal static partial class Preview
{
    private static async Task<int> RunPluginCommand(string[] args)
    {
        if (args.Length > 0 && args[0] == "run")
        {
            if (args.Length < 4 || args[1].StartsWith('-'))
                throw new ArgumentException(
                    "Use: dotnative plugin run <platform> --project <plugin-directory> [--device id] [--team id]"
                );
            var index = Array.IndexOf(args, "--project");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing --project.");
            var directory = Path.GetFullPath(args[index + 1]);
            if (File.Exists(directory))
                directory = Path.GetDirectoryName(directory)!;
            var project = Directory.GetFiles(directory, "*.csproj").Single();
            var example = Path.Combine(directory, "example");
            if (!Directory.Exists(example))
                throw new IOException(
                    "This plugin has no example app. Create it with dotnative plugin new, or add an example directory."
                );
            var managed = System
                .Xml.Linq.XDocument.Load(project)
                .Descendants("DotNativePluginKind")
                .Any(element => element.Value == "Managed");
            var forwarded = args[1..].ToList();
            forwarded[index] = example;
            forwarded.Insert(0, "run");
            forwarded.Add(managed ? "--bindings" : "--hot-reload");
            return await Run(forwarded.ToArray());
        }
        if (args.Length == 0 || args[0] is not ("new" or "build"))
            throw new ArgumentException(
                "Use: dotnative plugin new --name Name --output path [--kind native|managed], or plugin build --project path --platform mac|windows|linux"
            );
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Plugin options need unique names and values.");
        }
        string Required(string key) =>
            options.TryGetValue(key, out var value) && value.Length > 0
                ? value
                : throw new ArgumentException("Missing " + key);
        if (args[0] == "new")
        {
            if (options.Keys.Any(k => k is not ("--name" or "--output" or "--kind")))
                throw new ArgumentException("Unknown plugin new option.");
            var kind = options.GetValueOrDefault("--kind", "native");
            if (kind is not ("native" or "managed"))
                throw new ArgumentException("Plugin kind must be native or managed.");
            var project = PluginScaffold.Create(
                Required("--output"),
                Required("--name"),
                kind == "managed",
                Path.Combine(Root, "src/DotNative/DotNative.csproj"),
                Path.Combine(Root, "templates/DotNative.App")
            );
            Console.WriteLine($"Created {kind} plugin: {project}");
            Console.WriteLine(
                $"Example: dotnative plugin run ios --project \"{Path.GetDirectoryName(project)}\""
            );
            if (kind == "managed")
                Console.WriteLine(
                    "C# Android SDK bindings with NativeAOT are experimental (DNPLUGIN001). Use a platform-targeted bindings host; remote mobile preview cannot execute native C# bindings."
                );
            return 0;
        }
        if (options.Keys.Any(k => k is not ("--project" or "--platform")))
            throw new ArgumentException("Unknown plugin build option.");
        var path = Path.GetFullPath(Required("--project"));
        if (Directory.Exists(path))
            path = Directory.GetFiles(path, "*.csproj").Single();
        var platform = Required("--platform");
        if (platform is not ("mac" or "windows" or "linux"))
            throw new ArgumentException("Build desktop plugins on mac, windows or linux.");
        if (
            (platform == "mac" && !OperatingSystem.IsMacOS())
            || (platform == "windows" && !OperatingSystem.IsWindows())
            || (platform == "linux" && !OperatingSystem.IsLinux())
        )
            throw new NotSupportedException("Build this plugin on its target OS.");
        var plugins = await DiscoverPlugins(path);
        if (
            platform == "mac"
            && !plugins.Any(p =>
                p.Entry.Length != 0
                && Directory.Exists(Path.Combine(p.Directory, "Platform/Mac"))
                && Directory
                    .GetFiles(
                        Path.Combine(p.Directory, "Platform/Mac"),
                        "*.swift",
                        SearchOption.AllDirectories
                    )
                    .Any(f => !f.EndsWith(".g.swift", StringComparison.Ordinal))
            )
        )
            throw new InvalidOperationException(
                "No native plugin implementation found for this platform."
            );
        var output = Path.Combine(Path.GetDirectoryName(path)!, "obj", "DotNativePlugin", platform);
        Directory.CreateDirectory(output);
        var result =
            platform == "mac"
                ? await BuildApplePlugins(plugins, output, false, "mac")
                : await BuildDesktopPlugins(plugins, output, platform);
        if (result is null)
            throw new InvalidOperationException(
                "No native plugin implementation found for this platform."
            );
        Console.WriteLine($"Native plugin link input: {result}");
        return 0;
    }
}
