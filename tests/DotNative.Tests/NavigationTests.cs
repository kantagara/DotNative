using System.Buffers.Binary;
using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class NavigationTests
{
    public static void Run()
    {
        RemotePresentationSelection();
        NativeTransitionOwnership();
        var dispatcher = new Dispatcher();
        Navigator? navigator = null;
        using var session = ApplicationSession.Start(
            () =>
            {
                var builder = DotNativeApplication.CreateBuilder();
                builder.Services.AddSingleton(new PresentationTarget(NativePlatform.Unknown));
                builder.Services.AddDotNativeNavigation();
                builder.Services.AddScoped<ScreenData>();
                var app = builder.Build();
                navigator = app.Services.GetRequiredService<Navigator>();
                navigator.PushAsync<Screen>().GetAwaiter().GetResult();
                app.Run<NavigationHost>();
            },
            dispatcher,
            new NativeModel(),
            320,
            600
        );
        var first = (Screen)navigator!.Current!;
        first.Data.Count.Value = 42;
        navigator.PushAsync<Screen>().GetAwaiter().GetResult();
        dispatcher.Drain();
        var second = (Screen)navigator.Current!;
        Require(!ReferenceEquals(first, second) && !ReferenceEquals(first.Data, second.Data));
        Require(navigator.Count == 2 && navigator.CanPop);
        Require(navigator.PopAsync().GetAwaiter().GetResult());
        dispatcher.Drain();
        Require(ReferenceEquals(first, navigator.Current) && first.Data.Count.Value == 42);
        Require(second.Disposals == 1 && second.Data.Disposals == 1);
        Require(!navigator.PopAsync().GetAwaiter().GetResult());
        try
        {
            navigator.PushAsync<BrokenScreen>().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException) { }
        Require(navigator.Count == 1 && ScreenData.Last!.Disposals == 1);
        // Async-only screen disposal is awaited before disposing its dependencies.
        navigator.PushAsync<AsyncScreen>().GetAwaiter().GetResult();
        var asynchronous = (AsyncScreen)navigator.Current!;
        Pump(navigator.PopAsync(), dispatcher);
        Require(asynchronous.Disposed && asynchronous.Data.Disposals == 1);
        Pump(session.DisposeAsync().AsTask(), dispatcher);
        Require(first.Disposals == 1 && first.Data.Disposals == 1);
    }

    private static void RemotePresentationSelection()
    {
        foreach (
            var platform in new[]
            {
                NativePlatform.IOS,
                NativePlatform.Android,
                NativePlatform.MacOS,
                NativePlatform.Windows,
                NativePlatform.Linux,
            }
        )
        {
            var dispatcher = new Dispatcher();
            var native = new NativeModel();
            using var session = ApplicationSession.Start(
                () =>
                {
                    var builder = DotNativeApplication.CreateBuilder();
                    builder.Services.AddSingleton(new PresentationTarget(platform));
                    builder.Services.AddDotNativeNavigation();
                    builder.Services.AddScoped<ScreenData>();
                    var app = builder.Build();
                    var navigator = app.Services.GetRequiredService<Navigator>();
                    navigator.PushAsync<Screen>().GetAwaiter().GetResult();
                    app.Run<NavigationHost>();
                },
                dispatcher,
                native,
                320,
                600
            );
            dispatcher.Drain();
            Require(native.Nodes.Values.Any(node => node.Kind == 10));
            {
                var transition = native
                    .Nodes.Values.Single(node => node.Kind == 10)
                    .NavigationTransition;
                Require(
                    transition.Length == 6
                        && transition[0] == (byte)NavigationTransitionKind.PlatformDefault
                        && Math.Abs(
                            BinaryPrimitives.ReadSingleLittleEndian(transition.AsSpan(1)) - 0.18f
                        ) < 0.001f
                        && transition[5] == (byte)TransitionEasing.EaseInOut
                );
            }
            Pump(session.DisposeAsync().AsTask(), dispatcher);
        }
    }

    private static void NativeTransitionOwnership()
    {
        var dispatcher = new Dispatcher();
        using var services = new ServiceCollection().AddScoped<ScreenData>().BuildServiceProvider();
        var navigator = new Navigator(
            services.GetRequiredService<IServiceScopeFactory>(),
            dispatcher
        )
        {
            NativePresentation = true,
            DefaultOptions = new ScreenOptions
            {
                Transition = new NavigationTransition
                {
                    Kind = NavigationTransitionKind.Fade,
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = TransitionEasing.EaseOutCubic,
                },
            },
        };
        var context = new DotNative.Hosting.DispatcherSynchronizationContext(dispatcher);
        Task operation = Task.CompletedTask;
        context.Run(() => operation = navigator.PushAsync<Screen>("First"));
        Pump(operation, dispatcher);
        context.Run(() => operation = navigator.PushAsync<Screen>("Second"));
        var second = (Screen)navigator.Current!;
        Require(!operation.IsCompleted && second.Data.Disposals == 0);
        var lifetime = new BuildContext.Lifetime();
        var buildContext = new BuildContext(lifetime);
        NativeNavigationNode Presentation() =>
            (NativeNavigationNode)navigator.BuildPresentation(buildContext);
        Require(
            Presentation().TransitionOptions
                == new NavigationTransition
                {
                    Kind = NavigationTransitionKind.Fade,
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = TransitionEasing.EaseOutCubic,
                }
        );
        context.Run(() => Presentation().Shown(2));
        Pump(operation, dispatcher);
        // UIKit cancelled an edge swipe: report the unchanged stack depth.
        context.Run(() =>
        {
            Presentation().Transition(true);
            Presentation().Shown(2);
        });
        Require(
            navigator.Count == 2
                && ReferenceEquals(navigator.Current, second)
                && second.Data.Disposals == 0
        );
        // A programmatic pop must retain both native pages until didShow.
        context.Run(() => operation = navigator.PopAsync());
        Require(navigator.Count == 2 && Presentation().Depth == 1 && second.Data.Disposals == 0);
        context.Run(() => Presentation().Shown(1));
        Pump(operation, dispatcher);
        Require(navigator.Count == 1 && second.Disposals == 1 && second.Data.Disposals == 1);
        // A completed native Back owns the same cleanup path.
        context.Run(() => operation = navigator.PushAsync<Screen>("Third"));
        context.Run(() => Presentation().Shown(2));
        Pump(operation, dispatcher);
        var third = (Screen)navigator.Current!;
        context.Run(() => Presentation().Shown(1));
        Require(navigator.Count == 1 && third.Data.Disposals == 1);
        context.Run(() => operation = navigator.DisposeAsync().AsTask());
        Pump(operation, dispatcher);
    }

    private static void Pump(Task task, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            dispatcher.Drain();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new Exception("Navigation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new Exception("Navigation assertion failed.");
    }

    public sealed class ScreenData : IDisposable
    {
        public static ScreenData? Last;
        public State<int> Count { get; } = new(0);
        public int Disposals;

        public ScreenData() => Last = this;

        public void Dispose() => Disposals++;
    }

    public sealed class Screen(ScreenData data) : Component, IDisposable
    {
        public ScreenData Data { get; } = data;
        public int Disposals;

        public override Element Build() => new Text(Data.Count.Value.ToString());

        public void Dispose() => Disposals++;
    }

    public sealed class BrokenScreen : Component
    {
        public BrokenScreen(ScreenData data) =>
            throw new InvalidOperationException("Constructor failure");
    }

    public sealed class AsyncScreen(ScreenData data) : Component, IAsyncDisposable
    {
        public ScreenData Data { get; } = data;
        public bool Disposed;

        public override Element Build() => new Text("Async screen");

        public async ValueTask DisposeAsync()
        {
            await Task.Delay(1);
            Require(Data.Disposals == 0);
            Disposed = true;
        }
    }
}
