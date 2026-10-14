using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class PluginScaffold
{
    internal static string Create(
        string output,
        string name,
        bool managed,
        string frameworkProject,
        string? template = null
    )
    {
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$"))
            throw new ArgumentException(
                "Plugin name must be a C# namespace (letters, digits, underscores and dots)."
            );
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException(
                "Plugin output must be empty; existing sources are never overwritten."
            );
        var symbol = name.Replace(".", "");
        var entry = symbol + "Plugin";
        foreach (var platform in new[] { "Android", "iOS", "Mac", "Windows", "Linux" })
            Directory.CreateDirectory(Path.Combine(output, "Platform", platform));
        void Write(string file, string text) => File.WriteAllText(Path.Combine(output, file), text);
        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement(
                "PropertyGroup",
                new XElement(
                    managed ? "TargetFrameworks" : "TargetFramework",
                    managed
                        ? "net10.0;net10.0-android;net10.0-ios;net10.0-macos;net10.0-windows10.0.19041.0"
                        : "net9.0"
                ),
                new XElement("RootNamespace", name),
                new XElement("ImplicitUsings", "enable"),
                new XElement("Nullable", "enable"),
                new XElement("AllowUnsafeBlocks", "true"),
                new XElement("IsAotCompatible", "true"),
                new XElement("DotNativePluginKind", managed ? "Managed" : "Native"),
                managed ? null : new XElement("DotNativePluginEntry", entry)
            ),
            new XElement(
                "ItemGroup",
                new XElement(
                    "ProjectReference",
                    new XAttribute(
                        "Include",
                        "$(DotNativeSourceRoot)/src/DotNative/DotNative.csproj"
                    ),
                    new XAttribute(
                        "Condition",
                        "Exists('$(DotNativeSourceRoot)/src/DotNative/DotNative.csproj')"
                    )
                )
            ),
            new XElement(
                "ItemGroup",
                new XElement(
                    "None",
                    new XAttribute("Update", "Platform/**/*;buildTransitive/**/*"),
                    new XAttribute("Pack", "true"),
                    new XAttribute("PackagePath", "%(Identity)")
                )
            )
        );
        project.AddFirst(
            new XElement(
                "Import",
                new XAttribute("Project", "dotnative.local.props"),
                new XAttribute("Condition", "Exists('dotnative.local.props')")
            )
        );
        project.Add(
            new XElement(
                "ItemGroup",
                new XElement("Compile", new XAttribute("Remove", "example/**/*.cs")),
                new XElement("None", new XAttribute("Remove", "example/**/*")),
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "DotNative"),
                    new XAttribute("Version", "0.1.0-beta.1"),
                    new XAttribute(
                        "Condition",
                        "!Exists('$(DotNativeSourceRoot)/src/DotNative/DotNative.csproj')"
                    )
                )
            )
        );
        var local = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "PropertyGroup",
                    new XElement(
                        "DotNativeSourceRoot",
                        Path.GetFullPath(
                            Path.Combine(Path.GetDirectoryName(frameworkProject)!, "../..")
                        )
                    )
                )
            )
        );
        local.Save(Path.Combine(output, "dotnative.local.props"));
        File.WriteAllText(
            Path.Combine(output, "dotnative.local.props.example"),
            "<Project>\n  <PropertyGroup>\n    <DotNativeSourceRoot>/path/to/DotNative</DotNativeSourceRoot>\n  </PropertyGroup>\n</Project>\n"
        );
        var path = Path.Combine(output, name + ".csproj");
        if (managed)
        {
            project.Add(
                new XElement("PropertyGroup", new XElement("EnableWindowsTargeting", "true"))
            );
            foreach (
                var (platform, framework) in new[]
                {
                    ("android", "net10.0-android"),
                    ("ios", "net10.0-ios"),
                    ("macos", "net10.0-macos"),
                    ("windows", "net10.0-windows10.0.19041.0"),
                    ("linux", "net10.0"),
                }
            )
                project.Add(
                    new XElement(
                        "PropertyGroup",
                        new XAttribute(
                            "Condition",
                            $"'$(DotNativeBindingsPlatform)' == '{platform}'"
                        ),
                        new XElement("TargetFrameworks", framework)
                    )
                );
            project.Add(
                new XElement("Import", new XAttribute("Project", "PlatformSources.targets"))
            );
            Write(
                "PlatformSources.targets",
                """
<Project>
  <PropertyGroup>
    <DefineConstants Condition="'$(DotNativeBindingsPlatform)' == 'linux'">$(DefineConstants);DOTNATIVE_LINUX</DefineConstants>
    <_PluginPlatform Condition="$(TargetFramework.Contains('-android'))">Android</_PluginPlatform>
    <_PluginPlatform Condition="$(TargetFramework.Contains('-ios'))">iOS</_PluginPlatform>
    <_PluginPlatform Condition="$(TargetFramework.Contains('-macos'))">Mac</_PluginPlatform>
    <_PluginPlatform Condition="$(TargetFramework.Contains('-windows'))">Windows</_PluginPlatform>
    <_PluginPlatform Condition="'$(TargetFramework)' == 'net10.0' and '$(DotNativeBindingsPlatform)' == 'linux'">Linux</_PluginPlatform>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="Platform/**/*.cs" />
    <Compile Include="Platform/$(_PluginPlatform)/**/*.cs" Condition="'$(_PluginPlatform)' != ''" />
    <Compile Remove="Unsupported.cs" Condition="'$(_PluginPlatform)' != ''" />
  </ItemGroup>
  <Target Name="ReportAndroidBindingsAot" BeforeTargets="CoreCompile" Condition="'$(_PluginPlatform)' == 'Android' and '$(PublishAot)' == 'true'">
    <Warning Code="DNPLUGIN001" Text="C# Android platform bindings with NativeAOT are experimental. Validate native callbacks, lifecycle and trimming on devices." />
  </Target>
</Project>
"""
            );
            Write(
                "NativeService.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read();
}
"""
            );
            Write(
                "Unsupported.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read() => throw new PlatformNotSupportedException("Build this plugin for a native platform; a remote preview cannot call a device's SDK bindings.");
}
"""
            );
            Write(
                "Platform/Android/NativeService.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read() => $"Android: {Android.OS.Build.Model}";
}
"""
            );
            Write(
                "Platform/iOS/NativeService.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read() => $"iOS: {UIKit.UIDevice.CurrentDevice.SystemVersion}";
}
"""
            );
            Write(
                "Platform/Mac/NativeService.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read() => $"macOS: {Foundation.NSProcessInfo.ProcessInfo.OperatingSystemVersionString}";
}
"""
            );
            Write(
                "Platform/Windows/NativeService.cs",
                $$"""
namespace {{name}};
public sealed partial class NativeService
{
    public partial string Read() => $"Windows: {Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily}";
}
"""
            );
            Write(
                "Platform/Linux/NativeService.cs",
                $$"""
using System.Runtime.InteropServices;
namespace {{name}};
public sealed partial class NativeService
{
    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();
    public partial string Read() => $"Linux: uid={GetUid()}";
}
"""
            );
        }
        else
        {
            var channelName = "dotnative.plugins." + name.ToLowerInvariant().Replace('_', '-');
            Write(
                "NativeService.cs",
                $$"""
using DotNative.Plugins;
namespace {{name}};
public sealed class NativeService(IPlatformChannels channels)
{
    public async Task<string> ReadAsync(CancellationToken cancellationToken = default) =>
        (string)(await channels.Get("{{channelName}}").InvokeAsync("read", cancellationToken: cancellationToken))!;
}
"""
            );
            Write(
                "Platform/Android/" + entry + ".kt",
                $$"""
package com.dotnative.plugins
import android.app.Activity
fun {{entry}}(activity: Activity) {
    NativeChannels.channel("{{channelName}}").handle("read") { _, reply ->
        reply.success("Android: " + android.os.Build.MODEL)
    }
}
"""
            );
            foreach (var platform in new[] { "iOS", "Mac" })
                Write(
                    $"Platform/{platform}/{entry}.swift",
                    $$"""
import {{(platform == "iOS" ? "UIKit" : "AppKit")}}
@MainActor enum {{entry}} {
    static func register() {
        NativeChannels.channel("{{channelName}}").handle("read") { _, reply in
            reply.success(.string("{{(platform == "iOS" ? "iOS" : "macOS")}}: " + {{(
                        platform == "iOS"
                            ? "UIDevice.current.systemVersion"
                            : "ProcessInfo.processInfo.operatingSystemVersionString"
                    )}}))
        }
    }
}
"""
                );
            foreach (var platform in new[] { "Windows", "Linux" })
                Write(
                    $"Platform/{platform}/{entry}.cpp",
                    $$"""
#include <DotNativePlugins.hpp>
{{(platform == "Windows" ? "#include <windows.h>" : "#include <sys/utsname.h>\n#include <gtk/gtk.h>")}}
void {{entry}}(const dotnative::PluginRegistrar& registrar) {
    auto window = registrar.NativeWindow();
    registrar.Channel("{{channelName}}").Handle("read", [window](const dotnative::Value&, std::shared_ptr<dotnative::Reply> reply) {
        if (!window || !{{(platform == "Windows" ? "IsWindow(static_cast<HWND>(window))" : "GTK_IS_WINDOW(window)")}}) { reply->Error("no_window", "Native window is unavailable"); return; }
        {{(
                        platform == "Windows"
                            ? "reply->Success(\"Windows: pid=\" + std::to_string(GetCurrentProcessId()));"
                            : "utsname info{}; if (uname(&info) != 0) { reply->Error(\"os_error\", \"uname failed\"); return; }\n        reply->Success(std::string(info.sysname) + \": \" + info.release);"
                    )}}
    });
}
"""
                );
            Directory.CreateDirectory(Path.Combine(output, "buildTransitive"));
            var targets = new XElement("Project");
            foreach (
                var (rid, folder, pattern) in new[]
                {
                    ("ios", "iOS", "*.swift"),
                    ("osx-", "Mac", "*.swift"),
                    ("linux-bionic-", "Android", "*.kt"),
                    ("android-", "Android", "*.kt"),
                    ("win-", "Windows", "*.cpp"),
                    ("linux-", "Linux", "*.cpp"),
                }
            )
                targets.Add(
                    new XElement(
                        "ItemGroup",
                        new XAttribute(
                            "Condition",
                            $"$([System.String]::Copy('$(RuntimeIdentifier)').StartsWith('{rid}'))"
                                + (
                                    folder == "Linux"
                                        ? " and !$([System.String]::Copy('$(RuntimeIdentifier)').StartsWith('linux-bionic-'))"
                                        : ""
                                )
                        ),
                        new XElement(
                            "DotNativeNativeSource",
                            new XAttribute(
                                "Include",
                                $"$(MSBuildThisFileDirectory)../Platform/{folder}/**/{pattern}"
                            )
                        ),
                        new XElement("DotNativePluginEntry", new XAttribute("Include", entry))
                    )
                );
            new XDocument(targets).Save(Path.Combine(output, "buildTransitive", name + ".targets"));
        }
        new XDocument(project).Save(path);
        Write(
            "Registration.cs",
            $$"""
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace {{name}};
public static class Registration
{
    public static IServiceCollection Add{{symbol}}(this IServiceCollection services)
    {
        services.TryAddSingleton<NativeService>();
        return services;
    }
}
"""
        );
        Write(".gitignore", "bin/\nobj/\nartifacts/\ndotnative.local.props\n");
        if (template is not null)
            PluginExample.Create(output, name, symbol, managed, template);
        return path;
    }
}
