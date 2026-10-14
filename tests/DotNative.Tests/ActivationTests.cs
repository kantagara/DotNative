using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class ActivationTests
{
    public static void Run()
    {
        IApplicationActivations? source = null;
        using var session = ApplicationSession.Start(
            () =>
            {
                var app = DotNativeApplication.CreateBuilder().Build();
                source = app.Services.GetRequiredService<IApplicationActivations>();
                app.Run<Root>();
            },
            new Dispatcher(),
            new Sink(),
            320,
            600
        );
        session.Host.DispatchInput(0, NativeEventKind.OpenUri, 0, "https://example.test/game/1");
        var pending = source!.ReadPending();
        Check(pending.Count == 1 && pending[0].Kind == ApplicationActivationKind.Uri);
        Check(source.ReadPending().Count == 0);
        ApplicationActivation? opened = null;
        source.Opened += value => opened = value;
        session.Host.DispatchInput(0, NativeEventKind.OpenNotification, 0, "{payload}");
        Check(
            opened?.Kind == ApplicationActivationKind.Notification && opened.Payload == "{payload}"
        );
        session.Host.DispatchInput(0, NativeEventKind.OpenUri, 0, new string('x', 16385));
        Check(opened?.Kind == ApplicationActivationKind.Notification);
        Check(source.ReadPending().Count == 0);
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Activation transport contract");
    }

    private sealed class Root : Component
    {
        public override Node Build(BuildContext context) => new Text("Ready");
    }

    private sealed class Sink : ICommandTransport
    {
        public void Flush(ReadOnlySpan<byte> bytes) { }
    }
}
