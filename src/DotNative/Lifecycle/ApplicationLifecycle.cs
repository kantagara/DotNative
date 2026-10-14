namespace DotNative;

public enum ApplicationActivity
{
    Active = 0,
    Inactive = 1,
    Background = 2,
    Stopping = 3,
}

public interface IApplicationLifecycle
{
    ApplicationActivity Activity { get; }
    event Action<ApplicationActivity>? Changed;
}

// Transitions arrive from the platform runner, never from a frame counter.
// Reading Activity during Build participates in normal reactive tracking.
internal sealed class ApplicationLifecycle : ReactiveSource, IApplicationLifecycle
{
    private ApplicationActivity _activity = ApplicationActivity.Inactive;
    public ApplicationActivity Activity
    {
        get
        {
            TrackRead();
            return _activity;
        }
    }
    public event Action<ApplicationActivity>? Changed;

    internal void Transition(ApplicationActivity activity)
    {
        VerifyCanWrite();
        if (!Enum.IsDefined(activity))
            throw new ArgumentOutOfRangeException(nameof(activity));
        if (_activity == ApplicationActivity.Stopping || _activity == activity)
            return;
        _activity = activity;
        PublishChange();
        Changed?.Invoke(activity);
    }
}
