using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

internal static class CoreRegression
{
    internal static void Run(string applicationDirectory, string outputDirectory)
    {
        var app = Path.GetFullPath(applicationDirectory);
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var executable = Path.Combine(app, "WidgetSamples.exe");
        if (!File.Exists(executable))
            executable = Path.Combine(app, "dotnative-desktop.exe");
        if (
            !File.Exists(executable)
            || !File.Exists(Path.Combine(app, "managed", "DotNative.DevHost.exe"))
        )
            throw new ArgumentException(
                "Expected a renderer with the WidgetSamples host in managed/."
            );
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = app,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNATIVE_PREVIEW_PORT"] = "14976";
        start.Environment["DOTNATIVE_WINUI_NAVIGATION_TEST"] = "1";
        start.Environment["DOTNATIVE_WINUI_WINDOW_TEST"] = "1";
        using var process = Process.Start(start)!;
        var lines = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var stdout = Pump(process.StandardOutput, Path.Combine(output, "app.log"), lines);
        var stderr = Pump(process.StandardError, Path.Combine(output, "app.err"), errors);
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (
                    errors.Any(line =>
                        line.Contains("FAIL")
                        || line.Contains("Exception")
                        || line.Contains("fatal")
                    )
                )
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
                if (
                    lines.Any(line =>
                        line.StartsWith(
                            "PASS WidgetSamples radio selection/grouping/state + Home/Cart"
                        )
                    ) && lines.Any(line => line.StartsWith("PASS native window move/resize loop"))
                )
                {
                    var result = string.Join(
                        Environment.NewLine,
                        lines.Where(line => line.StartsWith("PASS "))
                    );
                    File.WriteAllText(Path.Combine(output, "result.txt"), result);
                    Console.WriteLine(result);
                    return;
                }
                if (process.HasExited)
                    throw new InvalidOperationException(
                        $"Renderer exited: {process.ExitCode}. See {output}."
                    );
                Thread.Sleep(100);
            }
            throw new TimeoutException($"Core regression timed out. See {output}.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        }
    }

    private static async Task Pump(StreamReader reader, string path, ConcurrentQueue<string> lines)
    {
        await using var writer = new StreamWriter(path) { AutoFlush = true };
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Enqueue(line);
            await writer.WriteLineAsync(line);
        }
    }
}
