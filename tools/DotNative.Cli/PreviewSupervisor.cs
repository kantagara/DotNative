using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal static partial class Preview
{
    private static async Task<int> SupervisePreview(
        string[] arguments,
        string app,
        string project,
        Device device
    )
    {
        using var lease = AcquireLease(device.Platform);
        var previewPort =
            4970
            + (
                device.Platform switch
                {
                    "ios" => 1,
                    "android" => 2,
                    "mac" => 3,
                    "windows" => 4,
                    "linux" => 5,
                    _ => throw new ArgumentException("Unsupported preview platform."),
                }
            );
        EnsurePortFree(previewPort);
        var roots = new HashSet<string>(StringComparer.Ordinal) { app };
        foreach (
            var folder in new[] { "src", "native", "eng", "templates", "tools/DotNative.DevHost" }
        )
            roots.Add(Path.Combine(Root, folder));
        PreviewFiles.AddProjectRoots(project, roots);
        var baseline = PreviewFiles.Read(roots);
        var generation = 0;
        var recoveryUsed = false;
        var inconsistentMetadata = 0;
        while (!Stopping.IsCancellationRequested)
        {
            var build = $"{++generation}-{Guid.NewGuid():N}";
            build = build[..Math.Min(build.Length, 12)];
            Console.WriteLine($"[preview] Building {build}. Native confirmation is pending.");
            using var session = new CancellationTokenSource();
            var restart = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var bootCount = 0;
            string? managedRestartReason = null;
            string? lastManagedFile = null;
            var nativeConfirmed = false;
            var compilationFailed = 0;
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);
            // Resolve an implicit device once; never switch devices during a rebuild.
            if (!arguments.Contains("--device"))
            {
                info.ArgumentList.Add("--device");
                info.ArgumentList.Add(device.Id);
            }
            info.ArgumentList.Add("--preview-worker");
            info.Environment["DOTNATIVE_PREVIEW_BUILD_ID"] = build;
            info.Environment["DOTNATIVE_PREVIEW_SUPERVISED"] = "1";
            info.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";
            if (Volatile.Read(ref inconsistentMetadata) != 0)
                info.Environment["DOTNATIVE_PREVIEW_REBUILD_ONLY"] = "1";
            using var child =
                Process.Start(info)
                ?? throw new InvalidOperationException("Cannot start preview worker.");
            child.StandardInput.Close();
            async Task Forward(StreamReader reader, bool error)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (
                        session.IsCancellationRequested
                        && (
                            line.Contains("Exited with error code 143", StringComparison.Ordinal)
                            || line.Contains("Exited with error code 137", StringComparison.Ordinal)
                        )
                    )
                        continue; // Expected termination of our owned process tree.
                    if (
                        line.Contains("File updated:", StringComparison.Ordinal)
                        || line.Contains("File changed:", StringComparison.Ordinal)
                    )
                        lastManagedFile = line[(line.IndexOf(':') + 1)..].Trim();
                    if (
                        System.Text.RegularExpressions.Regex.IsMatch(
                            line,
                            @"ENC\d{4}|not supported|cannot be applied|Unable to apply|requires.*restart",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        )
                    )
                        managedRestartReason = line.Trim();
                    if (error)
                        Console.Error.WriteLine(line);
                    else
                        Console.WriteLine(line);
                    if (
                        line.StartsWith("DotNative UI preview:", StringComparison.Ordinal)
                        && Interlocked.Increment(ref bootCount) > 1
                    )
                        restart.TrySetResult(
                            "Managed host restarted: "
                                + (
                                    managedRestartReason
                                    ?? (
                                        lastManagedFile is null
                                            ? "watch requested a restart"
                                            : "edit in " + lastManagedFile
                                    )
                                )
                                + "; reconnecting the native preview."
                        );
                    if (
                        line.Contains(
                            "Found project reference without a matching metadata reference",
                            StringComparison.Ordinal
                        )
                        && Interlocked.Exchange(ref inconsistentMetadata, 1) == 0
                    )
                    {
                        Console.Error.WriteLine(
                            "[preview] SDK watch metadata is inconsistent. Disabling hot reload; source edits will rebuild the host and renderer."
                        );
                        restart.TrySetResult(
                            "SDK metadata requires rebuilding without hot reload."
                        );
                    }
                    if (line.StartsWith("[preview] Displayed build ", StringComparison.Ordinal))
                    {
                        nativeConfirmed = true;
                        Interlocked.Exchange(ref compilationFailed, 0);
                    }
                    if (
                        (
                            line.Contains("Build failed.", StringComparison.Ordinal)
                            || line.Contains("Fix the error", StringComparison.Ordinal)
                        )
                        && Interlocked.Exchange(ref compilationFailed, 1) == 0
                    )
                        Console.Error.WriteLine(
                            $"[preview] Compilation rejected. Build {build} still displays its previous revision; save a fix to continue."
                        );
                    if (
                        line.StartsWith("Preview ended:", StringComparison.Ordinal)
                        || line.Contains("DeltaMetadataWriter", StringComparison.Ordinal)
                        || line.Contains("Unhandled exception.", StringComparison.Ordinal)
                    )
                        restart.TrySetResult(
                            "Preview session failed; recovering its host and renderer."
                        );
                }
            }
            var stdout = Forward(child.StandardOutput, false);
            var stderr = Forward(child.StandardError, true);
            var changes = WatchPreviewFiles(
                project,
                roots,
                baseline,
                restart,
                () => Volatile.Read(ref inconsistentMetadata) != 0,
                session.Token
            );
            string? reason = null;
            var nativeChange = false;
            try
            {
                var exit = child.WaitForExitAsync(Stopping.Token);
                var finished = await Task.WhenAny(exit, restart.Task, changes);
                if (finished == changes)
                    await changes;
                Stopping.Token.ThrowIfCancellationRequested();
                if (PreviewFiles.Changes(baseline, PreviewFiles.Read(roots)).Any())
                    recoveryUsed = false;
                // The file watcher signals restart and then completes. Either task
                // can win WhenAny; honor the signal before waiting for child exit.
                if (restart.Task.IsCompletedSuccessfully)
                {
                    reason = await restart.Task;
                    nativeChange =
                        reason.StartsWith("Files changed:", StringComparison.Ordinal)
                        || reason.StartsWith("SDK metadata", StringComparison.Ordinal);
                }
                else
                {
                    await exit;
                    if (child.ExitCode == 0)
                        return 0;
                    // A failed initial build/argument/tool invocation needs a source fix,
                    // not a retry loop. Recover a session only after its host has run.
                    if (bootCount > 0 && !recoveryUsed)
                        reason = "Preview worker exited; recovering the session.";
                    else
                        Console.Error.WriteLine(
                            "[preview] Build failed. Save a source fix to rebuild; the displayed frame is stale."
                        );
                }
                if (reason is not null && !nativeChange && recoveryUsed)
                {
                    Console.Error.WriteLine(
                        "[preview] Recovery failed again. Save a source fix to rebuild; the displayed frame is stale."
                    );
                    reason = null;
                }
                if (reason is null)
                {
                    session.Cancel();
                    try
                    {
                        await changes;
                    }
                    catch (OperationCanceledException) { }
                    if (!child.HasExited)
                        await StopOwnedProcess(child);
                    // Managed edits also wake a failed build, although normally watch owns them.
                    await WaitForPreviewEdit(roots, PreviewFiles.Read(roots), Stopping.Token);
                    reason = "Source changed after failure; rebuilding.";
                    nativeChange = true;
                }
                Console.WriteLine(
                    $"[preview] {reason} Previous build {build} is stale{(nativeConfirmed ? "." : " (never confirmed).")}"
                );
                if (bootCount > 0)
                    Console.WriteLine(
                        "[preview] Restart recreates the application and resets in-memory state. Saved plugin data is retained."
                    );
                recoveryUsed = !nativeChange;
            }
            finally
            {
                session.Cancel();
                await StopOwnedProcess(child);
                await Task.WhenAll(stdout, stderr);
                try
                {
                    await changes;
                }
                catch (OperationCanceledException) { }
            }
            baseline = PreviewFiles.Read(roots);
            PreviewFiles.AddProjectRoots(project, roots);
        }
        return 0;
    }

    private static FileStream? AcquirePreviewWorkerLease(string platform) =>
        Environment.GetEnvironmentVariable("DOTNATIVE_PREVIEW_SUPERVISED") == "1"
            ? null
            : AcquireLease(platform);

    private static async Task WatchPreviewFiles(
        string project,
        HashSet<string> roots,
        Dictionary<string, (long Time, long Size)> baseline,
        TaskCompletionSource<string> restart,
        Func<bool> rebuildManagedSources,
        CancellationToken token
    )
    {
        bool RequiresRebuild(string path) =>
            rebuildManagedSources() || RequiresPreviewRebuild(path);
        var previous = baseline;
        while (true)
        {
            await Task.Delay(400, token);
            PreviewFiles.AddProjectRoots(project, roots);
            var next = PreviewFiles.Read(roots);
            var changed = PreviewFiles.Changes(previous, next);
            // dotnet watch owns ordinary C# edits; every other build input requires
            // a native/package rebuild. Ignore the files produced by that rebuild.
            var rebuild = changed.Where(RequiresRebuild).ToArray();
            if (rebuild.Length != 0)
            {
                // Batch editor saves; take another quiet snapshot before stopping a build.
                while (true)
                {
                    await Task.Delay(400, token);
                    var stable = PreviewFiles.Read(roots);
                    var stillChanging = PreviewFiles.Changes(next, stable).Any(RequiresRebuild);
                    next = stable;
                    if (!stillChanging)
                        break;
                }
                restart.TrySetResult(
                    "Files changed: "
                        + string.Join(", ", rebuild.Take(3).Select(Path.GetFileName))
                        + "; rebuilding the preview."
                );
                return;
            }
            previous = next;
        }
    }

    private static string[] PreviewWatchOptions() =>
        Environment.GetEnvironmentVariable("DOTNATIVE_PREVIEW_REBUILD_ONLY") == "1"
            ? ["--no-hot-reload"]
            : [];

    private static string[] PreviewBuildProperties(Dictionary<string, string> environment) =>
        environment
            .Where(property =>
                property.Key.StartsWith("DotNative", StringComparison.Ordinal)
                || property.Key is "DirectoryBuildPropsPath" or "DirectoryBuildTargetsPath"
            )
            .Select(property => $"--property:{property.Key}={property.Value}")
            .ToArray();

    private static async Task<string> PreviewAssemblyName(string project)
    {
        var name = (
            await Capture("dotnet", ["msbuild", project, "-getProperty:AssemblyName", "-nologo"])
        ).Trim();
        if (
            string.IsNullOrWhiteSpace(name)
            || name.Any(character => char.IsControl(character) || character is '"' or '\\')
        )
            throw new InvalidDataException(
                "Application AssemblyName is not a valid preview assembly name."
            );
        return name;
    }

    private static bool RequiresPreviewRebuild(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            return true;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        // Templates are SDK inputs, not projects loaded by the managed watcher.
        return path.StartsWith(
            Path.Combine(Root, "templates") + Path.DirectorySeparatorChar,
            comparison
        );
    }

    private static async Task WaitForPreviewEdit(
        IEnumerable<string> roots,
        Dictionary<string, (long Time, long Size)> baseline,
        CancellationToken token
    )
    {
        while (true)
        {
            await Task.Delay(400, token);
            if (PreviewFiles.Changes(baseline, PreviewFiles.Read(roots)).Any())
                return;
        }
    }
}

internal static class PreviewFiles
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "target",
        "build",
        "artifacts",
        ".git",
        ".gradle",
        "node_modules",
        ".dotnative",
        ".idea",
        ".vs",
        ".vscode",
    };

    internal static void AddProjectRoots(string project, HashSet<string> roots)
    {
        var folder = Path.GetDirectoryName(project)!;
        var assets = Path.Combine(folder, "obj", "project.assets.json");
        if (!File.Exists(assets))
            return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries))
                return;
            foreach (var library in libraries.EnumerateObject())
                if (
                    library.Value.TryGetProperty("msbuildProject", out var path)
                    && path.GetString() is { } relative
                )
                    lock (roots)
                        roots.Add(Path.GetDirectoryName(Path.GetFullPath(relative, folder))!);
        }
        catch (IOException) { } // Restore may atomically replace the file while we read it.
        catch (JsonException) { }
    }

    internal static Dictionary<string, (long Time, long Size)> Read(IEnumerable<string> roots)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        lock (roots)
            foreach (var root in roots.Distinct())
                Visit(root);
        return result;
        void Visit(string directory)
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                try
                {
                    var file = new FileInfo(entry);
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if ((file.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!Ignored.Contains(file.Name))
                            Visit(entry);
                    }
                    else if (
                        file.Extension is not (".md" or ".log")
                        && file.Name is not (".DS_Store" or "local.properties")
                    )
                    {
                        try
                        {
                            result[entry] = (file.LastWriteTimeUtc.Ticks, file.Length);
                        }
                        catch (FileNotFoundException) { } // Atomic editor replacement; the next snapshot sees it.
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
    }

    internal static IEnumerable<string> Changes(
        Dictionary<string, (long Time, long Size)> before,
        Dictionary<string, (long Time, long Size)> after
    ) =>
        before
            .Keys.Union(after.Keys)
            .Where(path =>
                !before.TryGetValue(path, out var old)
                || !after.TryGetValue(path, out var current)
                || old != current
            );
}
