using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class LifecycleTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var seen = new List<ApplicationActivity>();
        IApplicationLifecycle? lifecycle = null;
        using var session = ApplicationSession.Start(
            () =>
            {
                var app = DotNativeApplication.CreateBuilder().Build();
                lifecycle = app.Services.GetRequiredService<IApplicationLifecycle>();
                lifecycle.Changed += seen.Add;
                app.Run<Root>();
            },
            dispatcher,
            new Sink(),
            320,
            600
        );
        session.SetActivity(ApplicationActivity.Active);
        session.SetActivity(ApplicationActivity.Active);
        session.SetActivity(ApplicationActivity.Inactive);
        session.SetActivity(ApplicationActivity.Background);
        session.SetActivity(ApplicationActivity.Inactive);
        session.SetActivity(ApplicationActivity.Active);
        var shutdown = session.DisposeAsync().AsTask();
        Pump(shutdown, dispatcher);
        Require(
            seen.SequenceEqual(
                new[]
                {
                    ApplicationActivity.Active,
                    ApplicationActivity.Inactive,
                    ApplicationActivity.Background,
                    ApplicationActivity.Inactive,
                    ApplicationActivity.Active,
                    ApplicationActivity.Stopping,
                }
            )
        );
        session.SetActivity(ApplicationActivity.Active);
        Require(lifecycle!.Activity == ApplicationActivity.Stopping);
    }

    private static void Pump(Task task, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            dispatcher.Drain();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Lifecycle assertion failed");
    }

    private sealed class Sink : ICommandTransport
    {
        public void Flush(ReadOnlySpan<byte> bytes) { }
    }

    public sealed class Root : Component
    {
        public override Element Build() => new Text("lifecycle");
    }
}
