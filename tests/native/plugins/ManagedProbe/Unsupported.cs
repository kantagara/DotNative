namespace ManagedProbe;

public sealed partial class NativeService
{
    public partial string Read() =>
        throw new PlatformNotSupportedException(
            "Build this plugin for a native platform; a remote preview cannot call a device's SDK bindings."
        );
}
