using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed record Device(
    string Id,
    string Name,
    string Platform,
    string Kind,
    string State,
    string Architecture,
    bool CanPreview,
    string? UsbId = null
);

internal static class Devices
{
    public static string AndroidSdk =>
        Environment.GetEnvironmentVariable("ANDROID_HOME")
        ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
        ?? (
            OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Android",
                    "Sdk"
                )
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    OperatingSystem.IsMacOS() ? "Library/Android/sdk" : "Android/Sdk"
                )
        );

    public static async Task<List<Device>> Discover()
    {
        var devices = new List<Device>();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            var platform = OperatingSystem.IsWindows() ? "windows" : "linux";
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            devices.Add(
                new(
                    platform,
                    Environment.MachineName,
                    platform,
                    "desktop",
                    "ready",
                    arch,
                    arch is "x64" or "arm64"
                )
            );
        }
        if (OperatingSystem.IsMacOS())
        {
            var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            devices.Add(
                new(
                    "mac",
                    Environment.MachineName,
                    "mac",
                    "desktop",
                    "ready",
                    arch,
                    arch == "arm64"
                )
            );
            await Probe(
                "iOS simulators",
                async () =>
                {
                    using var json = JsonDocument.Parse(
                        await Read("xcrun", ["simctl", "list", "devices", "available", "--json"])
                    );
                    foreach (
                        var runtime in json.RootElement.GetProperty("devices").EnumerateObject()
                    )
                    {
                        if (!runtime.Name.Contains(".iOS-", StringComparison.Ordinal))
                            continue;
                        foreach (var item in runtime.Value.EnumerateArray())
                            devices.Add(
                                new(
                                    item.GetProperty("udid").GetString()!,
                                    item.GetProperty("name").GetString()!,
                                    "ios",
                                    "simulator",
                                    item.GetProperty("state").GetString()!,
                                    arch,
                                    arch == "arm64"
                                )
                            );
                    }
                }
            );
            await Probe(
                "iOS devices",
                async () =>
                {
                    var path = Path.Combine(
                        Path.GetTempPath(),
                        "dotnative-devices-" + Guid.NewGuid() + ".json"
                    );
                    try
                    {
                        await Read(
                            "xcrun",
                            ["devicectl", "list", "devices", "--json-output", path]
                        );
                        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                        foreach (
                            var item in json
                                .RootElement.GetProperty("result")
                                .GetProperty("devices")
                                .EnumerateArray()
                        )
                        {
                            var hw = item.GetProperty("hardwareProperties");
                            if (hw.GetProperty("platform").GetString() != "iOS")
                                continue;
                            devices.Add(
                                new(
                                    item.GetProperty("identifier").GetString()!,
                                    item.GetProperty("deviceProperties")
                                        .GetProperty("name")
                                        .GetString()!,
                                    "ios",
                                    "physical",
                                    item.GetProperty("connectionProperties")
                                        .GetProperty("tunnelState")
                                        .GetString()!,
                                    hw.GetProperty("cpuType").GetProperty("name").GetString()!,
                                    item.GetProperty("deviceProperties")
                                        .GetProperty("developerModeStatus")
                                        .GetString() == "enabled",
                                    hw.GetProperty("udid").GetString()
                                )
                            );
                        }
                    }
                    finally
                    {
                        File.Delete(path);
                    }
                }
            );
        }
        var adb = Path.Combine(
            AndroidSdk,
            "platform-tools",
            OperatingSystem.IsWindows() ? "adb.exe" : "adb"
        );
        if (File.Exists(adb))
            await Probe(
                "Android devices",
                async () =>
                {
                    foreach (var line in (await Read(adb, ["devices", "-l"])).Split('\n').Skip(1))
                    {
                        var parts = line.Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        if (parts.Length < 2)
                            continue;
                        var id = parts[0];
                        var state = parts[1];
                        var model =
                            parts
                                .FirstOrDefault(x => x.StartsWith("model:"))
                                ?[6..].Replace('_', ' ')
                            ?? id;
                        var arch =
                            state == "device"
                                ? (
                                    await Read(
                                        adb,
                                        ["-s", id, "shell", "getprop", "ro.product.cpu.abi"]
                                    )
                                ).Trim()
                                : "unknown";
                        var emulator = id.StartsWith("emulator-", StringComparison.Ordinal);
                        devices.Add(
                            new(
                                id,
                                model,
                                "android",
                                emulator ? "emulator" : "physical",
                                state,
                                arch,
                                OperatingSystem.IsMacOS()
                                    && state == "device"
                                    && arch == "arm64-v8a"
                            )
                        );
                    }
                }
            );
        else
            Console.Error.WriteLine(
                "Android discovery unavailable: install platform-tools or set ANDROID_HOME."
            );
        return devices;
    }

    public static void Print(IReadOnlyList<Device> devices)
    {
        Console.WriteLine(
            "ID                                      PLATFORM  KIND       STATE         ARCH         NAME"
        );
        foreach (var d in devices)
            Console.WriteLine(
                $"{d.Id, -39} {d.Platform, -9} {d.Kind, -10} {d.State, -13} {d.Architecture, -12} {d.Name}{(d.CanPreview ? "" : " [hot reload unavailable]")}"
            );
        Console.WriteLine("Select with: dotnative run --device <id> --hot-reload");
    }

    public static Device Select(IReadOnlyList<Device> devices, string? id, string? platform)
    {
        var matches = devices.Where(d => platform is null || d.Platform == platform).ToList();
        if (id is not null)
        {
            matches =
                id == "booted"
                    ? matches
                        .Where(d =>
                            d.Platform == "ios" && d.Kind == "simulator" && d.State == "Booted"
                        )
                        .ToList()
                    : matches
                        .Where(d =>
                            d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                            || d.Name.Equals(id, StringComparison.OrdinalIgnoreCase)
                        )
                        .ToList();
        }
        else
            matches = matches
                .Where(d => d.CanPreview && d.State is "ready" or "Booted" or "device")
                .ToList();
        if (matches.Count == 1)
            return matches[0];
        if (matches.Count == 0)
            throw new ArgumentException(
                "No matching device. Run dotnative devices and select an available ID."
            );
        Print(matches);
        if (id is not null || Console.IsInputRedirected)
            throw new ArgumentException(
                "Multiple devices match. Select an exact ID with --device."
            );
        Console.WriteLine("Choose a device:");
        for (int i = 0; i < matches.Count; i++)
            Console.WriteLine($"  {i + 1}. {matches[i].Name} ({matches[i].Id})");
        Console.Write("Device number: ");
        if (
            !int.TryParse(Console.ReadLine(), out var selection)
            || selection < 1
            || selection > matches.Count
        )
            throw new ArgumentException("Invalid device selection.");
        return matches[selection - 1];
    }

    private static async Task Probe(string name, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"{name} discovery unavailable: {error.Message}");
        }
    }

    private static async Task<string> Read(string command, string[] args)
    {
        var info = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException($"Cannot start {command}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new IOException($"{command} discovery timed out.");
        }
        if (process.ExitCode != 0)
            throw new IOException((await stderr).Trim());
        return await stdout;
    }
}
