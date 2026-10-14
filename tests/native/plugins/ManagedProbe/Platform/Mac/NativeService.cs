namespace ManagedProbe;

public sealed partial class NativeService
{
    public partial string Read() =>
        $"macOS: {Foundation.NSProcessInfo.ProcessInfo.OperatingSystemVersionString}";
}
