using System.Runtime.InteropServices;

namespace DotNative.Generated;

internal static partial class BindingsBootstrap
{
    // The SDK initializes its NativeAOT bindings before this entry point. The
    // existing Objective-C runner still owns UIApplication/NSApplication and
    // starts DotNative's normal ApplicationSession through the existing C ABI.
    private static int Main()
    {
        // Keep the platform binding assembly reachable even for apps whose
        // current plugins only use Swift. The SDK's registrar requires it.
        GC.KeepAlive(typeof(Foundation.NSObject));
        return RunNativeRunner(0, IntPtr.Zero);
    }

    [LibraryImport("__Internal", EntryPoint = "dotnative_runner_main")]
    private static partial int RunNativeRunner(int argc, IntPtr argv);
}
