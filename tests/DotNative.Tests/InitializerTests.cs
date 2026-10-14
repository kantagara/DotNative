using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class InitializerTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var sink = new Sink();
        Probe? probe = null;
        DotNativeApplication? app = null;
        var session = ApplicationSession.Start(
            () =>
            {
                var builder = DotNativeApplication.CreateBuilder();
                builder.Services.AddSingleton<IApplicationInitializer>(_ =>
                    probe = new Probe(sink)
                );
                app = builder.Build();
                Require(
                    probe is not null && probe.Calls == 0,
                    "Build must resolve, but not start, initializers."
                );
                app.Run<Root>();
                Require(probe!.Calls == 1, "Run must start each initializer once.");
                try
                {
                    app.Run<Root>();
                    throw new Exception("Second Run accepted.");
                }
                catch (InvalidOperationException) { }
            },
            dispatcher,
            sink,
            320,
            600
        );
        Require(
            session.Startup.IsCompleted && !app!.Initialization.IsCompleted,
            "Run blocked native startup."
        );
        try
        {
            session.Dispose();
            throw new Exception("Sync shutdown accepted pending initialization.");
        }
        catch (InvalidOperationException) { }
        Pump(session.DisposeAsync().AsTask(), dispatcher);
        Require(
            probe!.Canceled && probe.Finished && probe.Disposals == 1,
            "Services disposed before initialization settled."
        );
        Require(app!.Initialization.IsCanceled, "Cancellation was hidden.");

        var secondStarted = false;
        var priorError = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            using var failed = ApplicationSession.Start(
                () =>
                {
                    var builder = DotNativeApplication.CreateBuilder();
                    builder.Services.AddSingleton<IApplicationInitializer>(
                        new Callback(() => throw new InvalidOperationException("startup-probe"))
                    );
                    builder.Services.AddSingleton<IApplicationInitializer>(
                        new Callback(() =>
                        {
                            secondStarted = true;
                            return Task.CompletedTask;
                        })
                    );
                    app = builder.Build();
                    app.Run<Root>();
                },
                dispatcher,
                new Sink(),
                320,
                600
            );
            Require(secondStarted, "A failed plugin prevented another plugin from starting.");
            Require(
                app!.Initialization.IsFaulted && errors.ToString().Contains("startup-probe"),
                "Failure was not observable and logged."
            );
        }
        finally
        {
            Console.SetError(priorError);
        }
    }

    private static void Pump(Task task, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Initializer shutdown stalled.");
            dispatcher.Drain();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    private sealed class Sink : ICommandTransport
    {
        public bool Rendered;

        public void Flush(ReadOnlySpan<byte> bytes) => Rendered = true;
    }

    public sealed class Root : Component
    {
        public override Element Build() => new Text("initializer");
    }

    private sealed class Callback(Func<Task> start) : IApplicationInitializer
    {
        public Task InitializeAsync(CancellationToken stopping) => start();
    }

    private sealed class Probe(Sink sink) : IApplicationInitializer, IDisposable
    {
        public int Calls,
            Disposals;
        public bool Canceled,
            Finished;

        public async Task InitializeAsync(CancellationToken stopping)
        {
            Calls++;
            Require(sink.Rendered, "Plugin started before the first frame.");
            try
            {
                await Task.Delay(Timeout.Infinite, stopping);
            }
            catch (OperationCanceledException)
            {
                Canceled = true;
                await Task.Yield();
                throw;
            }
            finally
            {
                Finished = true;
            }
        }

        public void Dispose()
        {
            Require(Finished, "DI released a plugin while its initialization was still running.");
            Disposals++;
        }
    }
}
