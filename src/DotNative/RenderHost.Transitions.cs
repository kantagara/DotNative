namespace DotNative;

public sealed partial class RenderHost
{
    private readonly TimeProvider _clock;

    private sealed record Motion(
        Presentation From,
        Presentation Target,
        Presentation Presented,
        StyleTransition Spec,
        long Start
    );

    private Dictionary<uint, Motion> _motions = [];
    private Dictionary<uint, Motion> _pendingMotions = [];
    private ITimer? _motionTimer;
    private int _motionQueued;
    private bool _reducedMotion;
    private bool _externalFrames;

    /// <summary>Call once per native display callback. Switching to external frames stops the fallback timer.</summary>
    public void AdvanceAnimationFrame()
    {
        VerifyUsable();
        _externalFrames = true;
        _motionTimer?.Dispose();
        _motionTimer = null;
        if (_motions.Count != 0)
            AdvanceTransitions();
    }

    /// <summary>Application/accessibility preference. Enabling it finishes active transitions immediately.</summary>
    public bool ReducedMotion
    {
        get => ShouldReduceMotion;
        set
        {
            VerifyUsable();
            if (_reducedMotion == value)
                return;
            _reducedMotion = value;
            if (value)
                AdvanceTransitions();
        }
    }

    private (Node Node, Node? Old) TransitionNode(uint id, Node node, Node? old)
    {
        if (old is null)
            return (node, old);
        var target = Presentation.Of(node);
        var previous = _pendingMotions.TryGetValue(id, out var motion)
            ? motion.Presented
            : Presentation.Of(old);
        if (
            ShouldReduceMotion
            || node.TransitionStyle
                is not { Duration.Ticks: > 0, Properties: not TransitionProperties.None } spec
        )
        {
            _pendingMotions.Remove(id);
            return (node, motion is null ? old : Present(old, previous));
        }
        if (motion is not null && motion.Target == target && motion.Spec == spec)
            return (Present(node, motion.Presented), Present(old, motion.Presented));
        // Non-animated fields take effect in the same transaction as the new description.
        var start = Presentation.Interpolate(previous, target, 0, spec.Properties);
        if (start == target)
            _pendingMotions.Remove(id);
        else
            _pendingMotions[id] = new(start, target, start, spec, _clock.GetTimestamp());
        return (Present(node, start), Present(old, previous));
    }

    private static Node Present(Node node, Presentation value) =>
        node with
        {
            Style = value.Style,
            Visual = value.Visual,
            Decoration = value.Decoration,
        };

    private void UpdateMotionTimer()
    {
        if (_motions.Count == 0)
        {
            _motionTimer?.Dispose();
            _motionTimer = null;
            return;
        }
        if (_externalFrames)
            return;
        _motionTimer ??= _clock.CreateTimer(
            _ =>
            {
                if (Interlocked.Exchange(ref _motionQueued, 1) != 0)
                    return;
                _dispatcher.Post(() =>
                {
                    Interlocked.Exchange(ref _motionQueued, 0);
                    if (!_disposed && !_faulted)
                        AdvanceTransitions();
                });
            },
            null,
            TimeSpan.FromMilliseconds(16),
            TimeSpan.FromMilliseconds(16)
        );
    }

    private void AdvanceTransitions()
    {
        VerifyUsable();
        _rendering = true;
        try
        {
            AdvanceTransitionsCore();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void AdvanceTransitionsCore()
    {
        _buffer.Reset();
        var next = new Dictionary<uint, Motion>(_motions);
        foreach (var (id, motion) in _motions)
        {
            var t = ShouldReduceMotion
                ? 1
                : (float)
                    Math.Clamp(
                        _clock.GetElapsedTime(motion.Start).TotalSeconds
                            / motion.Spec.Duration.TotalSeconds,
                        0,
                        1
                    );
            var eased = motion.Spec.Easing switch
            {
                TransitionEasing.EaseInOut => Easings.EaseInOut(t),
                TransitionEasing.EaseOutCubic => Easings.EaseOutCubic(t),
                TransitionEasing.EaseInCubic => t * t * t,
                _ => t,
            };
            var value = Presentation.Interpolate(
                motion.From,
                motion.Target,
                eased,
                motion.Spec.Properties
            );
            if (value.Style != motion.Presented.Style)
                _buffer.SetStyle(id, value.Style);
            if (value.Visual != motion.Presented.Visual)
                _buffer.SetVisualStyle(id, value.Visual);
            if (value.Decoration != motion.Presented.Decoration)
                _buffer.SetBoxDecoration(id, value.Decoration);
            if (t == 1)
                next.Remove(id);
            else
                next[id] = motion with { Presented = value };
        }
        try
        {
            Flush();
            _motions = next;
        }
        catch
        {
            _motionTimer?.Dispose();
            _motionTimer = null;
            throw;
        }
        UpdateMotionTimer();
    }
}
