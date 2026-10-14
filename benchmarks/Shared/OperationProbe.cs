using System.Diagnostics;
using System.Globalization;

namespace FrameworkBenchmarks;

// Measures synchronous managed API work only. It deliberately does not call
// this frame time, presentation latency, startup time or FPS. Native platform
// tools collect those independently, outside these application markers.
public static class OperationProbe
{
    public static double Run(string framework, string action, Action operation)
    {
        var allocated = GC.GetTotalAllocatedBytes(false);
        var start = Stopwatch.GetTimestamp();
        operation();
        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bytes = GC.GetTotalAllocatedBytes(false) - allocated;
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"BENCH_OP contract={Workload.Contract} framework={framework} action={action} managed_ms={milliseconds:F6} managed_allocated_bytes={bytes}"
            )
        );
        return milliseconds;
    }
}
