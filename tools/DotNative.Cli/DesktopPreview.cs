using System.Diagnostics;

internal static partial class Preview
{
    private static async Task<int> RunDesktop(
        string app,
        string project,
        string name,
        string startup,
        Device device,
        string target,
        string managed,
        int port
    )
    {
        if (
            (device.Platform == "windows" && !OperatingSystem.IsWindows())
            || (device.Platform == "linux" && !OperatingSystem.IsLinux())
        )
            throw new NotSupportedException("Run this preview on its target operating system.");

        var plugins = await DiscoverPlugins(project, refreshAssets: true);
        if (plugins.Any(p => p.Entry.Length == 0))
            throw new NotSupportedException(
                "This app references managed native plugins. Use run <platform> --bindings --project <path>."
            );

        Console.WriteLine(
            "Experimental native desktop preview. Release packaging remains under development."
        );
        var output = Path.Combine(
            Root,
            "tools/DotNative.DevHost/obj/Previews",
            name,
            device.Platform
        );
        Directory.CreateDirectory(output);
        using var lease = AcquirePreviewWorkerLease(device.Platform);
        EnsurePortFree(port);

        if (device.Platform == "linux")
            await Exec("pkg-config", ["--atleast-version=4.8", "gtk4"]);
        var archive = await BuildDesktopPlugins(plugins, output, device.Platform);
        var cargoEnvironment = new Dictionary<string, string>
        {
            ["DOTNATIVE_PLUGIN_ARCHIVE"] = archive ?? "",
        };
        // Each application owns its linked plugin registry and cached executable.
        var cargoOutput = Path.Combine(output, "cargo");
        await Exec(
            "cargo",
            [
                "build",
                "--release",
                "--locked",
                "--manifest-path",
                Path.Combine(Root, "native/core/Cargo.toml"),
                "--features",
                "desktop,dev-host",
                "--bin",
                "dotnative-desktop",
                "--target",
                target,
                "--target-dir",
                cargoOutput,
            ],
            env: cargoEnvironment
        );

        var environment = new Dictionary<string, string>
        {
            ["DotNativeApp"] = app,
            ["DotNativeDevelopment"] = "true",
            ["DotNativeSourceRoot"] = Root,
            ["DotNativePreviewAppProject"] = project,
            ["DotNativePreviewAppAssembly"] = await PreviewAssemblyName(project),
            ["DirectoryBuildTargetsPath"] = Path.Combine(Root, "eng/DotNative.Preview.targets"),
            ["DotNativeStartupType"] = startup,
            ["DotNativePlatform"] = managed,
            ["DotNativePreviewPlugins"] = WritePreviewPluginReferences(plugins, output, project),
            ["DotNativePreviewBuildRoot"] = Path.Combine(output, "managed"),
            ["DirectoryBuildPropsPath"] = Path.Combine(Root, "eng/DotNative.Preview.props"),
            ["DotNativePreviewReadyFile"] = Path.Combine(output, "host.ready"),
        };
        var host = Path.Combine(Root, "tools/DotNative.DevHost/DotNative.DevHost.csproj");
        var buildProperties = PreviewBuildProperties(environment);
        await Exec("dotnet", ["build", host, .. buildProperties], env: environment);
        File.Delete(environment["DotNativePreviewReadyFile"]);
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
            environment
        );
        Process? renderer = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            while (!File.Exists(environment["DotNativePreviewReadyFile"]))
            {
                if (watcher.HasExited)
                    throw new InvalidOperationException(
                        "Preview host exited before binding its socket."
                    );
                await Task.Delay(100, timeout.Token);
            }
            var executable = Path.Combine(
                cargoOutput,
                target,
                "release",
                OperatingSystem.IsWindows() ? "dotnative-desktop.exe" : "dotnative-desktop"
            );
            if (device.Platform == "windows")
                await BuildWinUIRenderer(
                    "dotnet",
                    target.StartsWith("aarch64") ? "win-arm64" : "win-x64",
                    Path.GetDirectoryName(executable)!
                );
            renderer = Start(executable, [], app);
            Console.WriteLine(
                "Native preview started. Edit C# and save. Close the window or press Ctrl+C to stop."
            );
            await Task.WhenAny(
                watcher.WaitForExitAsync(Stopping.Token),
                renderer.WaitForExitAsync(Stopping.Token)
            );
            Stopping.Token.ThrowIfCancellationRequested();
            return renderer.HasExited ? renderer.ExitCode : watcher.ExitCode;
        }
        finally
        {
            // Own only the two processes started for this session. Never kill a
            // process merely because it occupies the preview port.
            if (renderer is not null)
            {
                await StopOwnedProcess(renderer);
                renderer.Dispose();
            }
            await StopOwnedProcess(watcher);
        }
    }

    private static async Task StopOwnedProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
