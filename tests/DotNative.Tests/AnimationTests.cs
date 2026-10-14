using DotNative;

internal static class AnimationTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var clock = new ManualClock();
        using var animation = new AnimationController(dispatcher, clock: clock);
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() => new Text("animated").Translate(animation.Value, 0)),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Find("animated");
        var first = animation.AnimateToAsync(100, TimeSpan.FromSeconds(1));
        clock.Advance(0.5);
        dispatcher.Drain();
        Check(animation.Value == 50 && animation.IsAnimating);
        Check(native.Find("animated") == id);
        Check(
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                native.Nodes[id].VisualStyle.AsSpan(28)
            ) == 50
        );
        var second = animation.AnimateToAsync(200, TimeSpan.FromSeconds(1), Easings.EaseInOut);
        Check(first.IsCanceled);
        clock.Advance(0.5);
        dispatcher.Drain();
        Check(animation.Value == 125);
        clock.Advance(0.5);
        dispatcher.Drain();
        Check(second.IsCompletedSuccessfully && animation.Value == 200 && !animation.IsAnimating);
        using var cancel = new CancellationTokenSource();
        var canceled = animation.AnimateToAsync(
            0,
            TimeSpan.FromSeconds(1),
            cancellationToken: cancel.Token
        );
        clock.Advance(0.5);
        cancel.Cancel();
        dispatcher.Drain();
        Check(canceled.IsCanceled && !animation.IsAnimating);
        var previous = animation.Value;
        clock.Advance(2);
        dispatcher.Drain();
        Check(animation.Value == previous);
        var disposed = animation.AnimateToAsync(0, TimeSpan.FromSeconds(1));
        clock.Advance(0.5);
        animation.Dispose();
        dispatcher.Drain();
        Check(disposed.IsCanceled && animation.Value == previous && clock.Active == 0);
        using var invalid = new AnimationController(dispatcher, clock: clock);
        var failed = invalid.AnimateToAsync(1, TimeSpan.FromSeconds(1), _ => float.NaN);
        clock.Advance(0.5);
        dispatcher.Drain();
        Check(failed.IsFaulted && !invalid.IsAnimating && invalid.Value == 0);
        _ = failed.Exception;
        Check(
            invalid.AnimateToAsync(1, TimeSpan.Zero).IsCompletedSuccessfully && invalid.Value == 1
        );
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("Animation lifecycle contract failed.");
    }

    internal sealed class ManualClock : TimeProvider
    {
        private long _time;
        private readonly List<Timer> _timers = [];
        public int Active => _timers.Count(t => !t.Disposed);
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _time;

        public void Advance(double seconds)
        {
            _time += (long)(seconds * 1000);
            foreach (var timer in _timers.ToArray())
                if (!timer.Disposed)
                    timer.Callback(timer.State);
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new Timer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal bool Disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
