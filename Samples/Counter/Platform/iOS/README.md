# iOS runner and optional native code

`Runner/main.m` belongs to the Xcode Runner target. It creates the window, mounts
DotNative inside the safe area, starts C#, and drives its event queue once per
display tick. Open `Runner.xcodeproj` to customize native startup.

Most apps only edit `App.cs`. The Xcode host is here if you need platform APIs,
App Extensions or native code. Managed bootstrap/dispatcher code is supplied by
the DotNative.App package and is not part of this folder.

Add an Objective-C `.m` (or Objective-C++ `.mm`) file to the Runner target's Compile
Sources in Xcode. Export plain C functions; Objective-C methods are not C symbols.
For example:

```objective-c
#import <Foundation/Foundation.h>
#include <stdint.h>

int32_t myapp_ios_major_version(void) {
    return (int32_t)NSProcessInfo.processInfo.operatingSystemVersion.majorVersion;
}
```

For `.mm`, wrap exported functions in `extern "C"` to disable C++ name mangling.
Call from a file under ``:

```csharp
using System.Runtime.InteropServices;

internal static partial class NativeDevice
{
    [LibraryImport("__Internal", EntryPoint = "myapp_ios_major_version")]
    internal static partial int MajorVersion();
}
```

`DllImport("__Internal", EntryPoint = "myapp_ios_major_version")` with a
`static extern int` method works for this blittable signature too. The app package
configures DirectPInvoke for `__Internal`, so the symbol is resolved by Xcode's
final static link. Prefer LibraryImport for source-generated NativeAOT marshalling.
No per-function framework registration is needed; the native source must be linked
into Runner. UIKit operations must execute on the OS main thread.

Keep the ABI explicit: fixed-size numeric values, pointers plus lengths, clear
ownership, and status returns. Neither managed exceptions nor Objective-C/C++
exceptions may cross it. Swift requires a C-compatible export or Objective-C/C shim.
Android's Java/Kotlin APIs will require JNI integration; DllImport alone will not
call them. Other platform integrations are not implemented yet.
