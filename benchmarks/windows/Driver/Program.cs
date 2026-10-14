using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;

if (args.Length == 3 && args[0] == "--core-regression")
{
    CoreRegression.Run(args[1], args[2]);
    return;
}

if (args.Length != 6)
    throw new ArgumentException(
        "Expected framework, executable, output, iterations, preview port, VM flag"
    );
var framework = args[0];
var executable = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var iterations = int.Parse(args[3]);
var port = ushort.Parse(args[4]);
var rounds = int.Parse(Environment.GetEnvironmentVariable("DOTNATIVE_BENCH_ROUNDS") ?? "1");
if (rounds is < 1 or > 100)
    throw new ArgumentException("Mutation rounds must be 1..100");
if (iterations < 1 || port == 0 || framework is not ("dotnative" or "maui"))
    throw new ArgumentException("Invalid benchmark arguments");
Directory.CreateDirectory(output);
var samples = new List<object>();
void Save(string name, object value) =>
    File.WriteAllText(
        Path.Combine(output, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })
    );
string? HashSibling(string relative)
{
    var path = Path.Combine(Path.GetDirectoryName(executable)!, relative);
    return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
}
Save(
    "metadata.json",
    new
    {
        contract = "dotnative-maui-v4",
        framework,
        platform = "windows",
        iterations,
        mutation_rounds = rounds,
        virtual_machine = bool.Parse(args[5]),
        smoke_only = true,
        os = Environment.OSVersion.VersionString,
        os_architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        driver_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        executable_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))),
        renderer_sha256 = HashSibling("DotNative.WinUI.dll"),
        managed_host_sha256 = HashSibling("managed/DotNative.DevHost.exe"),
        metric = "UI Automation action-to-new-status latency; not frame time or FPS",
        startup_metric = "launch to Ready accessibility text; includes 30ms polling overhead",
        memory_scope = "application plus its bundled managed host, when present",
        private_bytes_metric = "private committed bytes, not private resident RAM",
        private_working_set_metric = "resident non-sharable pages from QueryWorkingSet",
        client_size = new { width = 1100, height = 1000 },
        readiness = "Ready status plus visible first row; Jump/End require a visible target row",
        runtime_mode = Environment.GetEnvironmentVariable("DOTNATIVE_BENCH_RUNTIME_MODE")
            ?? "unknown",
        build_revision = Environment.GetEnvironmentVariable("DOTNATIVE_BENCH_BUILD_REVISION")
            ?? "unknown",
        process_architecture = BenchmarkBinary.Architecture(executable),
        application_directory_bytes = BenchmarkBinary.DirectoryBytes(
            Path.GetDirectoryName(executable)!
        ),
    }
);
AutomationElement? Named(AutomationElement root, string name, bool button = false)
{
    Condition condition = new AndCondition(
        Automation.ControlViewCondition,
        new PropertyCondition(AutomationElement.NameProperty, name)
    );
    if (button)
        condition = new AndCondition(
            condition,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
        );
    return root.FindFirst(TreeScope.Descendants, condition);
}
T Wait<T>(Func<T?> query)
    where T : class
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed.TotalSeconds < 30)
    {
        if (query() is { } result)
            return result;
        Thread.Sleep(30);
    }
    throw new TimeoutException("UI condition timed out; keep the test desktop unlocked");
}
Process[] Processes(Process root)
{
    var expected = Path.GetFullPath(
        Path.Combine(Path.GetDirectoryName(executable)!, "managed", "DotNative.DevHost.exe")
    );
    var result = new List<Process> { root };
    if (framework == "dotnative")
        foreach (var child in Process.GetProcessesByName("DotNative.DevHost"))
        {
            try
            {
                if (
                    string.Equals(
                        child.MainModule?.FileName,
                        expected,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && child.StartTime >= root.StartTime
                )
                    result.Add(child);
                else
                    child.Dispose();
            }
            catch (InvalidOperationException)
            {
                child.Dispose();
            }
        }
    return result.ToArray();
}
for (var iteration = 0; iteration < iterations; iteration++)
{
    Process? process = null;
    Task<string>? stdout = null,
        stderr = null;
    try
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (framework == "dotnative")
            start.Environment["DOTNATIVE_PREVIEW_PORT"] = port.ToString();
        var timer = Stopwatch.StartNew();
        process = Process.Start(start)!;
        stdout = process.StandardOutput.ReadToEndAsync();
        stderr = process.StandardError.ReadToEndAsync();
        var window = Wait(() =>
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException($"Application exited: {process.ExitCode}");
            if (process.MainWindowHandle == 0)
                return null;
            var root = AutomationElement.FromHandle(process.MainWindowHandle);
            var first = Named(root, "Item 00000 · 0", true);
            return
                Named(root, "Ready 10000 v4") != null
                && first is not null
                && !first.Current.IsOffscreen
                ? root
                : null;
        });
        samples.Add(
            new
            {
                iteration,
                action = "startup",
                elapsed_ms = timer.Elapsed.TotalMilliseconds,
            }
        );
        // Match the client area, not framework-specific outer window chrome.
        BenchmarkWindow.SetClientSize(process.MainWindowHandle, 1100, 1000);
        Thread.Sleep(250);
        var generation = 0;
        var count = 10000;
        for (int round = 0; round < rounds; round++)
            foreach (
                var action in new[]
                {
                    "Jump",
                    "End",
                    "Variable",
                    "Jump",
                    "Update",
                    "Expand",
                    "Expand",
                    "Prepend",
                    "Reverse",
                    "Reverse",
                    "Clear",
                    "Jump",
                    "Reset",
                }
            )
            {
                var button =
                    Named(window, action, true)
                    ?? throw new InvalidOperationException($"Missing button {action}");
                var invoke = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
                count = action switch
                {
                    "Prepend" => count + 100,
                    "Clear" => 0,
                    "Reset" => 10000,
                    _ => count,
                };
                generation++;
                timer.Restart();
                invoke.Invoke();
                Wait(() => Named(window, $"Done {generation} {action} {count}"));
                if (action is "Jump" or "End" && count > 0)
                {
                    var title = action == "End" ? "Item 09999 · 0" : "Item 05000 · 0";
                    Wait(() =>
                    {
                        var row = Named(window, title, true);
                        return row is not null && !row.Current.IsOffscreen ? row : null;
                    });
                }
                var elapsed = timer.Elapsed.TotalMilliseconds;
                var tree = Processes(process);
                try
                {
                    // Process caches memory counters; every action needs a fresh snapshot.
                    foreach (var sampledProcess in tree)
                        sampledProcess.Refresh();
                    var memory = tree.Select(p => new
                        {
                            process = p.ProcessName,
                            working_set_bytes = p.WorkingSet64,
                            private_bytes = p.PrivateMemorySize64,
                            private_working_set_bytes = BenchmarkMemory.PrivateWorkingSet(p),
                        })
                        .ToArray();
                    samples.Add(
                        new
                        {
                            iteration,
                            round,
                            action,
                            elapsed_ms = elapsed,
                            process_count = tree.Length,
                            working_set_bytes = memory.Sum(p => p.working_set_bytes),
                            private_bytes = memory.Sum(p => p.private_bytes),
                            private_working_set_bytes = memory.Sum(p =>
                                p.private_working_set_bytes
                            ),
                            processes = memory,
                        }
                    );
                }
                finally
                {
                    foreach (var child in tree.Where(p => p != process))
                        child.Dispose();
                }
            }
    }
    catch (Exception error)
    {
        File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
        throw;
    }
    finally
    {
        if (process != null)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
            if (stdout != null)
                File.WriteAllText(Path.Combine(output, $"app-{iteration}.log"), await stdout);
            if (stderr != null)
                File.WriteAllText(Path.Combine(output, $"app-{iteration}.err"), await stderr);
            process.Dispose();
        }
        Save("iterations.json", samples);
    }
}
File.WriteAllText(
    Path.Combine(output, "result.txt"),
    "PASS Windows startup and mutation journeys (UI latency, not FPS)"
);

internal static class BenchmarkWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window,
        nint after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    internal static void SetClientSize(nint window, int width, int height)
    {
        if (
            !GetClientRect(window, out var client)
            || !GetWindowRect(window, out var outer)
            || !SetWindowPos(
                window,
                0,
                0,
                0,
                width + (outer.Right - outer.Left) - (client.Right - client.Left),
                height + (outer.Bottom - outer.Top) - (client.Bottom - client.Top),
                0x16
            )
        )
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (
            !GetClientRect(window, out client)
            || client.Right - client.Left != width
            || client.Bottom - client.Top != height
        )
            throw new InvalidOperationException("Benchmark client sizes do not match");
    }
}

internal static class BenchmarkMemory
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32QueryWorkingSet(nint process, nint buffer, uint size);

    internal static long PrivateWorkingSet(Process process)
    {
        int bytes = checked(
            (int)
                Math.Max(
                    65536,
                    (process.WorkingSet64 / Environment.SystemPageSize + 4096) * nint.Size
                )
        );
        for (int attempt = 0; attempt < 8; attempt++, bytes = checked(bytes * 2))
        {
            nint buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                if (!K32QueryWorkingSet(process.Handle, buffer, (uint)bytes))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 24)
                        continue; // Working set grew beyond the buffer.
                    throw new System.ComponentModel.Win32Exception(error);
                }
                long count = Marshal.ReadIntPtr(buffer).ToInt64();
                if (count < 0 || count > bytes / nint.Size - 1)
                    throw new InvalidOperationException("Invalid working-set entry count");
                long pages = 0;
                for (int i = 0; i < count; i++)
                    // PSAPI_WORKING_SET_BLOCK.Shared is bit 8 on both architectures.
                    if ((Marshal.ReadIntPtr(buffer, (i + 1) * nint.Size).ToInt64() & 0x100) == 0)
                        pages++;
                return pages * Environment.SystemPageSize;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        throw new InvalidOperationException("Working set changed too quickly to sample");
    }
}

internal static class BenchmarkBinary
{
    internal static string Architecture(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0x5A4D)
            throw new InvalidDataException("Expected PE executable.");
        reader.BaseStream.Position = 0x3c;
        var offset = reader.ReadInt32();
        reader.BaseStream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("Invalid PE signature.");
        return reader.ReadUInt16() switch
        {
            0x8664 => "x64",
            0xAA64 => "arm64",
            0x14c => "x86",
            var machine => $"unknown-{machine:X4}",
        };
    }

    internal static long DirectoryBytes(string directory) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is not (".log" or ".err" or ".pdb"))
            .Sum(path => new FileInfo(path).Length);
}
