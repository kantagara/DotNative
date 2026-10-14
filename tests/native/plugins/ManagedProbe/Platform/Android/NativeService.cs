namespace ManagedProbe;

public sealed partial class NativeService
{
    public partial string Read() => $"Android: {Android.OS.Build.Model}";
}
