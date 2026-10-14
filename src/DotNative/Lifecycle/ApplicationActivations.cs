namespace DotNative;

public enum ApplicationActivationKind
{
    Uri,
    Notification,
}

public sealed record ApplicationActivation(ApplicationActivationKind Kind, string Payload);

/// <summary>OS launch/open events. Payloads are untrusted; apps validate routes and access.
/// Subscribe and drain on the application UI thread.
/// Events received before subscription are retained until ReadPending is called.</summary>
public interface IApplicationActivations
{
    event Action<ApplicationActivation>? Opened;
    IReadOnlyList<ApplicationActivation> ReadPending();
}

internal sealed class ApplicationActivations : IApplicationActivations
{
    private readonly Queue<ApplicationActivation> pending = new();
    public event Action<ApplicationActivation>? Opened;

    public IReadOnlyList<ApplicationActivation> ReadPending()
    {
        var values = pending.ToArray();
        pending.Clear();
        return values;
    }

    internal void Publish(ApplicationActivation activation)
    {
        if (string.IsNullOrWhiteSpace(activation.Payload) || activation.Payload.Length > 16384)
            return;
        if (Opened is { } handler)
            handler(activation);
        else
        {
            if (pending.Count == 32)
                pending.Dequeue();
            pending.Enqueue(activation);
        }
    }
}
