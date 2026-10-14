using System.Security.Cryptography;
using System.Text;

internal static partial class Preview
{
    // Plugins are linked into the renderer, so service calls execute beside the
    // native window instead of inside the remote managed preview process.
    private static async Task<string?> BuildDesktopPlugins(
        List<PluginProject> plugins,
        string output,
        string platform
    )
    {
        var directory = platform == "windows" ? "Windows" : "Linux";
        var active = plugins
            .Where(p => p.Entry.Length != 0)
            .Select(p => new
            {
                Plugin = p,
                Folder = Path.Combine(p.Directory, "Platform", directory),
            })
            .Where(p => Directory.Exists(p.Folder))
            .Select(p => new
            {
                p.Plugin,
                p.Folder,
                Sources = Directory
                    .GetFiles(p.Folder, "*", SearchOption.AllDirectories)
                    .Where(f => Path.GetExtension(f) is ".cpp" or ".cc")
                    .ToArray(),
            })
            .Where(p => p.Sources.Length > 0)
            .ToArray();
        if (active.Length == 0)
            return null;
        var build = Path.Combine(output, "native-plugins");
        Directory.CreateDirectory(build);
        var sdk = Path.Combine(Root, "native/plugins/cpp");
        var registration = Path.Combine(build, "Registration.cpp");
        WriteIfChanged(
            registration,
            "#include <DotNativePlugins.hpp>\n"
                + string.Join(
                    "\n",
                    active.Select(p => $"void {p.Plugin.Entry}(const dotnative::PluginRegistrar&);")
                )
                + "\nextern \"C\" int32_t dotnative_register_plugins(void* window) {\ntry {\n dotnative::PluginRegistrar registrar(window);\n"
                + string.Join("\n", active.Select(p => $" {p.Plugin.Entry}(registrar);"))
                + "\nreturn 0;\n} catch (...) { return -1; }\n}\n"
        );
        var gtkFlags =
            platform == "linux"
                ? (await Capture("pkg-config", ["--cflags", "gtk4"])).Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries
                )
                : [];
        var archive = Path.Combine(
            build,
            platform == "windows" ? "dotnative_plugins.lib" : "libdotnative_plugins.a"
        );
        var compilerName =
            platform == "windows" ? "cl" : Environment.GetEnvironmentVariable("CXX") ?? "c++";
        var compilerInfo = await CommandOutput(
            compilerName,
            platform == "windows" ? [] : ["--version"]
        );
        var cache = new NativeBuildCache(
            Path.Combine(build, "archive.stamp.json"),
            [sdk, registration, .. active.Select(plugin => plugin.Folder)],
            [archive],
            [
                platform,
                compilerName,
                compilerInfo.Output,
                compilerInfo.Error,
                Environment.GetEnvironmentVariable("INCLUDE") ?? "",
                Environment.GetEnvironmentVariable("LIB") ?? "",
                .. gtkFlags,
            ]
        );
        if (cache.IsCurrent)
        {
            Console.WriteLine("[build] Desktop plugins: unchanged; using cached archive.");
            return archive;
        }
        var objects = new List<string>();
        foreach (var source in active.SelectMany(p => p.Sources).Append(registration))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24];
            var obj = Path.Combine(build, hash + (platform == "windows" ? ".obj" : ".o"));
            if (platform == "windows")
                await Exec(
                    "cl",
                    [
                        "/nologo",
                        "/std:c++17",
                        "/EHsc",
                        "/MD",
                        "/O2",
                        "/c",
                        "/I" + sdk,
                        "/Fo" + obj,
                        source,
                    ]
                );
            else
                await Exec(
                    Environment.GetEnvironmentVariable("CXX") ?? "c++",
                    ["-std=c++17", "-O2", "-fPIC", "-I", sdk, .. gtkFlags, "-c", source, "-o", obj]
                );
            objects.Add(obj);
        }
        // Replacing the archive prevents removed plugins from surviving incremental builds.
        File.Delete(archive);
        if (platform == "windows")
            await Exec("lib", ["/nologo", "/OUT:" + archive, .. objects]);
        else
            await Exec("ar", ["rcs", archive, .. objects]);
        cache.Commit();
        return archive;
    }
}
