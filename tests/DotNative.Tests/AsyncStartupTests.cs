using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class AsyncStartupTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        int thread = Environment.CurrentManagedThreadId;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AsyncService? service = null;
        var session = ApplicationSession.Begin(
            async () =>
            {
                var builder = DotNativeApplication.CreateBuilder();
                builder.Services.AddSingleton<AsyncService>();
                var app = builder.Build();
                service = app.Services.GetRequiredService<AsyncService>();
                await gate.Task;
                Require(Environment.CurrentManagedThreadId == thread);
                app.Run<Root>();
            },
            dispatcher,
            new Sink(),
            320,
            600
        );
        Require(!session.Startup.IsCompleted && session.MountedHost is null);
        session.Resize(600, 320);
        gate.SetResult();
        Pump(session.Startup, dispatcher);
        Require(session.Host is not null);
        var stopping = session.DisposeAsync().AsTask();
        Require(ReferenceEquals(stopping, session.DisposeAsync().AsTask()));
        Require(session.MountedHost is null);
        Pump(stopping, dispatcher);
        Require(service!.Disposals == 1);

        // Stop cancels pending initialization. Main must never mount afterwards.
        bool canceled = false;
        var pending = ApplicationSession.Begin(
            async () =>
            {
                var app = DotNativeApplication.CreateBuilder().Build();
                try
                {
                    await Task.Delay(Timeout.Infinite, app.Stopping);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    throw;
                }
                app.Run<Root>();
            },
            dispatcher,
            new Sink(),
            320,
            600
        );
        Pump(pending.DisposeAsync().AsTask(), dispatcher);
        Require(canceled && pending.MountedHost is null);

        // Failure after await is observed, and async-only dependencies are released.
        AsyncService? failed = null;
        var failure = ApplicationSession.Begin(
            async () =>
            {
                var builder = DotNativeApplication.CreateBuilder();
                builder.Services.AddSingleton<AsyncService>();
                var app = builder.Build();
                failed = app.Services.GetRequiredService<AsyncService>();
                await Task.Yield();
                throw new InvalidOperationException("after await");
            },
            dispatcher,
            new Sink(),
            320,
            600
        );
        var cleanup = failure.DisposeAsync().AsTask();
        try
        {
            Pump(cleanup, dispatcher);
            throw new Exception("Expected failure");
        }
        catch (AggregateException) { }
        Require(failed!.Disposals == 1);

        // Building after an await retains the startup session in ExecutionContext.
        var delayedBuild = ApplicationSession.Begin(
            async () =>
            {
                await Task.Yield();
                DotNativeApplication.CreateBuilder().Build().Run<Root>();
            },
            dispatcher,
            new Sink(),
            320,
            600
        );
        Pump(delayedBuild.Startup, dispatcher);
        Pump(delayedBuild.DisposeAsync().AsTask(), dispatcher);
    }

    private static void Pump(Task task, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("UI continuation stalled");
            dispatcher.Drain();
            Thread.Sleep(1); // Test-only event-loop pump; production never blocks.
        }
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Async startup assertion failed");
    }

    private sealed class Sink : ICommandTransport
    {
        public void Flush(ReadOnlySpan<byte> bytes) { }
    }

    public sealed class Root : Component
    {
        public override Element Build() => new Text("async");
    }

    public sealed class AsyncService : IAsyncDisposable
    {
        public int Disposals;

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposals++;
        }
    }
}
