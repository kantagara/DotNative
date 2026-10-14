using System.Runtime.InteropServices;
using DotNative;

namespace DotNative.Generated;

internal static unsafe partial class ManagedBootstrap
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public nint Start,
            Resize,
            Tick,
            Stop,
            Activity,
            Theme,
            Accessibility,
            Insets;
    }

    internal static void Initialize()
    {
#if !DOTNATIVE_ANDROID
        // The Apple SDK links Rust into its runtime-owned native executable.
        NativeLibrary.SetDllImportResolver(typeof(NativeTransport).Assembly, ResolveCore);
#endif
        var callbacks = new Callbacks
        {
            Start = (nint)(delegate* unmanaged[Cdecl]<float, float, int>)&AppEntry.Start,
            Resize = (nint)(delegate* unmanaged[Cdecl]<float, float, int>)&AppEntry.Resize,
            Tick = (nint)(delegate* unmanaged[Cdecl]<int>)&AppEntry.Tick,
            Stop = (nint)(delegate* unmanaged[Cdecl]<int>)&AppEntry.Stop,
            Activity = (nint)(delegate* unmanaged[Cdecl]<int, int>)&AppEntry.Activity,
            Theme = (nint)(delegate* unmanaged[Cdecl]<int, int>)&AppEntry.SystemTheme,
            Accessibility = (nint)
                (delegate* unmanaged[Cdecl]<int, float, int, int>)&AppEntry.Accessibility,
            Insets = (nint)(delegate* unmanaged[Cdecl]<float, float, float, int>)&AppEntry.Insets,
        };
        if (Register(2, (nuint)sizeof(Callbacks), &callbacks) != 0)
            throw new InvalidOperationException(
                "Could not register the DotNative managed runtime bridge."
            );
    }

#if DOTNATIVE_ANDROID
    [LibraryImport("core", EntryPoint = "dotnative_register_managed_host")]
#else
    [LibraryImport("__Internal", EntryPoint = "dotnative_register_managed_host")]
#endif
    private static partial int Register(uint version, nuint size, Callbacks* callbacks);

#if !DOTNATIVE_ANDROID
    private static nint ResolveCore(
        string name,
        System.Reflection.Assembly assembly,
        DllImportSearchPath? path
    ) => name == "core" ? NativeLibrary.GetMainProgramHandle() : 0;

    private static int Main()
    {
        Initialize();
        return RunNativeRunner(0, 0);
    }

    [LibraryImport("__Internal", EntryPoint = "dotnative_runner_main")]
    private static partial int RunNativeRunner(int argc, nint argv);
#endif
}

#if DOTNATIVE_ANDROID
[Android.App.Application]
public sealed class ManagedApplication : Android.App.Application
{
    public ManagedApplication(nint handle, Android.Runtime.JniHandleOwnership ownership)
        : base(handle, ownership) { }

    public override void OnCreate()
    {
        base.OnCreate();
        ManagedBootstrap.Initialize();
    }
}
#endif
