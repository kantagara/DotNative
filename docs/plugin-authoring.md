# Native and .NET service plugins

The DotNative CLI creates service plugins for Android, iOS, macOS, Windows and
Linux. Native implementations run in the native renderer. C# implementations are
platform-targeted .NET libraries, statically compiled with the bindings host.
Neither path needs MAUI or reflection-based plugin discovery.

## Required C# service shape

A service plugin has one implementation class. Do not split a `RoutingX` type
that only checks the platform from a `ChannelX` type that only calls
`InvokeAsync`. That is the same layer written twice. Put the platform check and
the channel call in `{Name}Service`.

Keep the C# file in this order. Mark each section with the divider used by the
first-party plugins, and omit a section that does not apply:

```csharp
// ─────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────
```

1. **Configuration.** Options records. Credentials that the native SDK needs for
   the whole app are registered once. Startup flags are a separate record and
   default to off.
2. **Public API.** One interface per product. Initialization stays on
   `I{Name}App` as `InitializeAsync`. Analytics, crash reporting, messaging and
   remote configuration are separate interfaces. Do not hang those methods on
   the app interface.
3. **Dependency Injection.** One `Add{Name}` registration.
   `TryAddSingleton` the concrete service, then map every public interface to
   that same instance. Do not register a second object per product.
4. **Implementation.** `{Name}Service` takes the platform channels, the
   presentation target, and an optional configuration factory. Store the
   channel name in a const and call native code through one private
   `InvokeAsync`. Reject invalid arguments on every platform. Where the native
   SDK does not run, return the documented empty result and do not call the
   channel.
5. **Application Lifecycle.** One `IApplicationInitializer` when the plugin
   starts with the app. It calls the product interfaces it needs. After
   `Add{Name}`, application code does not call `InitializeAsync` itself.

A second class is required only for a different implementation, such as a
desktop overlay or opening a browser. It is not a place to forward a channel
call.

`net10.0` exposes each product as an `IServiceProvider` property. `net9.0`
exposes the same name as a method. Resolve with `GetRequiredService<T>()` at
the call. See the plugin repository `CONTRIBUTING.md` for the accessor split.

## Create a native plugin

Install the [DotNative CLI](../tools/DotNative.Cli/README.md), then run from your workspace:

```sh
dotnative plugin new --name Acme.Device --output ../Acme.Device
```

The generated project contains a PascalCase C# service and DI extension, Kotlin
under `Platform/Android`, Swift under `Platform/iOS` and `Platform/Mac`, and C++17
under `Platform/Windows` and `Platform/Linux`. Reference its csproj from your app
and register it with `builder.Services.AddAcmeDevice()`.

`Build()` returns `DotNativeApplication`, which owns startup and shutdown.
Register services through `builder.Services.Add...(...)`; configuration is passed
as parameters to the registration method. Components created through DI can
receive individual service interfaces through constructor injection.

Use a readable channel name in C#, Swift, Kotlin and C++:

```csharp
var channel = channels.Get("dotnative.acme.device");
```

```swift
let channel = NativeChannels.channel("dotnative.acme.device")
```

```kotlin
val channel = NativeChannels.channel("dotnative.acme.device")
```

```cpp
auto& channel = registrar.Channel("dotnative.acme.device");
```

Version defaults to 1. Use the same name and version in the C# facade and each
native implementation. The SDK derives a numeric ID internally from UTF-8
`name/vN`; plugin authors do not assign transport IDs or generate channel files.
The codec supports null, bool, Int64, double, UTF-8 strings, bytes, lists and
string-keyed maps. Double is tag 8, so ship matching managed/native runtime versions.
Limits remain 1 MiB, 16 levels and 4096 collection entries.

Windows/Linux use `native/plugins/cpp/DotNativePlugins.hpp`:

```cpp
void AcmeDevicePlugin(const dotnative::PluginRegistrar& registrar) {
    void* window = registrar.NativeWindow(); // borrowed HWND / GtkWindow*
    registrar.Channel("dotnative.acme.device").Handle("read",
        [window](const dotnative::Value& arguments, std::shared_ptr<dotnative::Reply> reply) {
            reply->Success("native result");
        });
}
```

Handlers run on the renderer UI thread. Keep the shared reply for asynchronous
work; C++ replies can complete on a worker thread and only the first terminal
reply wins. `OnCancel`/`OnReset` release plugin-owned operations. Set cancellation
handlers on the UI thread. The window is borrowed and must not be destroyed or
used after the host closes. This is service access to the existing window, not a
custom-widget registration API.

Compile native desktop inputs on their target OS:

```sh
dotnative plugin build --project ../Acme.Device --platform mac
# --platform windows or --platform linux on those operating systems
```

Windows needs a Visual Studio Developer PowerShell with `cl` and `lib`, plus the
MSVC Rust target. Linux needs a C++17 compiler, ar, pkg-config and GTK 4.8+ development
headers. macOS needs Xcode. Run native-only apps with the usual
`run <platform> --hot-reload --project <app>` command. The CLI builds and statically
links each app's native registrations. Windows/Linux release-distribution
packaging is still experimental; this command uses the desktop preview host.

## Package and select native implementations automatically

NuGet plugins that need native source should ship a manifest at
`dotnative/plugin.json` and include their platform sources in the package:

```json
{
  "id": "Acme.Device",
  "kind": "native",
  "entry": "AcmeDevicePlugin"
}
```

`kind` is `native` or `managed`; native entries must name a valid registration
function. The CLI reads package manifests from restored `project.assets.json`,
then selects source for the requested OS. Source `ProjectReference`s continue
to work for local development. Consumers add the package reference without
editing app code or copying native implementation files. Package IDs must
match the manifest IDs.

This contract selects implementation source and registration. It does not yet
automatically merge arbitrary Android Gradle dependencies, entitlements,
usage-description strings or custom compiler options. Declared iOS binary SDKs,
resource bundles and SDK privacy manifests use the Apple dependency contract below. A plugin requiring such metadata must state that limitation clearly
and fail its build rather than silently omitting a dependency. Windows/Linux
native release packaging remains experimental even though the desktop preview
CLI can compile their plugin sources on the corresponding OS.

## NativeAOT compatibility

Each managed plugin library should set `<IsAotCompatible>true</IsAotCompatible>`
in its own project file. This enables trimming and AOT analyzers; it is a
compatibility declaration, not an instruction to AOT-compile the library by
itself. The app controls NativeAOT publish with `<PublishAot>true</PublishAot>`
and the target RID. The publish step analyzes the app and its package
dependencies together. Keep reflection and runtime code generation out of
plugin paths, and fix or explicitly justify analyzer warnings before claiming
support.

The current first-party C# service packages set `IsAotCompatible`, but they
target `net9.0`; that property alone does not prove every RID publishes or runs.
Native source plugins have a separate build/link contract through their plugin
manifest and transitive native inputs. The manifest's `kind` field is not an
AOT flag. Validate NativeAOT publish and runtime per OS/RID before marking a
platform confirmed.

## Create a C# platform plugin

```sh
dotnative plugin new --kind managed --name Acme.Platform --output ../Acme.Platform
```

This creates `NativeService` as a partial class with real platform implementations:

| Platform | Target | API used by the generated example |
| --- | --- | --- |
| Android | net10.0-android | Android.OS.Build |
| iOS | net10.0-ios | UIKit.UIDevice |
| macOS | net10.0-macos | Foundation.NSProcessInfo |
| Windows | net10.0-windows10.0.19041.0 | Windows.System.Profile |
| Linux | net10.0 | LibraryImport into libc |

`PlatformSources.targets` selects one platform's C# files and excludes the fallback.
Use partial classes/interfaces and normal DI. `UseMaui` is not set. Linux selection
uses `DotNativeBindingsPlatform=linux`; a plain net10.0 build intentionally uses
the unsupported-platform fallback. Android C# SDK bindings with NativeAOT are
**experimental** (`DNPLUGIN001`, plus the Android SDK's XA1040 warning).

Reference the plugin from an app, call `builder.UseAcmePlatform()`, then:

```sh
dotnative run mac --bindings --project <app>
dotnative run linux --bindings --project <app>
dotnative run windows --bindings --project <app>
dotnative run android --bindings --project <app> --device emulator-5554
dotnative run ios --bindings --project <app> --device <simulator-UDID>
```

Use `--dotnet <SDK-executable>` to select the SDK with installed platform workloads,
and `--build-only` to build without launching. Apple/mobile bindings currently use
an Apple Silicon host; iOS bindings execution here targets the simulator. Swift /
Kotlin / C++ and managed service plugins can be referenced by the same app.

The bindings adapter accepts source apps with literal project references and a
`DotNativeStartupType` (defaults to `<RootNamespace>.Program`). It creates an
isolated build project; arbitrary app MSBuild customizations, app resources,
manifest edits and custom build targets are not automatically forwarded. Library
project references retain their own project build configuration. See
[platform-bindings.md](platform-bindings.md) for runtime ownership. In particular,
Windows/Linux C# services run in the managed host process, not in the renderer's
UI process. MAUI `ViewHandler` is not an implemented DotNative API.

## Native event streams

Native producers use `NativeEventChannel("dotnative.sensors.accelerometer", ...)`
with the channel name directly. The channel
registers `listen`, `next` and `cancel` handlers on `NativeChannels`. C# consumes
events with `new EventChannel(channels, "dotnative.sensors.accelerometer").ReadAllAsync(...)`.

Each subscription allows one consumer and one pending read. The FIFO holds at
most 64 events. Overflow or a terminal error stops the producer and returns a
structured `PluginException`. Cancellation, iterator disposal and renderer reset
stop native resources; sinks from an earlier subscription are ignored.

Apple producers emit typed `PluginValue` values on the main actor. Android sinks
marshal worker emissions onto the UI thread. Both use DotNative's `PluginCodec`
and request/reply transport. Desktop authors use `DotNativeEvents.hpp`.

## Repeatable tests

```sh
# Tests real C++ + C# NativeAOT calls, including a native window and libc/WinRT.
# Linux: run under a desktop session or xvfb-run -a.
python3 tests/native/run-desktop-plugin-test.py

# Stage a mobile/macOS mixed plugin app; then run with --bindings as above.
python3 tests/native/stage-plugin-app.py /tmp/plugin-app --managed
```

Native channel checks cover lifecycle, ordered events, cancellation and overflow.
C++ unit tests additionally check bounds,
invalid UTF-8, duplicate map keys, cancellation, reset and duplicate completion.
The desktop CI workflow runs these on Linux and Windows; local Linux Xvfb results
do not establish compositor behavior or high-refresh-rate performance.

## Android plugin dependencies

A native plugin can ship `Platform/Android/dependencies.gradle` containing Gradle
`dependencies { implementation ... }` declarations. The CLI stages this script
during Android preview builds. For app publish, expose the
file as a `DotNativeAndroidGradle` item in the package's buildTransitive targets;
DotNative generates `plugin-dependencies.gradle`, which the Android runner applies.
The generated file is replaced on publish, including when all dependencies are
removed. Declare sources as `DotNativeNativeSource` and registration as
`DotNativePluginEntry`. Source ProjectReferences require explicitly importing
these targets for app publish; NuGet imports buildTransitive targets automatically.

This provides dependency declarations, not manifest/resource/entitlement merging.

## Declarative iOS binary SDK dependencies

Native iOS plugins may ship `Platform/iOS/apple-dependencies.json` with schema
version 1. The CLI and DotNative.App publish tool use the same resolver. NuGet
plugins expose the manifest from their `buildTransitive` targets:

```xml
<DotNativeAppleDependencyManifest Include="$(MSBuildThisFileDirectory)../Platform/iOS/apple-dependencies.json" />
```

Source ProjectReferences must import the plugin targets for publish, as with
`DotNativeNativeSource`. The preview CLI discovers the conventional manifest
path alongside the plugin's iOS Swift sources.

A manifest has this shape (replace the illustrative checksum with the archive's
verified SHA-256 before use):

```json
{
  "schemaVersion": 1,
  "dependencies": [{
    "id": "vendor-sdk",
    "version": "1.2.3",
    "url": "https://vendor.example/sdk-1.2.3.zip",
    "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
    "xcframeworks": ["VendorSDK.xcframework"],
    "resourceBundles": ["VendorResources.bundle"],
    "systemFrameworks": ["Security"]
  }]
}
```

The paths are exact archive-relative paths, without globs. Declare all binary
components required by the SDK; this is not a SwiftPM/CocoaPods source-package
resolver. `resourceBundles` and `systemFrameworks` are optional. Each dependency
ID must pin the same version, URL and checksum across plugins. Compatible
component/resource/system-framework selections are merged; conflicting pins fail
rather than choosing an arbitrary version.
Frameworks or bundles with the same destination must have identical contents.

The resolver verifies new and cached ZIP archives, selects `ios-arm64` or
`iossimulator-arm64` using XCFramework `AvailableLibraries` metadata, verifies the
binary architecture and deployment target, and validates cached extracted files.
Cache entries live outside NuGet packages in `~/.cache/dotnative/apple`; override
with `DOTNATIVE_APPLE_CACHE`. OS file locks serialize concurrent downloads and
extraction. Invalid checksums, missing/ambiguous slices, unsafe ZIP paths, selected
slice symlinks and unsupported manifest fields stop the build. A corrupt archive
must be removed and downloaded again; corrupt extracted files are rebuilt from
the verified archive. Verified archive caches support offline builds.

Static frameworks are linked; their nested resource bundles are copied into the
app. Bare framework `PrivacyInfo.xcprivacy` files are placed in named resource
bundles so multiple SDK privacy manifests are retained. Dynamic frameworks are
embedded, thinned to arm64 and signed before the app is signed. Framework-owned
resources remain in dynamic frameworks. A staging journal tracks owned paths so
removed dependencies leave no stale bundles and retries do not overwrite app-owned
resources. Generated .NET bindings projects receive equivalent NativeReference
and BundleResource items.

The iOS runner must include the **Embed native plugin resources** phase from the
current template. It runs the generated `plugin-bundle.sh` after linking and before
Xcode signs the app. Custom runners upgrading from older DotNative versions must
adopt this phase. Publish fails with `DNAPPLE001` if the standard runner lacks
it. A custom packager must explicitly set `DotNativeAppleBundleStagingHandled=true`
and invoke the generated script before signing. Preview simulator packaging uses the same staging implementation;
physical preview regenerates an equivalent Xcode phase.

The current manifest contract covers iOS arm64 device/simulator `.framework`
XCFrameworks. Raw `.a` XCFramework slices, macOS/Catalyst, source packages,
extensions, entitlements, application Info.plist policy and privacy declarations
for the app's own code remain explicit integrations. Unsupported slices fail;
they are never silently packaged as another platform. Signed device push/ad E2E
verification is separate from compilation and bundle integrity tests.

Regression checks on macOS/Xcode:

```sh
dotnet run --project tests/AppleDependencies.Tests
python3 tests/native/test-notification-channels.py
python3 tests/native/test-empty-apple-registration.py
python3 tests/native/test-apple-staging-contract.py
# With the Firebase, GoogleMobileAds and LocalNotifications sibling repositories:
python3 tests/native/test-apple-sdk-bundle.py iossimulator-arm64
python3 tests/native/test-apple-sdk-bundle.py ios-arm64
```

The SDK bundle fixtures compile/link real SDKs, inspect bundled privacy resources
and verify ad-hoc signatures. They do not launch apps, request ads or send push
notifications. Firebase and GoogleMobileAds now use this manifest path instead
of plugin-specific `fetch.sh` scripts.

## Optional static plugin APIs

A plugin can use `PluginRuntime.Current` to obtain the current session's `Channels`
and renderer `Target`, without requiring application service registration. The context
is available during startup, UI/lifecycle callbacks and their async continuations.
It is execution-context scoped, not a global last-created application. Calls outside
that scope or after shutdown throw; independently started threads must receive an
explicit service. Resolve this context at each static call instead of caching it globally.
Keep injectable instance APIs for testing and custom configuration.
