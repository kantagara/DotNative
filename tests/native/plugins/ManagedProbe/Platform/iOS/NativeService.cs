namespace ManagedProbe;

public sealed partial class NativeService
{
    public partial string Read() => $"iOS: {UIKit.UIDevice.CurrentDevice.SystemVersion}";
}
