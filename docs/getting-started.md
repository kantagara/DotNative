# First desktop application

This preview runs from a clone of the repository. Start with Counter before the
larger WidgetSamples demo. Commands below run in the repository root.

The macOS Counter path was exercised from an isolated clone during
[public-preview preparation](publication-readiness.md), including Increment, Reset
and hot reload. Windows/Linux instructions require separate validation on those hosts.

## Shared prerequisites

- Git; .NET SDKs **9 and 10** on PATH (`dotnet --list-sdks`). The CLI targets .NET 10;
  the framework and some test executables target .NET 9.
- Rust **1.94.0**, including Cargo. After cloning, select it for this checkout:

```sh
rustup toolchain install 1.94.0 --profile minimal --component rustfmt
rustup override set 1.94.0
```

No local NuGet feed or globally installed `dotnative` tool is required for the
repository CLI paths below. An internet connection is needed for initial restores.

## macOS (Apple Silicon)

Install Xcode, select it with `xcode-select`, and complete its first-launch setup.
The hot-reload path uses AppKit, the macOS SDK and the managed development host;
it does not require .NET Apple workloads.

```sh
dotnet run --project tools/DotNative.Cli -- run mac --hot-reload --project Samples/Counter
```

An AppKit window should show `Count: 0`. Increment should change it to 1; Reset
should return it to 0. Edit a text literal in `Samples/Counter/App.cs` and save to
try supported hot-reload edits. Changes to startup or native code require a restart.

For native navigation and plugins, replace `Samples/Counter` with
`Samples/WidgetSamples`. Intel Mac is not part of this preview's verified run path.

## Windows (x64)

Install Visual Studio 2022 Build Tools with **Desktop development with C++**, an
MSVC toolset and the Windows SDK. Run from an **x64 Native Tools Command Prompt**
so NativeAOT and native plugin compilation can find the compiler/linker. Install
the Rust MSVC toolchain, not the GNU toolchain.

```powershell
dotnet run --project tools/DotNative.Cli -- run windows --hot-reload --project Samples/Counter
```

The CLI builds the Rust renderer, the C# development host and the NativeAOT WinUI
renderer for the entire UI, including its Windows App SDK runtime. Keep their output files
together. Windows ARM64 is a separate build target; x64 execution under emulation
is not proof of a fully native ARM64 build.

## Ubuntu 24.04 (GTK4)

Install the shared prerequisites above, plus:

```sh
sudo apt-get update
sudo apt-get install -y build-essential pkg-config libgtk-4-dev clang zlib1g-dev
dotnet run --project tools/DotNative.Cli -- run linux --hot-reload --project Samples/Counter
```

Run inside a graphical desktop session. This uses GTK4 on both GNOME and other
desktops; it does not switch to Qt on KDE. Other distributions need equivalent
GTK4 development packages (GTK 4.8 or newer).

## NativeAOT desktop demo

On Windows/Linux, use the same native build prerequisites and:

```sh
dotnet run --project tools/DotNative.Cli -- run windows --bindings --project Samples/WidgetSamples --build-only
# On Linux, replace windows with linux. Omit --build-only to launch.
```

This builds a native C# host plus a separate Rust renderer. It is a preview
architecture, not a single-process packaged application. Output is under
`artifacts/bindings-integration/desktop/<platform>/WidgetSamples`.

Apple/Android SDK-binding builds have additional workload/version requirements.
Use the dedicated [bindings guide](platform-bindings.md); do not install those
workloads merely to run the desktop hot-reload quickstart.

## Troubleshooting

- A missing `Microsoft.NETCore.App 9.0` means the .NET 9 runtime is missing; install
  SDK 9 alongside SDK 10. Do not assume SDK 10 runs every net9.0 executable.
- Close a previous preview before starting another for the same platform. The CLI
  refuses to take over a port held by another process.
- A native compile error needs the platform SDK/compiler, not another NuGet feed.
- Run `dotnet run --project tools/DotNative.Cli -- devices` to inspect detected targets.
- Include the full command and the first failing build error in an issue.
