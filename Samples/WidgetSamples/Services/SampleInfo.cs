namespace WidgetSamples.Services;

public interface ISampleInfo
{
    string Description { get; }
    Task InitializeAsync(CancellationToken cancellation);
}

public sealed class SampleInfo : ISampleInfo
{
    public string Description { get; private set; } = "Initializing…";

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        // Exercise the UI synchronization context without requiring storage,
        // network access or platform permissions just to open the sample.
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        Description = "async Main + .NET DI ready";
    }
}
