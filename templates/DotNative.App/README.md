# AppName

One C# project. Start in root `Program.cs`: register services and call
`builder.Build().Run<App>()`. Write shared UI and state in `App.cs`.
Optional native code lives under `Platform/iOS/`, `Platform/Android/`,
`Platform/Mac/`, and `Platform/Windows/`.

## Run on an Apple Silicon development Mac

```sh
dotnet run                                      # booted iOS Simulator
dotnet run -- <UDID>                             # specific iOS Simulator
dotnet run -r osx-arm64                          # native AppKit window
dotnet run -r linux-bionic-arm64 -p:DisableUnsupportedError=true  # Android emulator
```

The Android emulator must already be booted. Install Android SDK 36, JDK 17 and
NDK 29.0.14206865; override ANDROID_HOME and ANDROID_NDK_HOME for other locations.
The Android NativeAOT route is experimental. This preview supports ARM64 only.
Xcode is required for iOS and Mac. Windows is reserved and fails clearly at build.

The preview DotNative.App package must be in your NuGet sources; it has not been
published to nuget.org. No MAUI workload or Rust SDK is needed by generated apps.
The framework's source repository has local packaging instructions in templates/README.md.

## Ownership

The csproj references DotNative.App, which supplies managed bootstrap, the UI
thread dispatcher, native archives, and the first-party Essentials bundle.
The generated entry point under obj invokes `<RootNamespace>.Program.Main()`;
set DotNativeStartupType for another startup class. Main is public, static and parameterless, returning void or Task. The native runner invokes it on the UI thread. There is no separate C# platform
project or required solution file.

Platform/iOS/Runner/main.m belongs to the Xcode Runner target. It creates the window,
mounts DotNative, and drives the event loop. Open Platform/iOS/Runner.xcodeproj to
customize native startup or configure device signing. Mac uses an AppKit runner;
Android uses a Gradle Activity with native Android controls and JNI.

Native buttons enqueue stable node IDs. C# invokes the current callback, rebuilds
when State changes, and sends a binary diff. The counter supports Increment and
Reset. Text, focus, switch and slider events are also supported. Navigation and
development code hot reload in these native runners are not implemented yet.

## Platform-specific C#

Root C# files are shared. Optional C# files under Platform/iOS/, Platform/Android/,
Platform/Mac/, or Platform/Windows/ compile only for that target. Use DOTNATIVE_IOS,
DOTNATIVE_ANDROID, DOTNATIVE_MACOS, DOTNATIVE_WINDOWS and DOTNATIVE_SIMULATOR for
small conditional branches. These app symbols do not recompile prebuilt plugin DLLs.

DeviceInfo is bundled and currently exposes an iOS-only API. Storage, secure
storage, permissions, camera, and photos/file picking remain upcoming modules.
See Platform/iOS/README.md for optional LibraryImport/DllImport C ABI calls.
