using System.Runtime.InteropServices;

namespace ManagedProbe;

public sealed partial class NativeService
{
    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();

    public partial string Read() => $"Linux: uid={GetUid()}";
}
