namespace DotNative.BindingsProbe;

// A separate platform-targeted plugin assembly. The existing Counter app calls
// this on its UI thread; native platform objects exercise actual SDK bindings.
public static partial class PlatformProbe
{
    public static string Read()
    {
#if ANDROID
        using var value = new Java.Lang.String("NativeAOT bindings");
        var result = $"Android: {Android.OS.Build.Model} · {value}";
        Android.Util.Log.Info("DotNative.BindingsProbe", result);
        return result;
#elif IOS
        var device = UIKit.UIDevice.CurrentDevice;
        return $"iOS: {device.Model} · {device.SystemVersion}";
#elif MACOS
        return $"macOS: {Foundation.NSProcessInfo.ProcessInfo.OperatingSystemVersionString}";
#elif WINDOWS
        return $"Windows: {Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily}";
#elif DOTNATIVE_LINUX
        return $"Linux: libc getuid() = {GetUserId()}";
#else
        throw new PlatformNotSupportedException("A platform bindings target is required.");
#endif
    }

#if DOTNATIVE_LINUX
    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUserId();
#endif
}
