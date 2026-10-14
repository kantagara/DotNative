namespace ManagedProbe;

public sealed partial class NativeService
{
    public partial string Read() =>
        $"Windows: {Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily}";
}
