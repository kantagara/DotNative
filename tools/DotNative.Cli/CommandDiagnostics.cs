using System.Diagnostics;

internal static partial class Preview
{
    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private static async Task<CommandResult> CommandOutput(string file, string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in args)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Cannot start {file}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
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
        return new(process.ExitCode, await output, await error);
    }

    private static async Task StopSimulatorApp(string device, string bundle)
    {
        var result = await CommandOutput("xcrun", ["simctl", "terminate", device, bundle]);
        if (result.ExitCode == 0)
            return;
        // simctl returns ESRCH when this specific app is already stopped.
        if (
            result.Error.Contains("NSPOSIXErrorDomain, code=3", StringComparison.Ordinal)
            && (
                result.Error.Contains("No such process", StringComparison.OrdinalIgnoreCase)
                || result.Error.Contains("nothing to terminate", StringComparison.OrdinalIgnoreCase)
            )
        )
            return;
        throw new IOException($"Could not stop simulator app {bundle}: {result.Error.Trim()}");
    }
}
