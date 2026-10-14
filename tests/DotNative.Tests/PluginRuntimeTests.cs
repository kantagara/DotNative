using DotNative;
using DotNative.Hosting;
using DotNative.Plugins;
using Microsoft.Extensions.DependencyInjection;

internal static class PluginRuntimeTests
{
    public static void Run()
    {
        Reject(() => _ = PluginRuntime.Current);
        var dispatcher = new Dispatcher();
        ExecutionContext? stale = null;
        IApplicationActivations? activations = null;
        IPlatformChannels? firstChannels = null;
        using var first = ApplicationSession.Start(() =>
        {
            firstChannels = PluginRuntime.Current.Channels;
            Check(PluginRuntime.Current.Target.Platform == NativePlatform.Android);
            var app = DotNativeApplication.CreateBuilder().Build();
            activations = app.Services.GetRequiredService<IApplicationActivations>();
            app.Run<Root>();
        }, dispatcher, new Sink(NativePlatform.Android), 320, 600);
        Reject(() => _ = PluginRuntime.Current);
        var callbacks = 0;
        activations!.Opened += _ =>
        {
            Check(ReferenceEquals(PluginRuntime.Current.Channels, firstChannels));
            Check(Task.Run(async () =>
            {
                await Task.Yield();
                return ReferenceEquals(PluginRuntime.Current.Channels, firstChannels);
            }).GetAwaiter().GetResult());
            stale = ExecutionContext.Capture();
            callbacks++;
        };
        using (var second = ApplicationSession.Start(() =>
        {
            Check(!ReferenceEquals(PluginRuntime.Current.Channels, firstChannels));
            Check(PluginRuntime.Current.Target.Platform == NativePlatform.IOS);
            DotNativeApplication.CreateBuilder().Build().Run<Root>();
        }, dispatcher, new Sink(NativePlatform.IOS), 320, 600))
        {
            first.Host.DispatchInput(0, NativeEventKind.OpenUri, 0, "https://example.test/");
            Check(callbacks == 1);
        }
        first.Dispose();
        ExecutionContext.Run(stale!, _ => Reject(() => _ = PluginRuntime.Current), null);
        Reject(() => _ = PluginRuntime.Current);
        using var restarted = ApplicationSession.Start(() =>
        {
            Check(!ReferenceEquals(PluginRuntime.Current.Channels, firstChannels));
            DotNativeApplication.CreateBuilder().Build().Run<Root>();
        }, dispatcher, new Sink(NativePlatform.Android), 320, 600);
    }
    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Plugin runtime context mismatch");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected unavailable runtime context");
    }
    private sealed class Root : Component
    {
        public override Node Build(BuildContext context) => new Text("Ready");
    }
    private sealed class Sink(NativePlatform platform) : ICommandTransport
    {
        public PresentationTarget Target { get; } = new(platform);
        public void Flush(ReadOnlySpan<byte> bytes) { }
    }
}
