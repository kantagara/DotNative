using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class StartupTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var transport = new Sink();
        Lifetime? singleton = null,
            scoped = null;
        DotNativeApplication? app = null;
        int mainCalls = 0;
        using (
            var session = ApplicationSession.Start(
                () =>
                {
                    mainCalls++;
                    var builder = DotNativeApplication.CreateBuilder();
                    builder.Services.AddSingleton<Lifetime>();
                    builder.Services.AddScoped<Scoped>();
                    builder.Services.AddTransient<Transient>();
                    app = builder.Build();
                    singleton = app.Services.GetRequiredService<Lifetime>();
                    scoped = app.Services.GetRequiredService<Scoped>();
                    Require(
                        ReferenceEquals(singleton, app.Services.GetRequiredService<Lifetime>())
                    );
                    Require(ReferenceEquals(scoped, app.Services.GetRequiredService<Scoped>()));
                    Require(
                        !ReferenceEquals(
                            app.Services.GetRequiredService<Transient>(),
                            app.Services.GetRequiredService<Transient>()
                        )
                    );
                    using (var other = app.Services.CreateScope())
                        Require(
                            !ReferenceEquals(
                                scoped,
                                other.ServiceProvider.GetRequiredService<Scoped>()
                            )
                        );
                    app.Run<InjectedRoot>();
                    Reject(() => app.Run<InjectedRoot>());
                },
                dispatcher,
                transport,
                320,
                600
            )
        )
        {
            session.Host.RenderNow();
            Require(mainCalls == 1 && singleton!.Disposals == 0 && scoped!.Disposals == 0);
            Require(InjectedRoot.Instances == 1);
        }
        Require(singleton!.Disposals == 1 && scoped!.Disposals == 1 && InjectedRoot.Disposals == 1);
        app!.Dispose();
        Require(singleton.Disposals == 1);

        // Failure after mounting still releases the root and all DI-owned services.
        Lifetime? failedService = null;
        Reject(() =>
            ApplicationSession.Start(
                () =>
                {
                    var builder = DotNativeApplication.CreateBuilder();
                    builder.Services.AddSingleton<Lifetime>();
                    builder.Services.AddScoped<Scoped>();
                    var failed = builder.Build();
                    failedService = failed.Services.GetRequiredService<Lifetime>();
                    failed.Run<InjectedRoot>();
                    throw new InvalidOperationException("Startup failure");
                },
                dispatcher,
                new Sink(),
                320,
                600
            )
        );
        Require(failedService!.Disposals == 1 && InjectedRoot.Disposals == 2);
        Reject(() => ApplicationSession.Start(() => { }, dispatcher, new Sink(), 320, 600));
        // DI owns a registered root; the application must not dispose it twice.
        using (
            ApplicationSession.Start(
                () =>
                {
                    var builder = DotNativeApplication.CreateBuilder();
                    builder.Services.AddSingleton<Lifetime>();
                    builder.Services.AddScoped<Scoped>();
                    builder.Services.AddScoped<InjectedRoot>();
                    builder.Build().Run<InjectedRoot>();
                },
                dispatcher,
                new Sink(),
                320,
                600
            )
        ) { }
        Require(InjectedRoot.Disposals == 3);
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new Exception("Startup assertion failed");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("Expected startup rejection");
    }

    private sealed class Sink : ICommandTransport
    {
        public void Flush(ReadOnlySpan<byte> buffer) { }
    }

    public class Lifetime : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    public sealed class Scoped : Lifetime { }

    public sealed class Transient { }

    public sealed class InjectedRoot(Lifetime singleton, Scoped scoped) : Component, IDisposable
    {
        public static int Instances;
        public static int Disposals;
        private readonly int _instance = ++Instances;

        public override Element Build() =>
            new Text($"{_instance}: {singleton.Disposals}, {scoped.Disposals}");

        public void Dispose() => Disposals++;
    }
}
