using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

internal static partial class Preview
{
    private static async Task<int> RunManagedBindings(
        string platform,
        string dotnet,
        bool buildOnly,
        BindingsProject app,
        string? device,
        string? team
    )
    {
        if (
            !OperatingSystem.IsMacOS()
            || RuntimeInformation.ProcessArchitecture != Architecture.Arm64
        )
            throw new NotSupportedException(
                "Apple and Android managed builds currently require an Apple Silicon build host."
            );
        if (platform == "ios" && !buildOnly && device is null)
            throw new ArgumentException("Select an iOS simulator with --device <UDID>.");
        var physicalIos = false;
        if (platform == "ios" && device is not null)
        {
            var selected = (await Devices.Discover()).FirstOrDefault(d =>
                d.Platform == "ios" && (d.Id == device || d.UsbId == device)
            );
            if (selected is null)
                throw new ArgumentException(
                    "iOS device was not found; use dotnative devices to select a simulator or connected device."
                );
            physicalIos = selected.Kind == "physical";
            device = selected.Id;
        }
        if (physicalIos && (team is null || !Regex.IsMatch(team, "^[A-Z0-9]{10}$")))
            throw new ArgumentException(
                "Physical iOS deployment requires --team <Apple Team ID> or DOTNATIVE_APPLE_TEAM."
            );
        using var lease = AcquireLease("bindings-" + platform);
        var output = Path.Combine(Root, "artifacts/managed-integration", platform, app.Name);
        Directory.CreateDirectory(output);
        var cargoOutput = Path.Combine(output, "cargo");
        var target = platform switch
        {
            "ios" => physicalIos ? "aarch64-apple-ios" : "aarch64-apple-ios-sim",
            "android" => "aarch64-linux-android",
            _ => "aarch64-apple-darwin",
        };
        var rid = platform switch
        {
            "ios" => physicalIos ? "ios-arm64" : "iossimulator-arm64",
            "android" => "android-arm64",
            _ => "osx-arm64",
        };
        var environment = new Dictionary<string, string>();
        if (platform == "android")
        {
            var ndk =
                Environment.GetEnvironmentVariable("ANDROID_NDK_HOME")
                ?? Path.Combine(AndroidSdkDirectory(), "ndk/29.0.14206865");
            environment["CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER"] = Path.Combine(
                ndk,
                "toolchains/llvm/prebuilt/darwin-x86_64/bin/aarch64-linux-android30-clang"
            );
        }
        await Exec(
            "cargo",
            [
                "build",
                "--release",
                "--locked",
                "--manifest-path",
                "native/core/Cargo.toml",
                "--features",
                "managed-host",
                "--target",
                target,
                "--target-dir",
                cargoOutput,
            ],
            env: environment
        );
        var plugins = await DiscoverPlugins(app.Project, refreshAssets: true);
        string native;
        IReadOnlyList<string> androidLibraries = [];
        if (platform == "android")
        {
            native = Path.Combine(cargoOutput, target, "release/libcore.so");
            androidLibraries = await BuildManagedAndroidRunner(app, plugins, output);
        }
        else
        {
            var sdk = platform == "ios" ? (physicalIos ? "iphoneos" : "iphonesimulator") : "macosx";
            var triple =
                platform == "ios"
                    ? (physicalIos ? "arm64-apple-ios16.0" : "arm64-apple-ios16.0-simulator")
                    : "arm64-apple-macos13.0";
            var runner = Path.Combine(output, "Runner.o");
            await Exec(
                "xcrun",
                [
                    "--sdk",
                    sdk,
                    "clang",
                    "-target",
                    triple,
                    "-fobjc-arc",
                    "-Dmain=dotnative_runner_main",
                    "-c",
                    Path.Combine(
                        app.Directory,
                        "Platform",
                        platform == "ios" ? "iOS" : "Mac",
                        "Runner/main.m"
                    ),
                    "-o",
                    runner,
                ]
            );
            var plugin = await BuildApplePlugins(
                plugins,
                output,
                physicalIos,
                platform == "ios" ? "ios" : "mac"
            );
            var extraObjects = new List<string>();
            if (app.Startup == "Counter.Program")
            {
                var deviceInfo = Path.Combine(output, "DeviceInfo.o");
                await Exec(
                    "xcrun",
                    [
                        "--sdk",
                        sdk,
                        "clang",
                        "-target",
                        triple,
                        "-fobjc-arc",
                        "-c",
                        Path.Combine(Root, "plugins/DotNative.DeviceInfo/native/ios/device_info.m"),
                        "-o",
                        deviceInfo,
                    ]
                );
                extraObjects.Add(deviceInfo);
            }
            native = Path.Combine(output, "libdotnative.a");
            await Exec(
                "xcrun",
                [
                    "libtool",
                    "-static",
                    "-o",
                    native,
                    Path.Combine(cargoOutput, target, "release/libcore.a"),
                    runner,
                    .. extraObjects,
                    .. (plugin is null ? [] : new[] { plugin }),
                ]
            );
        }
        var project = await WriteManagedProject(
            app,
            platform,
            dotnet,
            output,
            native,
            androidLibraries
        );
        var arguments = new List<string>
        {
            "build",
            project,
            "-c",
            "Release",
            "-p:RuntimeIdentifier=" + rid,
            "-p:DotNativeBindingsPlatform=" + platform,
            "-p:DotNativeDevelopment=true",
            "-p:DotNativeSourceRoot=" + Root,
            "-p:DotNativeManagedBuildRoot=" + Path.Combine(output, "build"),
            "-p:DirectoryBuildPropsPath=" + Path.Combine(Root, "eng/DotNative.Managed.props"),
        };
        if (physicalIos)
        {
            arguments.Add("-p:EnableCodeSigning=true");
            arguments.Add("-p:CodesignKey=Apple Development");
            arguments.Add("-p:CodesignProvision=Automatic");
            arguments.Add("-p:DevelopmentTeam=" + team);
        }
        if (platform == "android")
        {
            arguments.Add("-p:AndroidSdkDirectory=" + AndroidSdkDirectory());
            if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } java)
                arguments.Add(
                    "-p:JavaSdkDirectory="
                        + (
                            Directory.Exists(
                                Path.Combine(java, "libexec/openjdk.jdk/Contents/Home")
                            )
                                ? Path.Combine(java, "libexec/openjdk.jdk/Contents/Home")
                                : java
                        )
                );
        }
        await Exec(dotnet, arguments.ToArray());
        var buildOutput = Path.Combine(
            output,
            "build",
            app.Name,
            "bin/Release",
            platform == "android" ? "net10.0-android" : $"net10.0-{platform}26.5",
            rid
        );
        if (platform == "android")
        {
            var apk = Path.Combine(buildOutput, app.ApplicationId + "-Signed.apk");
            if (!File.Exists(apk))
                throw new FileNotFoundException(
                    "The Android SDK did not produce a signed APK.",
                    apk
                );
            Console.WriteLine("Managed Mono + native renderer APK: " + apk);
            if (buildOnly)
                return 0;
            var adb = Path.Combine(AndroidSdkDirectory(), "platform-tools/adb");
            string[] selector = device is null ? [] : ["-s", device];
            await Exec(adb, [.. selector, "install", "-r", apk]);
            await Exec(adb, [.. selector, "shell", "am", "force-stop", app.ApplicationId]);
            await Exec(
                adb,
                [
                    .. selector,
                    "shell",
                    "am",
                    "start",
                    "-n",
                    app.ApplicationId + "/com.dotnative.runtime.NativeHost",
                ]
            );
        }
        else
        {
            var bundle = Path.Combine(buildOutput, app.Name + ".app");
            if (!Directory.Exists(bundle))
                throw new DirectoryNotFoundException(bundle);
            if (!physicalIos)
            {
                foreach (
                    var library in Directory.EnumerateFiles(
                        bundle,
                        "*.dylib",
                        SearchOption.AllDirectories
                    )
                )
                    await Exec("codesign", ["--force", "--sign", "-", library]);
                await Exec("codesign", ["--force", "--sign", "-", bundle]);
            }
            Console.WriteLine("Managed .NET + native renderer bundle: " + bundle);
            if (buildOnly)
                return 0;
            if (physicalIos)
            {
                await Exec(
                    "xcrun",
                    ["devicectl", "device", "install", "app", "--device", device!, bundle]
                );
                await LaunchPhysicalIos([
                    "devicectl",
                    "device",
                    "process",
                    "launch",
                    "--terminate-existing",
                    "--console",
                    "--device",
                    device!,
                    app.ApplicationId,
                ]);
            }
            else if (platform == "ios")
            {
                await Exec("xcrun", ["simctl", "bootstatus", device!, "-b"]);
                await Exec("xcrun", ["simctl", "install", device!, bundle]);
                await Exec(
                    "xcrun",
                    [
                        "simctl",
                        "launch",
                        "--terminate-running-process",
                        "--console-pty",
                        device!,
                        app.ApplicationId,
                    ]
                );
            }
            else
                await Exec(Path.Combine(bundle, "Contents/MacOS", app.Name), []);
        }
        return 0;
    }

    private static string AndroidSdkDirectory() =>
        Environment.GetEnvironmentVariable("ANDROID_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Android/sdk"
        );

    private static async Task<IReadOnlyList<string>> BuildManagedAndroidRunner(
        BindingsProject app,
        List<PluginProject> plugins,
        string output
    )
    {
        var runner = Path.Combine(output, "Android");
        CopyTree(Path.Combine(app.Directory, "Platform/Android"), runner);
        StageAndroidPlugins(
            plugins,
            runner,
            Path.Combine(app.Directory, "Platform/Android/app/build.gradle")
        );
        var rootGradle = Path.Combine(runner, "build.gradle");
        File.WriteAllText(
            rootGradle,
            File.ReadAllText(rootGradle).Replace("com.android.application", "com.android.library")
        );
        var gradle = Path.Combine(runner, "app/build.gradle");
        var source = File.ReadAllText(gradle)
            .Replace("com.android.application", "com.android.library");
        // Application-only plugins cannot be applied to the intermediate library.
        source = Regex.Replace(source, @"^\s*id ['"" ]com\.google\.(firebase\.crashlytics|gms\.google-services)['"" ][^\r\n]*", "", RegexOptions.Multiline);
        source = Regex.Replace(source, @"^\s*applicationId\s+[^\r\n]+", "", RegexOptions.Multiline);
        source += """

// The .NET Android SDK owns the runtime and final APK. Export native runner dependencies.
 tasks.register('exportDotNativeDependencies') {
     dependsOn 'assembleRelease'
     doLast {
         project.sync {
             from configurations.releaseRuntimeClasspath
             into layout.buildDirectory.dir('dotnative-dependencies')
         }
     }
 }
""";
        File.WriteAllText(gradle, source);
        await Exec(
            "bash",
            [
                "./gradlew",
                "--console=plain",
                ":app:exportDotNativeDependencies",
                "-PdotNativeLibraryName=core",
            ],
            runner
        );
        var archive = Path.Combine(runner, "app/build/outputs/aar/app-release.aar");
        if (!File.Exists(archive))
            throw new FileNotFoundException("Native runner AAR was not produced.", archive);
        var dependencies = Path.Combine(runner, "app/build/dotnative-dependencies");
        var files = Directory.EnumerateFiles(dependencies).ToArray();
        // Guava embeds ListenableFuture; AndroidX's standalone compatibility jar is redundant.
        var hasGuava = files.Any(path => Path.GetFileName(path).StartsWith("guava-", StringComparison.Ordinal));
        return new[] { archive }
            .Concat(
                Directory
                    .EnumerateFiles(dependencies)
                    .Where(path => Path.GetExtension(path) is ".aar" or ".jar")
                    .Where(path => !hasGuava || !Path.GetFileName(path).StartsWith("listenablefuture-", StringComparison.Ordinal))
            )
            .ToArray();
    }
}
