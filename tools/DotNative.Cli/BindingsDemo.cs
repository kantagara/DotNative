using System.Runtime.InteropServices;
using System.Xml.Linq;

internal static partial class Preview
{
    // Opt-in platform SDK bindings; Android NativeAOT remains experimental.
    private static async Task<int> RunBindingsDemo(string[] args)
    {
        if (
            args.Length == 0
            || args[0] is not ("ios" or "android" or "mac" or "windows" or "linux")
        )
            throw new ArgumentException(
                "Use: dotnative bindings-demo ios|android|mac|windows|linux [--dotnet path] [--device id] [--build-only] [--runtime nativeaot|managed]"
            );
        var platform = args[0] == "mac" ? "macos" : args[0];
        string? device = null;
        string? projectPath = null;
        string? runtimeOption = null;
        string? team = Environment.GetEnvironmentVariable("DOTNATIVE_APPLE_TEAM");
        var sdkRoot = Path.Combine(Root, "artifacts/bindings-integration/dotnet");
        var dotnet = File.Exists(Path.Combine(sdkRoot, "dotnet"))
            ? Path.Combine(sdkRoot, "dotnet")
            : "dotnet";
        bool buildOnly = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--build-only")
            {
                buildOnly = true;
                continue;
            }
            if (
                args[i] is not ("--dotnet" or "--device" or "--project" or "--runtime" or "--team")
                || i + 1 == args.Length
            )
                throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
            var option = args[i++];
            if (option == "--team")
                team = args[i];
            else if (option == "--runtime")
                runtimeOption = args[i];
            else if (option == "--dotnet")
                dotnet = Path.GetFullPath(args[i]);
            else if (option == "--project")
                projectPath = args[i];
            else
                device = args[i];
        }
        var appProject = ReadBindingsProject(projectPath ?? Path.Combine(Root, "Samples/Counter"));
        var runtime = await ResolveRuntime(appProject.Project, dotnet, runtimeOption);
        if (platform is "windows" or "linux")
            return await RunDesktopBindingsDemo(platform, dotnet, buildOnly, appProject, runtime);
        if (runtime == ApplicationRuntime.Managed)
            return await RunManagedBindings(platform, dotnet, buildOnly, appProject, device, team);
        if (args.Contains("--team"))
            throw new ArgumentException(
                "--team is currently supported by managed iOS device builds only."
            );
        using var lease = AcquireLease("bindings-" + platform);
        if (
            !OperatingSystem.IsMacOS()
            || RuntimeInformation.ProcessArchitecture != Architecture.Arm64
        )
            throw new NotSupportedException(
                "This mobile/Apple integration proof currently requires an Apple Silicon Mac."
            );
        if (platform == "ios" && !buildOnly && device is null)
            throw new ArgumentException(
                "Select an iOS simulator with --device <UDID>; use dotnative devices to list them."
            );

        Console.WriteLine(
            "Experimental SDK-bindings integration. Android NativeAOT and the iOS simulator build override are not production support guarantees."
        );
        var rid = platform switch
        {
            "ios" => "iossimulator-arm64",
            "android" => "android-arm64",
            _ => "osx-arm64",
        };
        var rust = platform switch
        {
            "ios" => "aarch64-apple-ios-sim",
            "android" => "aarch64-linux-android",
            _ => "aarch64-apple-darwin",
        };
        if (platform == "android")
            await Exec("bash", ["eng/build-core.sh", "linux-bionic-arm64"]);
        else
            await Exec(
                "cargo",
                [
                    "build",
                    "--release",
                    "--locked",
                    "--manifest-path",
                    "native/core/Cargo.toml",
                    "--target",
                    rust,
                ]
            );

        var tfm = platform == "android" ? "net10.0-android" : $"net10.0-{platform}26.5";
        var pluginOutput = Path.Combine(
            Root,
            "artifacts/bindings-integration/plugins",
            platform,
            appProject.Name
        );
        Directory.CreateDirectory(pluginOutput);
        var plugins = await DiscoverPlugins(appProject.Project);
        var applePlugin =
            platform == "android"
                ? null
                : await BuildApplePlugins(
                    plugins,
                    pluginOutput,
                    false,
                    platform == "macos" ? "mac" : "ios"
                );
        var generatedProject = WriteBindingsProject(appProject, platform, tfm, applePlugin);
        var build = new List<string>
        {
            platform == "macos" ? "publish" : "build",
            generatedProject,
            "-c",
            "Release",
            $"-p:DotNativeBindingsPlatform={platform}",
            $"-p:RuntimeIdentifier={rid}",
            $"-p:DirectoryBuildPropsPath={Path.Combine(Root, "eng/DotNative.Bindings.props")}",
        };
        if (platform == "ios")
            build.Add("-p:_IsPublishing=true");
        if (platform != "android")
            build.Add("-p:EnableCodeSigning=false");
        if (platform == "android")
        {
            var androidSdk =
                Environment.GetEnvironmentVariable("ANDROID_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Android/sdk"
                );
            build.Add($"-p:AndroidSdkDirectory={androidSdk}");
            if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } java)
                build.Add($"-p:JavaSdkDirectory={java}");
        }
        await Exec(dotnet, build.ToArray());
        var output = Path.Combine(
            Root,
            "artifacts/bindings-integration/build",
            platform,
            appProject.Name + "/bin/Release",
            tfm,
            rid
        );
        if (platform == "android")
        {
            // Use the exact Java support assets used by SDK 36.1.69's NativeAOT
            // pipeline. Historical mono.* class names do not imply a Mono VM.
            if (dotnet == "dotnet")
                throw new ArgumentException(
                    "Android staging requires --dotnet with the full SDK executable path."
                );
            sdkRoot = Path.GetDirectoryName(dotnet)!;
            var stage = Path.Combine(
                Root,
                "artifacts/bindings-integration/android-stage",
                appProject.Name
            );
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(Path.Combine(stage, "jniLibs/arm64-v8a"));
            File.Copy(
                Path.Combine(output, "native/" + appProject.Name + ".so"),
                Path.Combine(stage, "jniLibs/arm64-v8a/lib" + appProject.Name + ".so")
            );
            var generated = Path.Combine(
                Root,
                "artifacts/bindings-integration/build/android/" + appProject.Name + "/obj/Release",
                tfm,
                rid,
                "android/src"
            );
            if (
                !File.Exists(
                    Path.Combine(generated, "net/dot/jni/nativeaot/NativeAotRuntimeProvider.java")
                )
            )
                throw new InvalidOperationException(
                    "The Android SDK did not retain its NativeAOT bootstrap source. Refusing to package an app that would crash at launch."
                );
            CopyTree(generated, Path.Combine(stage, "java"));
            foreach (
                var resource in Directory.EnumerateFiles(
                    Path.Combine(stage, "java"),
                    "R.java",
                    SearchOption.AllDirectories
                )
            )
                File.Delete(resource);
            File.Copy(
                Path.Combine(
                    sdkRoot,
                    "packs/Microsoft.Android.Ref.36/36.1.69/ref/net10.0/mono.android.jar"
                ),
                Path.Combine(stage, "mono.android.jar")
            );
            File.Copy(
                Path.Combine(
                    sdkRoot,
                    "packs/Microsoft.Android.Sdk.Darwin/36.1.69/tools/java_runtime_net6.jar"
                ),
                Path.Combine(stage, "java_runtime_net6.jar")
            );
            var javaAssets = Path.Combine(output, "dotnative-java");
            if (Directory.Exists(javaAssets))
                foreach (var jar in Directory.GetFiles(javaAssets, "*.jar"))
                    File.Copy(jar, Path.Combine(stage, Path.GetFileName(jar)), overwrite: true);
            var runner = Path.Combine(pluginOutput, "Android");
            CopyTree(Path.Combine(appProject.Directory, "Platform/Android"), runner);
            StageAndroidPlugins(
                plugins,
                runner,
                Path.Combine(appProject.Directory, "Platform/Android/app/build.gradle")
            );
            var gradle = Path.Combine(runner, "app/build.gradle");
            File.WriteAllText(
                gradle,
                System.Text.RegularExpressions.Regex.Replace(
                    File.ReadAllText(gradle),
                    "applicationId '[^']+'",
                    $"applicationId '{appProject.ApplicationId}'"
                )
            );
            var manifest = XDocument.Load(Path.Combine(runner, "app/src/main/AndroidManifest.xml"));
            XNamespace android = "http://schemas.android.com/apk/res/android";
            manifest
                .Root!.Element("application")!
                .Add(
                    new XElement(
                        "provider",
                        new XAttribute(
                            android + "name",
                            "net.dot.jni.nativeaot.NativeAotRuntimeProvider"
                        ),
                        new XAttribute(
                            android + "authorities",
                            appProject.ApplicationId + ".bindings-init"
                        ),
                        new XAttribute(android + "exported", "false"),
                        new XAttribute(android + "initOrder", "1999999999")
                    )
                );
            manifest.Save(Path.Combine(stage, "AndroidManifest.xml"));
            await Exec(
                "bash",
                [
                    "./gradlew",
                    "--console=plain",
                    "assembleDebug",
                    $"-PdotNativeBindingsDir={stage}",
                    $"-PdotNativeLibraryName={appProject.Name}",
                ],
                runner
            );
            var apk = Path.Combine(runner, "app/build/outputs/apk/debug/app-debug.apk");
            Console.WriteLine($"NativeAOT + Rust APK: {apk}");
            if (buildOnly)
                return 0;
            var androidSdk =
                Environment.GetEnvironmentVariable("ANDROID_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Android/sdk"
                );
            var adb = Path.Combine(androidSdk, "platform-tools/adb");
            string[] selector = device is null ? [] : ["-s", device];
            await Exec(adb, [.. selector, "install", "-r", apk]);
            await Exec(adb, [.. selector, "shell", "am", "force-stop", appProject.ApplicationId]);
            await Exec(
                adb,
                [
                    .. selector,
                    "shell",
                    "am",
                    "start",
                    "-n",
                    appProject.ApplicationId + "/com.dotnative.runtime.NativeHost",
                ]
            );
            return 0;
        }

        var app = Path.Combine(output, appProject.Name + ".app");
        // SDK-produced helper dylibs must be signed individually for local runs.
        foreach (
            var library in Directory.EnumerateFiles(app, "*.dylib", SearchOption.AllDirectories)
        )
            await Exec("codesign", ["--force", "--sign", "-", library]);
        await Exec("codesign", ["--force", "--sign", "-", app]);
        Console.WriteLine($"NativeAOT + Rust bundle: {app}");
        if (buildOnly)
            return 0;
        if (platform == "ios")
        {
            await Exec("xcrun", ["simctl", "bootstatus", device!, "-b"]);
            await Exec("xcrun", ["simctl", "install", device!, app]);
            await Exec(
                "xcrun",
                [
                    "simctl",
                    "launch",
                    "--terminate-running-process",
                    "--console-pty",
                    device!,
                    appProject.ApplicationId,
                ]
            );
        }
        else
            await Exec(Path.Combine(app, "Contents/MacOS", appProject.Name), []);
        return 0;
    }

    private static async Task<int> RunDesktopBindingsDemo(
        string platform,
        string dotnet,
        bool buildOnly,
        BindingsProject appProject,
        ApplicationRuntime runtime
    )
    {
        if (
            (platform == "windows" && !OperatingSystem.IsWindows())
            || (platform == "linux" && !OperatingSystem.IsLinux())
        )
            throw new NotSupportedException("Run the desktop bindings demo on its target OS.");
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new NotSupportedException("Use ARM64 or x64."),
        };
        var rid = $"{(platform == "windows" ? "win" : "linux")}-{arch}";
        var rust =
            $"{(arch == "arm64" ? "aarch64" : "x86_64")}-{(platform == "windows" ? "pc-windows-msvc" : "unknown-linux-gnu")}";
        var output = Path.Combine(
            Root,
            "artifacts/bindings-integration/desktop",
            platform,
            appProject.Name,
            runtime == ApplicationRuntime.NativeAot ? "nativeaot" : "managed"
        );
        using var lease = AcquireLease(platform);
        if (!buildOnly)
            EnsurePortFree(platform == "windows" ? 4974 : 4975);
        Console.WriteLine(
            $"Desktop host: {runtime}, self-contained deployment + separate native renderer."
        );
        Directory.CreateDirectory(output);
        var plugins = await DiscoverPlugins(appProject.Project);
        var references = WritePreviewPluginReferences(plugins, output, appProject.Project);
        await Exec(
            dotnet,
            [
                "publish",
                "tools/DotNative.DevHost",
                "-c",
                "Release",
                "-r",
                rid,
                $"-p:PublishAot={runtime == ApplicationRuntime.NativeAot}",
                $"-p:PublishTrimmed={runtime == ApplicationRuntime.NativeAot}",
                "-p:SelfContained=true",
                "-p:UseAppHost=true",
                $"-p:JsonSerializerIsReflectionEnabledByDefault={runtime == ApplicationRuntime.Managed}",
                $"-p:DotNativeBindingsPlatform={platform}",
                $"-p:DotNativeApp={appProject.Directory}",
                $"-p:DotNativeStartupType={appProject.Startup}",
                $"-p:DotNativePlatform={platform.ToUpperInvariant()}",
                $"-p:DotNativePreviewPlugins={references}",
                $"-p:DirectoryBuildPropsPath={Path.Combine(Root, "eng/DotNative.Bindings.props")}",
                "-o",
                Path.Combine(output, "managed"),
            ]
        );
        var archive = await BuildDesktopPlugins(plugins, output, platform);
        await Exec(
            "cargo",
            [
                "build",
                "--release",
                "--locked",
                "--manifest-path",
                "native/core/Cargo.toml",
                "--features",
                "desktop,dev-host",
                "--bin",
                "dotnative-desktop",
                "--target",
                rust,
            ],
            env: new() { ["DOTNATIVE_PLUGIN_ARCHIVE"] = archive ?? "" }
        );
        var suffix = platform == "windows" ? ".exe" : "";
        var renderer = Path.Combine(output, appProject.Name + suffix);
        File.Copy(
            Path.Combine(Root, "native/core/target", rust, "release", "dotnative-desktop" + suffix),
            renderer,
            true
        );
        if (platform == "windows")
            await BuildWinUIRenderer(dotnet, rid, output);
        if (buildOnly)
        {
            Console.WriteLine($"Desktop proof: {output}");
            return 0;
        }
        // Keep the host lifecycle explicit on both platforms; the Windows runner
        // auto-starts its adjacent managed host, while Linux needs it started here.
        if (platform == "windows")
        {
            await Exec(renderer, []);
            return 0;
        }
        var ready = Path.Combine(output, "host.ready");
        File.Delete(ready);
        using var host = Start(
            Path.Combine(output, "managed/DotNative.DevHost"),
            ["Linux", "--once"],
            Root,
            new() { ["DotNativePreviewReadyFile"] = ready }
        );
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(ready))
            {
                if (host.HasExited)
                    throw new InvalidOperationException("Bindings host exited before startup.");
                await Task.Delay(100, timeout.Token);
            }
            await Exec(renderer, []);
        }
        finally
        {
            await StopOwnedProcess(host);
        }
        return 0;
    }
}
