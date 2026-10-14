namespace DotNative;

/// <summary>
/// A service started once by Run, after the first UI frame is mounted.
/// Implementations must observe cancellation; DisposeAsync waits for them before releasing services.
/// </summary>
public interface IApplicationInitializer
{
    Task InitializeAsync(CancellationToken stopping);
}
