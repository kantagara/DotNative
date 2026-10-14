# Native UI hot reload preview

On an Apple Silicon Mac with .NET 10 SDK, Rust and Xcode installed:

```sh
dotnative run ios --hot-reload --device booted
dotnative run android --hot-reload --device emulator-5554
dotnative run mac --hot-reload
```

Run each command in its own terminal. Boot an ARM64 Android emulator first;
Android also requires the SDK, NDK and Java 17. The launcher defaults to
`Samples/WidgetSamples`. Each platform has an independent managed application
session and separate build output under `tools/DotNative.DevHost/obj/Previews`.

C# runs under `dotnet watch` on the Mac. Native previews render the existing binary
commands using Rust and UIKit, Android controls or AppKit. Input returns through
the binary event protocol. Layout and native UI work stay on the OS main thread;
socket reads and writes in the native runner use background threads. Preview
ports 4971–4973 bind to loopback; Android uses `adb reverse`.

Edit a string or styling expression in `HomeScreen.Build` and save. Supported
.NET code updates trigger the existing RenderHost invalidation hook. The managed
process, DI services and reactive state remain alive. A compilation error leaves
the previous UI running. Unsupported edits may require a watch restart; currently
restart the native preview too after restarting the host. State is not persisted
across process restarts.

This is an experimental **UI preview**, not on-device NativeAOT hot reload.
Direct native plugin calls are not transported. `OperatingSystem` describes the
Mac host; injected `PresentationTarget` describes the renderer. The app source
receives its platform symbol. The demo host currently references the framework
and DeviceInfo package explicitly; it does not import arbitrary app package
references or platform-native source. Release builds do not enable Rust's
`dev-host` feature and retain their existing NativeAOT pipeline.

## Install the repository CLI

From the repository root (one time):

```sh
dotnet pack tools/DotNative.Cli -c Release -o artifacts/cli
dotnet tool install --global DotNative.Cli --add-source artifacts/cli --version 0.1.0-beta.1
```

Then run `dotnative run ios --hot-reload` from the repository or sample directory.
Use `--project Samples/Counter` to select another sample from the repository root.
This CLI is currently repository-backed: installing its tool package alone does
not install the native engine, development host or platform templates.

The CLI checks for an existing session before building and waits for the host's
socket to bind before launching the native app. A second session for the same
platform exits with an explanatory message. Stop the original CLI with Ctrl+C
before launching it again. Ports occupied by unrelated processes are never killed.

In Rider, open `Samples/Samples.sln`, run the CLI in the integrated terminal,
and edit/save `WidgetSamples/Screens/HomeScreen.cs`. The CLI owns `dotnet watch`;
Rider's built-in Hot Reload button is not involved in this preview mode.

## Choose a device

```sh
dotnative devices
dotnative devices --all
dotnative run --device mac --hot-reload
dotnative run --device emulator-5554 --hot-reload
dotnative run --device <ios-simulator-udid> --hot-reload
```

The device determines the platform and native build target. `devices` works
outside the repository too. `--all` also includes shut-down iOS simulators;
selecting one boots it before building. Android discovery lists ADB-connected
emulators and physical devices, including unauthorized/offline states. Only
ready ARM64 Android devices can run the current preview. Physical iOS devices use a signed runner and a USB tunnel (see below).

With no `--device`, one ready matching target is selected automatically. Multiple
ready targets produce an interactive choice (or an explicit error when stdin is
redirected). Exact IDs avoid ambiguity between simulators with the same name.
The optional `ios`, `android` or `mac` positional argument filters the candidates;
a conflicting platform and device is rejected before building.

## Physical iPhone over USB

Connect and trust the iPhone, enable Developer Mode, and keep it unlocked during
installation and launch. Xcode must have the signing account configured.

```sh
dotnative devices
dotnative run --device <iphone-id> --hot-reload --team <APPLE_TEAM_ID>
```

Alternatively set `DOTNATIVE_APPLE_TEAM`. The team ID is not embedded in the
repository. The CLI builds `aarch64-apple-ios` with the development feature,
generates a separate native preview Xcode project, signs it through Xcode's
automatic provisioning, installs it with `devicectl`, and connects over USB.
The release application and its NativeAOT publish phase are not replaced.

The iPhone listens on loopback; the C# CLI uses macOS usbmuxd to relay its binary
traffic to the managed host's loopback socket. No Wi-Fi listener, external Python
tool or `iproxy` installation is needed. This uses the usbmuxd plist control
protocol described by [libusbmuxd](https://github.com/libimobiledevice/libusbmuxd);
UI commands and events remain binary.

`USB preview connected` confirms the tunnel; `Native frame committed` confirms
that the engine completed layout and native synchronization. Supported code edits
are applied by `dotnet watch`; they do not replace native code or remote native
plugin calls. Disconnecting USB ends the session. Restart the CLI to reconnect.
One session per platform remains the current limit, so an iPhone and iOS simulator
cannot run simultaneous preview sessions. Android and Mac can run alongside it.

Channel-based plugins now execute on the native device in preview. FilePicker supports iOS and Android; direct native imports still require a local/release host. See [plugin channels](plugin-channels.md). Native plugin source changes require restarting the CLI session.
