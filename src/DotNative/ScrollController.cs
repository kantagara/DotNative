using System.Globalization;

namespace DotNative;

public readonly record struct ScrollMetrics(float Offset, float ViewportExtent);

/// <summary>One controller per mounted scroll view; offsets use logical pixels.</summary>
public sealed class ScrollController : ReactiveSource
{
    private float _offset,
        _viewport,
        _requestedOffset;
    private uint _revision;
    public float Offset
    {
        get
        {
            TrackRead();
            return _offset;
        }
    }
    public float ViewportExtent
    {
        get
        {
            TrackRead();
            return _viewport;
        }
    }

    public void JumpTo(float offset)
    {
        VerifyCanWrite();
        if (!float.IsFinite(offset) || offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _revision = checked(_revision + 1);
        _offset = _requestedOffset = offset;
        PublishChange();
    }

    internal (uint Revision, float Offset) Request
    {
        get
        {
            TrackRead();
            return (_revision, _requestedOffset);
        }
    }

    internal bool Report(uint revision, ScrollMetrics metrics)
    {
        VerifyCanWrite();
        if (revision != _revision)
            return false;
        if (_offset == metrics.Offset && _viewport == metrics.ViewportExtent)
            return true;
        _offset = _requestedOffset = metrics.Offset;
        _viewport = metrics.ViewportExtent;
        PublishChange();
        return true;
    }

    internal static bool Decode(
        float offset,
        string text,
        out uint revision,
        out ScrollMetrics metrics
    )
    {
        var split = text.IndexOf(':');
        revision = 0;
        metrics = default;
        if (
            !float.IsFinite(offset)
            || offset < 0
            || split < 1
            || !uint.TryParse(
                text.AsSpan(0, split),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out revision
            )
            || !float.TryParse(
                text.AsSpan(split + 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var viewport
            )
            || !float.IsFinite(viewport)
            || viewport < 0
        )
            return false;
        metrics = new(offset, viewport);
        return true;
    }
}
