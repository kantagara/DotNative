using DotNative;
using DotNative.Hosting;
using Microsoft.Extensions.DependencyInjection;

internal static class SharedStateTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var sink = new Sink();
        CartStore? cart = null;
        Root? root = null;
        using var session = ApplicationSession.Start(
            () =>
            {
                var builder = DotNativeApplication.CreateBuilder();
                builder.Services.AddSingleton<CartStore>();
                builder.Services.AddSingleton<Root>();
                var app = builder.Build();
                Require(
                    ReferenceEquals(app.Services.GetRequiredService<IUiDispatcher>(), dispatcher)
                );
                cart = app.Services.GetRequiredService<CartStore>();
                root = app.Services.GetRequiredService<Root>();
                app.Run<Root>();
            },
            dispatcher,
            sink,
            320,
            600
        );
        dispatcher.Drain();
        Require(root!.Builds == 1 && root.LastCount == 0);
        cart!.Count.Value = 1;
        cart.Count.Value = 2;
        dispatcher.Drain();
        Require(root.Builds == 2 && root.LastCount == 2);

        // A second mounted reader shares the instance, not a copied state value.
        int observed = -1;
        using var other = new RenderHost(
            new Factory(() =>
            {
                observed = cart.Count.Value;
                return new Text(observed.ToString());
            }),
            dispatcher,
            new Sink()
        );
        other.RenderNow();
        cart.Count.Value = 3;
        dispatcher.Drain();
        Require(observed == 3 && root.LastCount == 3);
        root.Show.Value = false;
        dispatcher.Drain();
        int hiddenBuilds = root.Builds;
        cart.Count.Value = 4;
        dispatcher.Drain();
        Require(root.Builds == hiddenBuilds && observed == 4);
        other.Dispose();
        cart.Count.Value = 5;
        dispatcher.Drain();
        Require(observed == 4 && root.Builds == hiddenBuilds);

        // Worker access is rejected; a posted update is applied on the UI thread.
        Task.Run(() =>
            {
                try
                {
                    cart.Count.Value = 6;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                throw new Exception("Cross-thread state mutation was accepted.");
            })
            .GetAwaiter()
            .GetResult();
        root.Show.Value = true;
        dispatcher.Drain();
        cart.UpdateFromWorker(7).GetAwaiter().GetResult();
        dispatcher.Drain();
        Require(root.LastCount == 7);
        session.Dispose();
        int stoppedBuilds = root.Builds;
        cart.Count.Value = 8;
        dispatcher.Drain();
        Require(root.Builds == stoppedBuilds && cart.Disposed);
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new Exception("Shared state assertion failed.");
    }

    private sealed class Sink : ICommandTransport
    {
        public void Flush(ReadOnlySpan<byte> buffer) { }
    }

    public sealed class CartStore(IUiDispatcher dispatcher) : IDisposable
    {
        public State<int> Count { get; } = new(0);
        public bool Disposed { get; private set; }

        public Task UpdateFromWorker(int value) =>
            Task.Run(() => dispatcher.Post(() => Count.Value = value));

        public void Dispose() => Disposed = true;
    }

    public sealed class Root(CartStore cart) : Component
    {
        public State<bool> Show { get; } = new(true);
        public int Builds { get; private set; }
        public int LastCount { get; private set; }

        public override Element Build()
        {
            Builds++;
            if (!Show.Value)
                return new Text("Hidden");
            LastCount = cart.Count.Value;
            return new Text(LastCount.ToString());
        }
    }
}
