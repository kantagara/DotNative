using System.Text.Json;
using System.Xml.Linq;

internal static partial class Preview
{
    private static async Task<string> WriteManagedProject(
        BindingsProject app,
        string platform,
        string dotnet,
        string output,
        string nativeLibrary,
        IReadOnlyList<string> androidLibraries
    )
    {
        using var evaluation = JsonDocument.Parse(
            await Capture(
                dotnet,
                [
                    "msbuild",
                    app.Project,
                    "-nologo",
                    "-p:Configuration=Release",
                    "-p:TargetFramework=net10.0",
                    "-p:DotNativeDevelopment=true",
                    "-getProperty:LangVersion,DefineConstants,ApplicationTitle,ApplicationVersion,ApplicationDisplayVersion,CodesignEntitlements",
                    "-getItem:Compile,ProjectReference,PackageReference,EmbeddedResource,Analyzer,AdditionalFiles",
                ]
            )
        );
        var items = evaluation.RootElement.GetProperty("Items");
        var evaluatedProperties = evaluation.RootElement.GetProperty("Properties");
        var android = platform == "android";
        var tfm = android ? "net10.0-android" : $"net10.0-{platform}26.5";
        var constants = evaluatedProperties.GetProperty("DefineConstants").GetString();
        var language = evaluatedProperties.GetProperty("LangVersion").GetString();
        string Property(string key, string fallback) =>
            evaluatedProperties.GetProperty(key).GetString() is { Length: > 0 } value
                ? value
                : fallback;
        var properties = new XElement(
            "PropertyGroup",
            new XElement("TargetFramework", tfm),
            new XElement("AssemblyName", app.Name),
            new XElement("RootNamespace", app.Name),
            new XElement("OutputType", "Exe"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("EnableDefaultAndroidItems", "false"),
            new XElement("EnableDefaultEmbeddedResourceItems", "false"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("AllowUnsafeBlocks", "true"),
            new XElement("LangVersion", string.IsNullOrEmpty(language) ? "latest" : language),
            new XElement(
                "DefineConstants",
                constants + ";DOTNATIVE_BINDINGS;DOTNATIVE_" + platform.ToUpperInvariant()
            ),
            new XElement("PublishAot", "false"),
            new XElement("PublishAotUsingRuntimePack", "false"),
            new XElement("JsonSerializerIsReflectionEnabledByDefault", "true"),
            new XElement("InvariantGlobalization", "false"),
            new XElement("ApplicationId", app.ApplicationId),
            new XElement("ApplicationTitle", Property("ApplicationTitle", app.Name)),
            new XElement("ApplicationVersion", Property("ApplicationVersion", "1")),
            new XElement("ApplicationDisplayVersion", Property("ApplicationDisplayVersion", "0.1")),
            new XElement(
                "SupportedOSPlatformVersion",
                android ? "30.0"
                    : platform == "ios" ? "16.0"
                    : "13.0"
            )
        );
        if (android)
        {
            File.Copy(
                Path.Combine(app.Directory, "Platform/Android/app/src/main/AndroidManifest.xml"),
                Path.Combine(output, "AndroidManifest.xml"),
                overwrite: true
            );
            properties.Add(
                new XElement("PublishTrimmed", "false"),
                new XElement("AndroidLinkMode", "None"),
                new XElement("RunAOTCompilation", "false"),
                new XElement("AndroidEnableProfiledAot", "false"),
                new XElement("AndroidEnableR8", "false"),
                // Use runtime JNI registration for the managed bootstrap. Compiled
                // marshal stubs left Application.onCreate unresolved on device.
                new XElement("AndroidEnableMarshalMethods", "false"),
                new XElement("AndroidPackageFormat", "apk"),
                new XElement("EmbedAssembliesIntoApk", "true"),
                new XElement("AndroidManifest", "AndroidManifest.xml")
            );
        }
        else
        {
            // Apple SDKs require their linker pipeline even when no assemblies are trimmed.
            properties.Add(
                new XElement("TrimMode", "copy"),
                new XElement("StartupObject", "DotNative.Generated.ManagedBootstrap"),
                new XElement("CodesignKey", "-"),
                new XElement("EnableCodeSigning", "false")
            );
            if (platform == "ios")
                properties.Add(new XElement("UseInterpreter", "true"));
            var plist = Path.Combine(output, "Info.plist");
            WritePlist(
                plist,
                app.Name,
                app.ApplicationId,
                platform == "ios",
                Path.Combine(
                    app.Directory,
                    "Platform",
                    platform == "ios" ? "iOS" : "Mac",
                    "Info.plist"
                )
            );
            // Standalone identity/version must not inherit the preview defaults.
            var document = XDocument.Load(plist);
            var dictionary = document.Root!.Element("dict")!;
            foreach (
                var (key, value) in new[]
                {
                    ("CFBundleName", Property("ApplicationTitle", app.Name)),
                    ("CFBundleVersion", Property("ApplicationVersion", "1")),
                    ("CFBundleShortVersionString", Property("ApplicationDisplayVersion", "0.1")),
                }
            )
                dictionary
                    .Elements("key")
                    .Single(x => x.Value == key)
                    .ElementsAfterSelf()
                    .First()
                    .Value = value;
            document.Save(plist);
            var entitlements = Property("CodesignEntitlements", "");
            if (entitlements.Length > 0)
            {
                var source = Path.GetFullPath(entitlements, app.Directory);
                if (!File.Exists(source))
                    throw new FileNotFoundException("Configured entitlements are missing.", source);
                properties.Add(new XElement("CodesignEntitlements", source));
            }
            if (platform == "ios")
            {
                // Let the Apple SDK embed simulator entitlements in __TEXT. Passing
                // restricted iOS entitlements to macOS ad-hoc codesign prevents launch.
                var simulatorEntitlements = WriteSimulatorEntitlements(output, app.ApplicationId);
                if (entitlements.Length > 0)
                {
                    var original = XDocument.Load(Path.GetFullPath(entitlements, app.Directory));
                    var simulator = XDocument.Load(simulatorEntitlements);
                    var destination = simulator.Root!.Element("dict")!;
                    foreach (var key in original.Root!.Element("dict")!.Elements("key"))
                        if (
                            !destination
                                .Elements("key")
                                .Any(existing => existing.Value == key.Value)
                        )
                            destination.Add(
                                new XElement(key),
                                new XElement(key.ElementsAfterSelf().First())
                            );
                    simulator.Save(simulatorEntitlements);
                }
                properties.Add(
                    new XElement(
                        "CodesignEntitlements",
                        new XAttribute(
                            "Condition",
                            "$(RuntimeIdentifier.StartsWith('iossimulator-'))"
                        ),
                        simulatorEntitlements
                    )
                );
            }
            properties.Add(new XElement("ApplicationManifest", plist));
        }
        var sources = new XElement("ItemGroup");
        foreach (
            var item in items
                .GetProperty("Compile")
                .EnumerateArray()
                .Where(item =>
                    item.GetProperty("FullPath").GetString()
                    != Path.Combine(Root, "packaging/DotNative.App/tools/AppEntry.cs")
                )
        )
            sources.Add(
                new XElement(
                    "Compile",
                    new XAttribute("Include", item.GetProperty("FullPath").GetString()!)
                )
            );
        foreach (var item in items.GetProperty("EmbeddedResource").EnumerateArray())
        {
            var resource = new XElement(
                "EmbeddedResource",
                new XAttribute("Include", item.GetProperty("FullPath").GetString()!)
            );
            foreach (
                var key in new[] { "LogicalName", "ManifestResourceName", "Culture", "WithCulture" }
            )
                if (
                    item.TryGetProperty(key, out var value)
                    && !string.IsNullOrEmpty(value.GetString())
                )
                    resource.Add(new XElement(key, value.GetString()));
            sources.Add(resource);
        }
        sources.Add(
            new XElement(
                "Compile",
                new XAttribute("Include", Path.Combine(Root, "eng/ManagedBootstrap.cs"))
            ),
            new XElement(
                "Compile",
                new XAttribute(
                    "Include",
                    Path.Combine(Root, "packaging/DotNative.App/tools/AppEntry.cs")
                )
            ),
            new XElement("Compile", new XAttribute("Include", Path.Combine(output, "Entry.g.cs")))
        );
        File.WriteAllText(
            Path.Combine(output, "Entry.g.cs"),
            "namespace DotNative.Generated { internal static partial class AppEntry { private static partial System.Threading.Tasks.Task InvokeMain() => DotNative.Hosting.MainEntry.Invoke(global::"
                + app.Startup
                + ".Main); } }\n"
        );
        var references = new XElement("ItemGroup");
        foreach (var kind in new[] { "Analyzer", "AdditionalFiles" })
        foreach (var item in items.GetProperty(kind).EnumerateArray())
            references.Add(
                new XElement(
                    kind,
                    new XAttribute("Include", item.GetProperty("FullPath").GetString()!)
                )
            );
        if (android)
        {
            // Keep the portable application's namespace imports unchanged.
            references.Add(
                new XElement(
                    "Using",
                    new XAttribute("Remove", "Android.App;Android.Widget;Android.OS.Bundle")
                )
            );
        }
        var framework = Path.Combine(Root, "src/DotNative/DotNative.csproj");
        var paths = items
            .GetProperty("ProjectReference")
            .EnumerateArray()
            .Select(x => x.GetProperty("FullPath").GetString()!)
            .Append(framework)
            .Distinct(StringComparer.Ordinal);
        if (app.Startup == "Counter.Program")
            paths = paths.Append(
                Path.Combine(Root, "plugins/DotNative.BindingsProbe/DotNative.BindingsProbe.csproj")
            );
        foreach (var path in paths)
        {
            var original = items
                .GetProperty("ProjectReference")
                .EnumerateArray()
                .FirstOrDefault(x => x.GetProperty("FullPath").GetString() == path);
            references.Add(
                new XElement(
                    "ProjectReference",
                    new XAttribute("Include", path),
                    new XAttribute(
                        "GlobalPropertiesToRemove",
                        "RuntimeIdentifier;PublishAot;PublishAotUsingRuntimePack;NativeLib;SelfContained"
                    ),
                    original.ValueKind != JsonValueKind.Undefined
                    && original.TryGetProperty("ReferenceOutputAssembly", out var referenceOutput)
                        ? new XAttribute("ReferenceOutputAssembly", referenceOutput.GetString()!)
                        : null,
                    original.ValueKind != JsonValueKind.Undefined
                    && original.TryGetProperty("AdditionalProperties", out var additional)
                        ? new XAttribute("AdditionalProperties", additional.GetString()!)
                        : null
                )
            );
        }
        foreach (var item in items.GetProperty("PackageReference").EnumerateArray())
        {
            var id = item.GetProperty("Identity").GetString()!;
            if (id is "DotNative" or "DotNative.App")
                continue;
            var package = new XElement("PackageReference", new XAttribute("Include", id));
            foreach (
                var key in new[]
                {
                    "Version",
                    "PrivateAssets",
                    "IncludeAssets",
                    "ExcludeAssets",
                    "VersionOverride",
                }
            )
                if (
                    item.TryGetProperty(key, out var value)
                    && !string.IsNullOrEmpty(value.GetString())
                )
                    package.Add(new XAttribute(key, value.GetString()!));
            references.Add(package);
        }
        if (android)
        {
            references.Add(
                new XElement("AndroidLibrary", new XAttribute("Remove", "**/*.aar;**/*.jar")),
                new XElement("AndroidJavaSource", new XAttribute("Remove", "**/*.java")),
                new XElement("AndroidNativeLibrary", new XAttribute("Remove", "**/*.so"))
            );
            references.Add(
                new XElement(
                    "AndroidNativeLibrary",
                    new XAttribute("Include", nativeLibrary),
                    new XElement("Abi", "arm64-v8a")
                )
            );
            foreach (var library in androidLibraries)
                references.Add(
                    new XElement(
                        "AndroidLibrary",
                        new XAttribute("Include", library),
                        new XElement("Bind", "false")
                    )
                );
        }
        else
            references.Add(
                new XElement(
                    "NativeReference",
                    new XAttribute("Include", nativeLibrary),
                    new XElement("Kind", "Static"),
                    new XElement("ForceLoad", "true"),
                    new XElement("SmartLink", "false"),
                    new XElement("IsCxx", "false"),
                    new XElement(
                        "Frameworks",
                        (platform == "ios" ? "UIKit" : "AppKit")
                            + " Foundation QuartzCore CoreGraphics CoreText"
                    ),
                    new XElement(
                        "LinkerFlags",
                        "-ObjC -liconv -L/usr/lib/swift -Wl,-export_dynamic "
                            // The Apple SDK supplies an exported-symbols allowlist. Keeping
                            // these functions linked (-u) does not make them visible to dlsym.
                            + "-Wl,-u,_flush_commands,-u,_dotnative_plugins_reset,-u,_dotnative_send_plugin,-u,_dotnative_poll_plugin,-u,_dotnative_poll_event,-u,_dotnative_open_activation "
                            + "-Wl,-exported_symbol,_flush_commands,-exported_symbol,_dotnative_plugins_reset,-exported_symbol,_dotnative_send_plugin,-exported_symbol,_dotnative_poll_plugin,-exported_symbol,_dotnative_poll_event,-exported_symbol,_dotnative_open_activation"
                    )
                )
            );
        var project = Path.Combine(output, app.Name + ".csproj");
        new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                properties,
                sources,
                references,
                android
                    ? null
                    : new XElement(
                        "Import",
                        new XAttribute(
                            "Project",
                            Path.Combine(output, "apple-dependencies.targets")
                        )
                    )
            )
        ).Save(project);
        return project;
    }
}
