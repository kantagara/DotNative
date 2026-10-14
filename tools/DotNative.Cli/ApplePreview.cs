using System.Text.RegularExpressions;

internal static partial class Preview
{
    private static async Task LaunchPhysicalIos(string[] args)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        bool notified = false;
        while (true)
        {
            var info = new System.Diagnostics.ProcessStartInfo("xcrun")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
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
            var detail = await output + await errors;
            if (process.ExitCode == 0)
            {
                Console.WriteLine(detail.Trim());
                return;
            }
            if (
                !detail.Contains("Locked", StringComparison.OrdinalIgnoreCase)
                || DateTime.UtcNow >= deadline
            )
                throw new IOException(detail.Trim());
            if (!notified)
            {
                Console.WriteLine(
                    "Unlock your iPhone. DotNative will retry launch automatically for up to 3 minutes."
                );
                notified = true;
            }
            await Task.Delay(2000, Stopping.Token);
        }
    }

    private static async Task<string> BuildPhysicalIos(
        string app,
        string output,
        string name,
        string bundle,
        string archive,
        string team,
        string? pluginObject
    )
    {
        var projectRoot = Path.Combine(output, "Native");
        CopyTree(Path.Combine(app, "Platform/iOS"), projectRoot);
        var project = Path.Combine(projectRoot, "Runner.xcodeproj");
        var pbx = Path.Combine(project, "project.pbxproj");
        // The generated preview target links Rust's development entry points;
        // it must never run the app's NativeAOT publish build phase.
        var text = File.ReadAllText(pbx);
        var scripts = Regex
            .Matches(text, @"([A-F0-9]{24}) = \{isa = PBXShellScriptBuildPhase;")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        text = Regex.Replace(
            text,
            @"buildPhases = \(([^)]*)\)",
            m =>
                "buildPhases = ("
                + string.Join(
                    ",",
                    m.Groups[1].Value.Split(',').Where(id => !scripts.Contains(id.Trim()))
                )
                + ")"
        );
        const string bundlePhase = "D070A7100000000000000036";
        var bundleScript =
            "export PATH=\"/usr/local/share/dotnet:/usr/local/bin:$PATH\"\nbash "
            + "'"
            + Path.Combine(output, "plugin-bundle.sh").Replace("'", "'\\''")
            + "'\n";
        text = Regex.Replace(
            text,
            @"buildPhases = \(([^)]*)\)",
            m => "buildPhases = (" + m.Groups[1].Value.TrimEnd(',') + "," + bundlePhase + ")"
        );
        var phase =
            bundlePhase
            + " = {isa = PBXShellScriptBuildPhase; alwaysOutOfDate = 1; buildActionMask = 2147483647; files = (); inputPaths = (); outputPaths = (); runOnlyForDeploymentPostprocessing = 0; name = \"Embed native plugin resources\"; shellPath = /bin/bash; shellScript = "
            + (
                "\""
                + bundleScript.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
                + "\""
            )
            + "; };\n";
        text = text.Replace("objects = {", "objects = {\n" + phase);
        WriteIfChanged(pbx, text);
        var config = """
PRODUCT_NAME = DotNativePreview
GENERATE_INFOPLIST_FILE = YES
INFOPLIST_KEY_UILaunchScreen_Generation = YES
TARGETED_DEVICE_FAMILY = 1,2
SUPPORTED_PLATFORMS = iphoneos
ARCHS = arm64
CLANG_ENABLE_OBJC_ARC = YES
CODE_SIGN_STYLE = Automatic
CURRENT_PROJECT_VERSION = 1
MARKETING_VERSION = 1.0
IPHONEOS_DEPLOYMENT_TARGET = 16.0
ALWAYS_SEARCH_USER_PATHS = NO
ENABLE_USER_SCRIPT_SANDBOXING = NO
INFOPLIST_KEY_UISupportedInterfaceOrientations = UIInterfaceOrientationPortrait UIInterfaceOrientationLandscapeLeft UIInterfaceOrientationLandscapeRight UIInterfaceOrientationPortraitUpsideDown
OTHER_LDFLAGS = -framework UIKit -framework Foundation -framework QuartzCore -framework CoreGraphics -framework CoreText -liconv
""";
        WriteIfChanged(Path.Combine(projectRoot, "Runner.xcconfig"), config + "\n");
        var data = Path.Combine(output, "Xcode");
        var swiftRoot = Path.GetFullPath(
            Path.Combine(
                (await Capture("xcrun", ["--find", "swiftc"])).Trim(),
                "../../lib/swift/iphoneos"
            )
        );
        var sdkRoot = (await Capture("xcrun", ["--sdk", "iphoneos", "--show-sdk-path"])).Trim();
        var pluginLink = pluginObject is null
            ? ""
            : $" \"{pluginObject}\" -L\"{swiftRoot}\" -L\"{sdkRoot}/usr/lib/swift\""
                + string.Concat(
                    AppleFrameworkLink(output)
                        .Select(arg =>
                            " \"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                        )
                );
        await Exec(
            "xcodebuild",
            [
                "-project",
                project,
                "-scheme",
                "Runner",
                "-configuration",
                "Debug",
                "-sdk",
                "iphoneos",
                "-destination",
                "generic/platform=iOS",
                "-derivedDataPath",
                data,
                "-allowProvisioningUpdates",
                "DEVELOPMENT_TEAM=" + team,
                "PRODUCT_BUNDLE_IDENTIFIER=" + bundle,
                "PRODUCT_NAME=" + name,
                "OTHER_LDFLAGS=$(inherited) \"" + archive + "\"" + pluginLink,
                "build",
            ]
        );
        return Path.Combine(data, "Build/Products/Debug-iphoneos", name + ".app");
    }
}
