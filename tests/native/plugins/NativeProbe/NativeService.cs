using DotNative.Plugins;

namespace NativeProbe;

public sealed class NativeService(IPlatformChannels channels)
{
    public async Task<string> ReadAsync(CancellationToken cancellationToken = default) =>
        (string)
            (
                await channels
                    .Get("dotnative.plugins.nativeprobe")
                    .InvokeAsync("read", cancellationToken: cancellationToken)
            )!;
}
