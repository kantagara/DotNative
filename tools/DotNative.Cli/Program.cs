using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return await Preview.Run(args);

internal static partial class Preview
{
    private static readonly CancellationTokenSource Stopping = new();
    private static string Root = "";

    public static async Task<int> Run(string[] args)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Stopping.Cancel();
        };
        try
        {
            if (args.Length == 4 && args[0] == "--stage-apple-bundle")
            {
                await AppleDependencies.StageBundle(args[1], args[2], args[3]);
                return 0;
            }
            if (args.Length == 0 || args[0] is "--help" or "help")
            {
                Console.WriteLine(
                    "dotnative plugin new --name Name --output path [--kind native|csharp]\ndotnative plugin run <platform> --project path [--device id]\ndotnative restore --project path\ndotnative plugin build --project path --platform mac|windows|linux\ndotnative run <platform> --bindings --project path [--dotnet path] [--device id] [--build-only] [--runtime nativeaot|managed]\ndotnative devices [--all]\ndotnative run [ios|android|mac|windows|linux] --hot-reload [--project path] [--device id]\ndotnative bindings-demo ios|android|mac|windows|linux [--dotnet path] [--device id] [--build-only] [--runtime nativeaot|managed]\nInstallable DotNative CLI. Windows/Linux previews and Android NativeAOT bindings are experimental. Requires .NET 10 SDK and platform build tools."
                );
                return 0;
            }
            if (args[0] == "devices")
            {
                if (args.Length > 2 || (args.Length == 2 && args[1] != "--all"))
                    throw new ArgumentException("Use: dotnative devices");
                var found = await Devices.Discover();
                Devices.Print(
                    args.Contains("--all")
                        ? found
                        : found.Where(d => d.State != "Shutdown").ToList()
                );
                return 0;
            }
            Root = FindRoot(Environment.CurrentDirectory);
            if (args[0] == "restore")
                return await RestoreDependencies(args[1..]);
            if (args[0] == "plugin")
                return await RunPluginCommand(args[1..]);
            if (args[0] == "run" && (args.Contains("--bindings") || args.Contains("--runtime")))
                return await RunBindingsDemo(args[1..].Where(a => a != "--bindings").ToArray());
            if (args[0] == "bindings-demo")
                return await RunBindingsDemo(args[1..]);
            if (args.Length < 2 || args[0] != "run" || !args.Contains("--hot-reload"))
                throw new ArgumentException(
                    "Use: dotnative run <platform> --bindings --project path [--dotnet path] [--device id] [--build-only] [--runtime nativeaot|managed]\ndotnative devices [--all]\ndotnative run [ios|android|mac|windows|linux] --hot-reload [--project path] [--device id]"
                );
            string? platform = args.Length > 1 && !args[1].StartsWith("-") ? args[1] : null;
            string? project = null,
                device = null,
                team = Environment.GetEnvironmentVariable("DOTNATIVE_APPLE_TEAM");
            for (int i = platform is null ? 1 : 2; i < args.Length; i++)
            {
                if (args[i] is "--hot-reload" or "--preview-worker")
                    continue;
                if (args[i] is not ("--project" or "--device" or "--team") || i + 1 == args.Length)
                    throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
                var option = args[i++];
                if (option == "--project")
                    project = args[i];
                else if (option == "--team")
                    team = args[i];
                else
                    device = args[i];
            }
            var selected = Devices.Select(await Devices.Discover(), device, platform);
            if (!selected.CanPreview)
                throw new NotSupportedException(
                    $"{selected.Name}: hot reload is not supported for {selected.Kind} {selected.Platform} ({selected.State}, {selected.Architecture}). Physical iOS requires the signed NativeAOT device build, not this simulator preview."
                );
            platform = selected.Platform;
            device = selected.Id;
            bool physicalIos = platform == "ios" && selected.Kind == "physical";
            if (physicalIos && (team is null || !Regex.IsMatch(team, "^[A-Z0-9]{10}$")))
                throw new ArgumentException(
                    "Physical iOS preview requires --team <Apple Team ID> or DOTNATIVE_APPLE_TEAM."
                );
            var (target, managed, port) = platform switch
            {
                "ios" => (physicalIos ? "aarch64-apple-ios" : "aarch64-apple-ios-sim", "IOS", 4971),
                "android" => ("aarch64-linux-android", "ANDROID", 4972),
                "mac" => ("aarch64-apple-darwin", "MACOS", 4973),
                "windows" or "linux" => DesktopTarget.Resolve(platform, selected.Architecture),
                _ => throw new ArgumentException("Unsupported device platform."),
            };
            Console.WriteLine($"Device: {selected.Name} ({selected.Id}) → {target}");
            var app = Path.GetFullPath(
                project
                    ?? (
                        Directory.GetFiles(Environment.CurrentDirectory, "*.csproj").Length == 1
                            ? Environment.CurrentDirectory
                            : Path.Combine(
                                Root,
                                platform is "windows" or "linux"
                                    ? "Samples/Counter"
                                    : "Samples/WidgetSamples"
                            )
                    )
            );
            if (File.Exists(app))
                app = Path.GetDirectoryName(app)!;
            var projects = Directory.GetFiles(app, "*.csproj");
            if (projects.Length != 1)
                throw new ArgumentException(
                    "Choose an app directory containing one csproj with --project."
                );
            var name = Path.GetFileNameWithoutExtension(projects[0]);
            var doc = XDocument.Load(projects[0]);
            var startup =
                doc.Descendants("DotNativeStartupType").FirstOrDefault()?.Value
                ?? (doc.Descendants("RootNamespace").FirstOrDefault()?.Value ?? name) + ".Program";
            if (!Regex.IsMatch(startup, @"^[A-Za-z_][A-Za-z_0-9]*(\.[A-Za-z_][A-Za-z_0-9]*)*$"))
                throw new ArgumentException(
                    "DotNativeStartupType must be a fully qualified C# type name."
                );
            if (!args.Contains("--preview-worker"))
                return await SupervisePreview(args, app, projects[0], selected);

            if (platform is "windows" or "linux")
                return await RunDesktop(
                    app,
                    projects[0],
                    name,
                    startup,
                    selected,
                    target,
                    managed,
                    port
                );

            if (!OperatingSystem.IsMacOS())
                throw new NotSupportedException(
                    "This development preview currently requires macOS."
                );
            if (platform == "android" && device is null)
                throw new ArgumentException(
                    "Specify --device emulator-5554 (boot an ARM64 emulator first)."
                );

            // Fail before compiling or replacing a running app. Never kill an unknown port owner.
            var output = Path.Combine(
                Root,
                "tools/DotNative.DevHost/obj/Previews",
                name,
                physicalIos ? "ios-device" : platform
            );
            Directory.CreateDirectory(output);
            var plugins = await DiscoverPlugins(projects[0], refreshAssets: true);
            if (plugins.Any(p => p.Entry.Length == 0))
                throw new NotSupportedException(
                    "This app references managed native plugins. Use run <platform> --bindings --project <path>; remote mobile preview cannot execute device SDK bindings."
                );
            var pluginReferences = WritePreviewPluginReferences(plugins, output, projects[0]);
            using var lease = AcquirePreviewWorkerLease(platform);
            EnsurePortFree(port);
            if (platform == "ios" && !physicalIos && selected.State != "Booted")
            {
                await Exec("xcrun", ["simctl", "boot", device]);
                await Exec("xcrun", ["simctl", "bootstatus", device, "-b"]);
            }
            var env = new Dictionary<string, string>();
            var sdk = Devices.AndroidSdk;
            if (platform == "android")
            {
                var ndk =
                    Environment.GetEnvironmentVariable("ANDROID_NDK_HOME")
                    ?? Path.Combine(sdk, "ndk/29.0.14206865");
                env["CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER"] = Path.Combine(
                    ndk,
                    "toolchains/llvm/prebuilt/darwin-x86_64/bin/aarch64-linux-android30-clang"
                );
            }
            var cargoOut = Path.Combine(Root, "native/core/target/dev-host");
            await Exec(
                "cargo",
                [
                    "build",
                    "--manifest-path",
                    Path.Combine(Root, "native/core/Cargo.toml"),
                    "--release",
                    "--features",
                    "dev-host",
                    "--target",
                    target,
                    "--target-dir",
                    cargoOut,
                ],
                env: env
            );
            var bundle = "com.dotnative.preview." + name.ToLowerInvariant();
            string launchFile;
            string[] launchArgs;
            if (physicalIos)
            {
                var pluginObject = await BuildApplePlugins(plugins, output, true);
                var product = await BuildPhysicalIos(
                    app,
                    output,
                    name,
                    bundle,
                    Path.Combine(cargoOut, target, "release/libcore.a"),
                    team!,
                    pluginObject
                );
                await Exec(
                    "xcrun",
                    ["devicectl", "device", "install", "app", "--device", device, product]
                );
                launchFile = "xcrun";
                launchArgs =
                [
                    "devicectl",
                    "device",
                    "process",
                    "launch",
                    "--device",
                    device,
                    "--terminate-existing",
                    bundle,
                ];
            }
            else if (platform == "android")
            {
                var native = Path.Combine(output, "Android");
                CopyTree(
                    Path.Combine(app, "Platform/Android"),
                    native,
                    "app/build.gradle",
                    "app/src/main/AndroidManifest.xml"
                );
                StageAndroidPlugins(
                    plugins,
                    native,
                    Path.Combine(app, "Platform/Android/app/build.gradle"),
                    bundle
                );
                var manifest = Path.Combine(native, "app/src/main/AndroidManifest.xml");
                var xml = XDocument.Load(
                    Path.Combine(app, "Platform/Android/app/src/main/AndroidManifest.xml")
                );
                xml.Root!.AddFirst(
                    new XElement(
                        "uses-permission",
                        new XAttribute(
                            XName.Get("name", "http://schemas.android.com/apk/res/android"),
                            "android.permission.INTERNET"
                        )
                    )
                );
                WriteIfChanged(manifest, xml.ToString());
                var libs = Path.Combine(native, "obj/DotNative/jniLibs/arm64-v8a");
                Directory.CreateDirectory(libs);
                CopyFileIfChanged(
                    Path.Combine(cargoOut, target, "release/libcore.so"),
                    Path.Combine(libs, "libdotnative.so")
                );
                env["ANDROID_HOME"] = sdk;
                env["JAVA_HOME"] =
                    Environment.GetEnvironmentVariable("JAVA_HOME")
                    ?? "/opt/homebrew/opt/openjdk@17";
                await Exec("bash", ["./gradlew", "--console=plain", "assembleDebug"], native, env);
                var adb = Path.Combine(sdk, "platform-tools/adb");
                var apk = Path.Combine(native, "app/build/outputs/apk/debug/app-debug.apk");
                var installed = await CommandOutput(
                    adb,
                    ["-s", device!, "shell", "pm", "path", bundle]
                );
                var stamp = Path.Combine(
                    output,
                    "installed-android-"
                        + Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(device!)
                            )
                        )[..16]
                        + ".txt"
                );
                var revision = NativeBuildCache.ContentRevision(apk);
                if (
                    installed.ExitCode != 0
                    || !installed.Output.Contains("package:", StringComparison.Ordinal)
                    || !File.Exists(stamp)
                    || File.ReadAllText(stamp) != revision
                )
                {
                    await Exec(adb, ["-s", device!, "install", "-r", apk]);
                    WriteIfChanged(stamp, revision);
                }
                else
                    Console.WriteLine("[build] Android app: already installed.");
                await Exec(adb, ["-s", device!, "reverse", "tcp:4972", "tcp:4972"]);
                await Exec(adb, ["-s", device!, "shell", "am", "force-stop", bundle]);
                launchFile = adb;
                launchArgs =
                [
                    "-s",
                    device!,
                    "shell",
                    "am",
                    "start",
                    // Clear this preview's task, including a document picker
                    // left above it when the previous process was stopped.
                    "-f",
                    "0x10008000",
                    "-n",
                    $"{bundle}/com.dotnative.runtime.NativeHost",
                ];
            }
            else
            {
                var ios = platform == "ios";
                var dest = Path.Combine(output, name + ".app");
                var binary = ios ? dest : Path.Combine(dest, "Contents/MacOS");
                Directory.CreateDirectory(binary);
                var appleSdk = ios ? "iphonesimulator" : "macosx";
                var sdkPath = (
                    await Capture("xcrun", ["--sdk", appleSdk, "--show-sdk-path"])
                ).Trim();
                var pluginObject = await BuildApplePlugins(plugins, output, false, platform);
                string[] pluginLink = pluginObject is null
                    ? []
                    :
                    [
                        pluginObject,
                        "-L" + sdkPath + "/usr/lib/swift",
                        "-L"
                            + Path.GetFullPath(
                                Path.Combine(
                                    (await Capture("xcrun", ["--find", "swiftc"])).Trim(),
                                    "../../lib/swift/iphonesimulator"
                                )
                            ),
                    ];
                var entitlementLink = ios
                    ? new[]
                    {
                        "-Xlinker",
                        "-sectcreate",
                        "-Xlinker",
                        "__TEXT",
                        "-Xlinker",
                        "__entitlements",
                        "-Xlinker",
                        WriteSimulatorEntitlements(output, bundle),
                    }
                    : Array.Empty<string>();
                var bundleCache = new NativeBuildCache(
                    Path.Combine(output, "bundle.stamp.json"),
                    [
                        Path.Combine(app, ios ? "Platform/iOS" : "Platform/Mac"),
                        Path.Combine(cargoOut, target, "release/libcore.a"),
                        .. (pluginObject is null ? Array.Empty<string>() : new[] { pluginObject }),
                        .. entitlementLink.Where(File.Exists),
                        Path.Combine(output, "apple-dependencies.json"),
                        .. AppleDependencyInputs(output),
                        Path.Combine(sdkPath, "SDKSettings.json"),
                    ],
                    [dest],
                    name,
                    bundle,
                    target,
                    sdkPath,
                    (await Capture("xcrun", ["clang", "--version"])).Trim()
                );
                if (!bundleCache.IsCurrent)
                {
                    // Replace the bundle so deleted launch resources cannot survive a rebuild.
                    if (Directory.Exists(dest))
                        Directory.Delete(dest, recursive: true);
                    Directory.CreateDirectory(binary);
                    await Exec(
                        "xcrun",
                        [
                            "--sdk",
                            appleSdk,
                            "clang",
                            "-target",
                            ios ? "arm64-apple-ios16.0-simulator" : "arm64-apple-macos13.0",
                            "-isysroot",
                            sdkPath,
                            "-fobjc-arc",
                            Path.Combine(
                                app,
                                ios ? "Platform/iOS/Runner/main.m" : "Platform/Mac/Runner/main.m"
                            ),
                            Path.Combine(cargoOut, target, "release/libcore.a"),
                            .. pluginLink,
                            .. AppleFrameworkLink(output),
                            .. entitlementLink,
                            "-framework",
                            ios ? "UIKit" : "AppKit",
                            "-framework",
                            "Foundation",
                            "-framework",
                            "QuartzCore",
                            "-framework",
                            "CoreGraphics",
                            "-framework",
                            "CoreText",
                            "-liconv",
                            "-o",
                            Path.Combine(binary, name),
                        ]
                    );
                    WritePlist(
                        Path.Combine(dest, ios ? "Info.plist" : "Contents/Info.plist"),
                        name,
                        bundle,
                        ios,
                        Path.Combine(app, "Platform", ios ? "iOS" : "Mac", "Info.plist")
                    );
                    if (ios)
                        await BuildLaunchScreen(app, dest, appleSdk);
                    if (ios)
                        await AppleDependencies.StageBundle(output, dest, "-");
                    await Exec("codesign", ["--force", "--sign", "-", dest]);
                    bundleCache.Commit();
                }
                else
                    Console.WriteLine("[build] Native app: unchanged; using signed bundle.");
                if (ios)
                {
                    device ??= "booted";
                    var installed = await CommandOutput(
                        "xcrun",
                        ["simctl", "get_app_container", device, bundle, "app"]
                    );
                    var installStamp = Path.Combine(output, $"installed-{device}.txt");
                    var revision = bundleCache.Revision;
                    if (
                        installed.ExitCode != 0
                        || !File.Exists(installStamp)
                        || File.ReadAllText(installStamp) != revision
                    )
                    {
                        await Exec("xcrun", ["simctl", "install", device, dest]);
                        WriteIfChanged(installStamp, revision);
                    }
                    else
                        Console.WriteLine("[build] Simulator app: already installed.");
                    await StopSimulatorApp(device, bundle);
                    launchFile = "xcrun";
                    launchArgs = ["simctl", "launch", device, bundle];
                }
                else
                {
                    launchFile = Path.Combine(binary, name);
                    launchArgs = [];
                }
            }
            var watchEnv = new Dictionary<string, string>
            {
                ["DotNativeApp"] = app,
                ["DotNativeDevelopment"] = "true",
                ["DotNativeSourceRoot"] = Root,
                ["DotNativePreviewAppProject"] = projects[0],
                ["DotNativePreviewAppAssembly"] = await PreviewAssemblyName(projects[0]),
                ["DirectoryBuildTargetsPath"] = Path.Combine(Root, "eng/DotNative.Preview.targets"),
                ["DotNativePreviewPlugins"] = pluginReferences,
                ["DotNativeStartupType"] = startup,
                ["DotNativePlatform"] = managed,
                ["DotNativePreviewBuildRoot"] = Path.Combine(output, "managed"),
                ["DirectoryBuildPropsPath"] = Path.Combine(Root, "eng/DotNative.Preview.props"),
                ["DotNativePreviewReadyFile"] = Path.Combine(output, "host.ready"),
            };
            var host = Path.Combine(Root, "tools/DotNative.DevHost/DotNative.DevHost.csproj");
            var buildProperties = PreviewBuildProperties(watchEnv);
            await Exec("dotnet", ["build", host, .. buildProperties], env: watchEnv);
            File.Delete(watchEnv["DotNativePreviewReadyFile"]);
            using var watcher = Start(
                "dotnet",
                [
                    "watch",
                    .. PreviewWatchOptions(),
                    "--non-interactive",
                    "--project",
                    host,
                    "run",
                    .. buildProperties,
                    "--",
                    managed,
                ],
                Root,
                watchEnv
            );
            Process? nativeProcess = null;
            try
            {
                // Launch only once the actual host has bound its socket, not just watch's build process.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                while (!File.Exists(watchEnv["DotNativePreviewReadyFile"]))
                {
                    if (watcher.HasExited)
                        throw new InvalidOperationException(
                            "Preview host exited before becoming ready."
                        );
                    await Task.Delay(100, timeout.Token);
                }
                if (physicalIos)
                    await LaunchPhysicalIos(launchArgs);
                else if (platform == "mac")
                    nativeProcess = Start(launchFile, launchArgs, app);
                else
                    await Exec(launchFile, launchArgs);
                if (physicalIos)
                {
                    using var usbSession = CancellationTokenSource.CreateLinkedTokenSource(
                        Stopping.Token
                    );
                    var relay = UsbTunnel.Relay(selected.UsbId!, port, usbSession.Token);
                    try
                    {
                        var hostExit = watcher.WaitForExitAsync(usbSession.Token);
                        await Task.WhenAny(relay, hostExit);
                        if (relay.IsCompleted)
                            await relay;
                        else
                            await hostExit;
                    }
                    finally
                    {
                        usbSession.Cancel();
                        try
                        {
                            await relay;
                        }
                        catch (OperationCanceledException) { }
                    }
                    return 0;
                }
                Console.WriteLine(
                    "DotNative preview ready. Edit C# and save. Ctrl+C stops this session."
                );
                if (nativeProcess is not null)
                {
                    await Task.WhenAny(
                        watcher.WaitForExitAsync(Stopping.Token),
                        nativeProcess.WaitForExitAsync(Stopping.Token)
                    );
                    Stopping.Token.ThrowIfCancellationRequested();
                    return nativeProcess.HasExited ? nativeProcess.ExitCode : watcher.ExitCode;
                }
                await watcher.WaitForExitAsync(Stopping.Token);
                return watcher.ExitCode;
            }
            finally
            {
                if (nativeProcess is not null)
                {
                    await StopOwnedProcess(nativeProcess);
                    nativeProcess.Dispose();
                }
                if (!watcher.HasExited)
                {
                    watcher.Kill(entireProcessTree: true);
                    await watcher.WaitForExitAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            return Stopping.IsCancellationRequested ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"dotnative: {error.Message}");
            return 1;
        }
    }

    private static FileStream AcquireLease(string platform)
    {
        var dir = Path.Combine(Root, "tools/DotNative.DevHost/obj/Sessions");
        Directory.CreateDirectory(dir);
        try
        {
            return new FileStream(
                Path.Combine(dir, platform + ".lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
        }
        catch (IOException)
        {
            throw new InvalidOperationException(
                $"A DotNative {platform} session is already running. Use that terminal, or stop it with Ctrl+C before running again."
            );
        }
    }

    private static void EnsurePortFree(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException(
                $"Preview port {port} is already in use. Stop the existing preview terminal with Ctrl+C and retry. No process was terminated."
            );
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string FindRoot(string path) =>
        SdkLocation.Resolve(path, AppContext.BaseDirectory);

    private static Process Start(
        string file,
        string[] args,
        string cwd,
        Dictionary<string, string>? env = null,
        bool capture = false
    )
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var pair in env)
                info.Environment[pair.Key] = pair.Value;
        return Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {file}");
    }

    private static async Task Exec(
        string file,
        string[] args,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool allowFailure = false
    )
    {
        using var process = Start(file, args, cwd ?? Root, env);
        try
        {
            await process.WaitForExitAsync(Stopping.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
            }
        }
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(
                $"{file} {string.Join(" ", args.Take(4))} failed (exit {process.ExitCode}). See the tool diagnostics above."
            );
    }

    private static async Task<string> Capture(string file, string[] args)
    {
        var result = await CommandOutput(file, args);
        if (result.ExitCode != 0)
            throw new IOException(
                $"{file} {string.Join(" ", args.Take(3))} failed (exit {result.ExitCode}): {result.Error.Trim()}"
            );
        return result.Output;
    }

    private static void CopyTree(string source, string dest, params string[] excluded)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            if (
                Path.GetFileName(file) != "local.properties"
                && !excluded.Contains(Path.GetFileName(file))
            )
                CopyFileIfChanged(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            if (Path.GetFileName(dir) is not ("obj" or "bin" or "build" or ".gradle" or ".kotlin"))
            {
                var prefix = Path.GetFileName(dir) + "/";
                CopyTree(
                    dir,
                    Path.Combine(dest, Path.GetFileName(dir)),
                    excluded
                        .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(path => path[prefix.Length..])
                        .ToArray()
                );
            }
    }

    private static void WritePlist(
        string path,
        string name,
        string bundle,
        bool ios,
        string? overlayPath = null
    )
    {
        var dict = new XElement("dict");
        void Add(string key, XElement value)
        {
            dict.Add(new XElement("key", key), value);
        }
        void Text(string key, string value) => Add(key, new XElement("string", value));
        Text("CFBundleExecutable", name);
        Text("CFBundleIdentifier", bundle);
        Text("CFBundleName", name + " Preview");
        Text("CFBundlePackageType", "APPL");
        Text("CFBundleVersion", "1");
        Text("CFBundleShortVersionString", "1.0");
        if (ios)
        {
            Add("LSRequiresIPhoneOS", new XElement("true"));
            Text("MinimumOSVersion", "16.0");
            Add("UIDeviceFamily", new XElement("array", new XElement("integer", 1)));
            Add("UILaunchScreen", new XElement("dict"));
        }
        else
        {
            Add("NSHighResolutionCapable", new XElement("true"));
            Text("LSMinimumSystemVersion", "13.0");
        }
        if (overlayPath is not null && File.Exists(overlayPath))
        {
            var overlay =
                XDocument.Load(overlayPath).Root?.Element("dict")
                ?? throw new InvalidDataException(
                    "Platform Info.plist must contain a plist dictionary."
                );
            var entries = overlay.Elements().ToArray();
            if (entries.Length % 2 != 0)
                throw new InvalidDataException("Invalid platform Info.plist dictionary.");
            var reserved = new HashSet<string>
            {
                "CFBundleExecutable",
                "CFBundleIdentifier",
                "CFBundlePackageType",
                "CFBundleVersion",
                "CFBundleShortVersionString",
            };
            for (var index = 0; index < entries.Length; index += 2)
            {
                if (entries[index].Name != "key")
                    throw new InvalidDataException("Invalid platform Info.plist key.");
                var key = entries[index].Value;
                if (reserved.Contains(key))
                    throw new InvalidDataException($"Platform Info.plist cannot override {key}.");
                var existing = dict.Elements("key").FirstOrDefault(element => element.Value == key);
                if (existing is not null)
                {
                    existing.ElementsAfterSelf().First().Remove();
                    existing.Remove();
                }
                Add(key, new XElement(entries[index + 1]));
            }
        }
        new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("plist", new XAttribute("version", "1.0"), dict)
        ).Save(path);
    }
}
