namespace DotNative;

/// <summary>UI-thread, time-based animation source. Dispose with the owning component.</summary>
public sealed class AnimationController : ReactiveSource, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private float _value;
    private Run? _run;
    private bool _disposed;

    public AnimationController(
        IUiDispatcher dispatcher,
        float initialValue = 0,
        TimeProvider? clock = null
    )
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("UI thread required.");
        Finite(initialValue);
        _dispatcher = dispatcher;
        _clock = clock ?? TimeProvider.System;
        _value = initialValue;
    }

    public float Value
    {
        get
        {
            TrackRead();
            return _value;
        }
    }
    public bool IsAnimating
    {
        get
        {
            TrackRead();
            return _run is not null;
        }
    }

    /// <summary>Retargets from the last displayed value. Superseded animations are canceled.</summary>
    public Task AnimateToAsync(
        float target,
        TimeSpan duration,
        Func<float, float>? easing = null,
        CancellationToken cancellationToken = default
    )
    {
        Check();
        Finite(target);
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        cancellationToken.ThrowIfCancellationRequested();
        StopCore();
        if (duration == TimeSpan.Zero || target == _value)
        {
            _value = target;
            PublishChange();
            return Task.CompletedTask;
        }
        var run = new Run(
            _value,
            target,
            duration,
            easing ?? Easings.Linear,
            _clock.GetTimestamp()
        );
        _run = run;
        try
        {
            run.Timer = _clock.CreateTimer(
                _ => QueueTick(run),
                null,
                TimeSpan.FromMilliseconds(16),
                TimeSpan.FromMilliseconds(16)
            );
            run.Cancellation = cancellationToken.Register(() =>
                _dispatcher.Post(() =>
                {
                    if (ReferenceEquals(_run, run))
                    {
                        StopCore();
                        PublishChange();
                    }
                })
            );
            PublishChange();
        }
        catch (Exception error)
        {
            Finish(run, error);
            PublishChange();
        }
        return run.Completion.Task;
    }

    public void SnapTo(float value)
    {
        Check();
        Finite(value);
        StopCore();
        _value = value;
        PublishChange();
    }

    public void Stop()
    {
        Check();
        StopCore();
        PublishChange();
    }

    public void Dispose()
    {
        VerifyCanWrite();
        if (_disposed)
            return;
        _disposed = true;
        StopCore();
        PublishChange();
    }

    private void QueueTick(Run run)
    {
        // A stalled UI receives one pending frame, not an unbounded timer backlog.
        if (Interlocked.Exchange(ref run.Queued, 1) == 0)
            _dispatcher.Post(() =>
            {
                Interlocked.Exchange(ref run.Queued, 0);
                if (!ReferenceEquals(_run, run))
                    return;
                try
                {
                    VerifyCanWrite();
                    double progress = Math.Clamp(
                        _clock.GetElapsedTime(run.Start).TotalSeconds / run.Duration.TotalSeconds,
                        0,
                        1
                    );
                    float eased = progress == 1 ? 1 : run.Easing((float)progress);
                    Finite(eased);
                    if (!ReferenceEquals(_run, run))
                        return;
                    var next = (float)((double)run.From + ((double)run.Target - run.From) * eased);
                    Finite(next);
                    _value = next;
                    if (progress == 1)
                        Finish(run);
                    PublishChange();
                }
                catch (Exception error)
                {
                    Finish(run, error);
                    PublishChange();
                }
            });
    }

    private void StopCore()
    {
        if (_run is not { } run)
            return;
        _run = null;
        run.Timer?.Dispose();
        run.Cancellation.Dispose();
        run.Completion.TrySetCanceled();
    }

    private void Finish(Run run, Exception? error = null)
    {
        if (!ReferenceEquals(_run, run))
            return;
        _run = null;
        run.Timer?.Dispose();
        run.Cancellation.Dispose();
        if (error is null)
            run.Completion.TrySetResult();
        else
            run.Completion.TrySetException(error);
    }

    private void Check()
    {
        VerifyCanWrite();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void Finite(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private sealed class Run(
        float from,
        float target,
        TimeSpan duration,
        Func<float, float> easing,
        long start
    )
    {
        internal readonly float From = from,
            Target = target;
        internal readonly TimeSpan Duration = duration;
        internal readonly Func<float, float> Easing = easing;
        internal readonly long Start = start;
        internal readonly TaskCompletionSource Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal ITimer? Timer;
        internal CancellationTokenRegistration Cancellation;
        internal int Queued;
    }
}

public static class Easings
{
    public static float Linear(float progress) => progress;

    public static float EaseInOut(float progress) => progress * progress * (3 - 2 * progress);

    public static float EaseOut(float progress) => CubicBezier(0, 0, 0.58f, 1)(progress);

    public static Func<float, float> CubicBezier(float x1, float y1, float x2, float y2)
    {
        if (
            !float.IsFinite(x1)
            || !float.IsFinite(y1)
            || !float.IsFinite(x2)
            || !float.IsFinite(y2)
            || x1 < 0
            || x1 > 1
            || x2 < 0
            || x2 > 1
        )
            throw new ArgumentOutOfRangeException(nameof(x1));
        return progress =>
        {
            progress = Math.Clamp(progress, 0, 1);
            if (progress is 0 or 1)
                return progress;
            static float Axis(float t, float a, float b) =>
                3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
            float lower = 0,
                upper = 1;
            for (var i = 0; i < 20; i++)
            {
                var t = (lower + upper) / 2;
                if (Axis(t, x1, x2) < progress)
                    lower = t;
                else
                    upper = t;
            }
            return Axis((lower + upper) / 2, y1, y2);
        };
    }

    public static float EaseOutCubic(float progress) => 1 - MathF.Pow(1 - progress, 3);
}
