# Platform .NET plugins and NativeAOT bindings

`DotNative.BindingsProbe` is a separate C# project compiled against the actual
.NET platform SDK. `Samples/Counter` calls it from its normal application UI
thread and renders its result through the existing binary protocol and Rust tree.
The normal sample build remains unchanged; this path is explicitly opt-in.

This proves a platform-targeted assembly can participate in DotNative. It does
not promise that an arbitrary MAUI plugin, reflection-heavy library, native view,
permission flow, or generated Java callback will work without additional work.
There is no second Mono runtime and no replacement MAUI application.

## What is exercised

- iOS: `net10.0-ios26.5`, `UIKit.UIDevice.CurrentDevice`.
- Android: `net10.0-android`, `Android.OS.Build.Model`, plus allocation and disposal
  of a `Java.Lang.String` through real Java interop.
- macOS: `net10.0-macos26.5`, `Foundation.NSProcessInfo`.
- Windows: `net10.0-windows10.0.19041.0`, WinRT
  `Windows.System.Profile.AnalyticsInfo`.
- Linux: `net10.0` and a source-generated `LibraryImport` of libc `getuid`.
  There is no special .NET Linux bindings workload in this proof.

The plugin is compiled into the NativeAOT image with the app. It is not loaded
dynamically. Native Swift/Kotlin/C++ service plugins can be composed with platform C# libraries
using `run <platform> --bindings --project <app>`. See [plugin-authoring.md](plugin-authoring.md)
for scaffolding, desktop support, import adapters and current limitations.

## Ownership and initialization

On Apple platforms, the platform SDK's native entry point initializes its binding
runtime and static registrar. `BindingsBootstrap` then calls the **existing**
application Objective-C runner, compiled with its `main` symbol renamed. That runner
still owns UIKit/AppKit and starts the existing DotNative application session.
Rust still creates the controls and computes layout. For this proof, the .NET
platform SDK performs the final link/package step, rather than Xcode's project.

On Android, the .NET SDK builds a NativeAOT shared library with the Rust archive
linked into it. Its generated content provider initializes Java interop before
the existing Java Activity calls DotNative. JNI exports needed by that Activity
are explicitly retained. The existing **Gradle project** packages and launches
the application. Only the required SDK-generated Java sources and helper JARs
are staged; this is not yet general AAR/resources/manifest dependency packaging.

`mono.android.jar`, Java `mono.*` helper classes, `Mono.Android.dll` at build time,
and Apple's `MonoBundle` directory are SDK naming conventions. They are not
evidence of a Mono VM. The Android proof packages `libCounter.so`, without
`libmonosgen`, `libmonodroid`, `libcoreclr`, or managed DLL payloads. NativeAOT
still includes runtime services such as GC and exception handling.

On Windows/Linux the **existing desktop architecture has two processes**: a
NativeAOT C# host and the Rust Win32/GTK renderer, using the binary preview
transport. Service interop happens in the C# process. This does not prove that
managed plugins can access the renderer's window or native UI thread.

## Run through the CLI

Run from the repository root. `run <platform> --bindings --project <app>` accepts
application projects; `bindings-demo` remains a backwards-compatible Counter
shortcut. This remains an opt-in integration path with the limitations documented
in [plugin-authoring.md](plugin-authoring.md).

```sh
dotnet run --project tools/DotNative.Cli -- devices
dotnet run --project tools/DotNative.Cli -- bindings-demo ios --device <simulator-UDID>
dotnet run --project tools/DotNative.Cli -- bindings-demo android --device <adb-ID>
dotnet run --project tools/DotNative.Cli -- bindings-demo mac

# Run on the target OS, with its Rust and NativeAOT build prerequisites installed:
dotnet run --project tools/DotNative.Cli -- bindings-demo windows
dotnet run --project tools/DotNative.Cli -- bindings-demo linux
```

`--build-only` builds without launching. `--dotnet /absolute/path/to/dotnet`
selects the SDK executable. The Mac proof defaults to the isolated SDK under
`artifacts/bindings-integration/dotnet` when it exists. It does not install or
update workloads automatically. Set `JAVA_HOME` to JDK 17 and `ANDROID_HOME` to
the Android SDK when necessary.

Validated Mac SDK configuration: .NET SDK 10.0.300, Android workload 36.1.69,
iOS/macOS workloads 26.5.10315, Xcode 26.6, Android NDK 29.0.14206865. Android
staging currently pins those workload asset paths deliberately. A newer SDK
requires checking its bootstrap/JAR contract before changing those paths.

Apple and Android mobile builds currently support ARM64 only. Apple local app
bundles and helper dylibs are ad-hoc signed. The CLI's `ios` target is a simulator,
not physical-device signing/provisioning. An unsigned `ios-arm64` publish was
also built, but that does not constitute a device execution test.

The simulator path uses the SDK's internal `_IsPublishing=true` build override
to force NativeAOT. Do not treat this as a supported IDE/hot reload configuration.
The diagnostic CLI launches compiled code; it does not hot reload plugin code.

## Limits that matter

Local verification (latest update 2026-10-08):

- iOS 26.5 simulator: plugin result displayed; Counter increment changed 0 to 1.
- Android ARM64 emulator: plugin result displayed; Counter increment changed 0 to 1.
  Pixel 7: the CLI installed and launched the app, and the SDK-binding call logged
  `Android: Pixel 7 · NativeAOT bindings` from the running process.
- macOS 26.6.2: Foundation result displayed; Counter increment changed 0 to 1.
- Linux ARM64: NativeAOT host called libc; existing GTK renderer connected and
  committed the Counter frame under Xvfb. This is headless verification.
- Windows ARM64: `bindings-demo windows` compiled the platform plugin and host
  with NativeAOT, built the Rust renderer and WinUI NativeAOT renderer, then ran
  the bindings call (`DOTNATIVE_BINDINGS_PASS: Windows: Windows.Desktop`) and
  committed a native frame.

Both sample/template parity checks, the CLI routing tests and all 23 managed
framework tests passed. Android incremental packaging also needs to preserve the
SDK-generated `NativeAotRuntimeProvider.java`: SDK 36.1.69 omits it from its file
tracking list, so the opt-in target explicitly records/regenerates it.

The Android workload emits [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040):
NativeAOT is experimental and not yet suitable for production use. It also emits
AOT analysis warnings from `Mono.Android`, including unsupported exception/debugger
paths. These warnings are visible, not suppressed.

Before advertising general plugin compatibility, test Java/Objective-C callbacks,
managed subclasses, cancellation, permissions, Activity recreation, native resource
and manifest merging, and third-party binding libraries under trimming. Libraries
depending on MAUI lifecycle services need those dependencies satisfied explicitly.
The successful device-info calls here do not establish those guarantees.

For NativeAOT constraints see the [official overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
Build products, the isolated SDK and local verification scripts remain ignored
under `artifacts/`; only the integration source and diagnostic CLI belong in Git.
