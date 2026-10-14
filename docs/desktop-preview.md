# Experimental Windows and Linux previews

GTK4 is the Linux renderer; WinUI 3 is the Windows renderer. Counter, WidgetSamples and the
virtual-list benchmark use WinUI controls. The
Release native renderer consumes the existing binary protocol and uses Taffy.
C# runs separately in DotNative.DevHost under `dotnet watch`; this is the managed hot-reload development
mode. `run <platform> --bindings` separately builds a NativeAOT host/renderer
demo; see [first-run instructions](getting-started.md). Neither path is a signed installer.

## Running

Install .NET 10 SDK and Rust 1.94. Windows requires the MSVC Rust toolchain,
Visual Studio C++ Build Tools and Windows SDK. Linux requires GTK 4.8+ and
`pkg-config` (on Debian/Ubuntu: `build-essential pkg-config libgtk-4-dev`).

```sh
dotnet run --project tools/DotNative.Cli -- devices
dotnet run --project tools/DotNative.Cli -- run windows --hot-reload --project Samples/WidgetSamples
# Run on Linux:
dotnet run --project tools/DotNative.Cli -- run linux --hot-reload --project Samples/WidgetSamples
```

Run on the target OS. The CLI discovers its architecture, builds the native runner
in Release, starts the managed development host and launches the window. Closing
it or pressing Ctrl+C stops owned processes. The repository-root default remains
Counter; use `--project` for WidgetSamples. Windows uses port 4974; Linux uses 4975.
A managed host restart requires restarting the native preview. Supported hot reload
edits preserve the existing session. Direct native imports in the remote managed
host do not execute in the renderer process.

## Coverage

- Stacks, native text/buttons, text input and focus, switches, radio buttons,
  sliders, images, icons and scroll containers.
- Taffy layout, DPI conversion on Windows, native hierarchy and rounded clipping.
- Native headers with actions and Back, using the shared C# Navigator and DI scopes.
- Windows controls, typography, focus, accessibility providers and scrolling are
  WinUI 3. The native shell hosts one XAML island for the entire tree. Win32 is
  limited to window lifecycle, DPI notifications and the message pump.
- WinUI sidebar selection retains menu controls and animates only the content
  canvas for 180 ms; unchanged selections do not restart it. Reduced motion
  disables the content animation. Shared sidebar padding is not applied twice.
- Windows navigation retains both pages until native fade/slide completion. The
  outgoing page stays opaque underneath the incoming page. WidgetSamples places
  its sidebar outside the navigation stack, so switching Home/Cart keeps the same
  sidebar and menu objects. Page motion suppresses the separate sidebar entrance.
- Radio buttons have native groups scoped to their logical parent. Checked events
  synchronize pointer, keyboard and accessibility selection with managed state.
- Linux has GTK navigation presentation with retained-transition runtime checks;
  platform behavior and animation implementations differ. Native GTK sidebar
  replacement is not yet implemented.
- Preview compilation of `Platform/Windows/**/*.cs` and `Platform/Linux/**/*.cs`,
  plus the corresponding `DOTNATIVE_WINDOWS` / `DOTNATIVE_LINUX` symbols.

## Limits

Native service plugins are supported through the documented desktop plugin path.
Platform-targeted C# plugins use the opt-in NativeAOT bindings path; this does not
grant them ownership of the renderer UI thread. General native-view plugin parity,
installers and public runtime package distribution remain incomplete.
The WinUI runtime is mandatory and is published by the CLI beside the native
host. There is no Win32-control fallback. The first full WinUI backend rejects
embedded/variable fonts, non-linear gradients, image tint and shadows before
committing the model; these need WinUI ports. Linear gradients and background
images use WinUI brushes; per-edge border decoration uses WinUI SVG images.
Basic colors, borders, radius, opacity, transforms, rich text, image fit, native
inputs and scrolling are implemented. Advanced IME and keyboard policy parity,
nested scrolling and per-monitor DPI need broader validation.

`ListView` is virtualized by default and keeps only viewport/overscan rows mounted,
including automatic heights and stable-key anchoring. The Windows implementation
uses WinUI ScrollViewer/Canvas rather than the former HWND container pool. This
recycles compatible layout/text/button row subtrees; it is not yet an
ItemsRepeater-owned full item source or a stateful form-row recycler.

## Verification

`desktop.yml` builds on Windows/Linux. Windows publishes the required WinUI
NativeAOT runtime and exercises create, native measure, layout, Button automation,
update and deletion, plus rapid tab selection, retained menu identity, reduced
motion and sidebar content offsets. These checks do not establish absence of
visual flicker in a packaged WidgetSamples build. Old Win32 pixel/navigation results are historical evidence,
not verification of the new renderer. Run smoke checks on the target OS:

```sh
cargo build --release --locked --manifest-path native/core/Cargo.toml --features desktop,dev-host --bin dotnative-desktop
# Windows (publish runtime next to the executable first):
dotnet publish native/winui/DotNative.WinUI.csproj -c Release -r win-x64 -o native/core/target/release
native/core/target/release/dotnative-desktop.exe --smoke
# Linux with a virtual display:
xvfb-run -a dbus-run-session -- native/core/target/release/dotnative-desktop --smoke
```

Local WinUI verification passed the native smoke and two iterations of the
10,000-row benchmark journeys (startup, jumps, automatic heights, content
expansion, reordering, clear and reset). These are functional/UI-latency checks,
not frame-pacing measurements. Verification uses the x64 renderer in a Windows 11 ARM64 VM. This
is emulation, not proof of native ARM64 behavior or physical-device performance.
If `managed/DotNative.DevHost.exe` is bundled beside the renderer it starts and
stops with the window. `DOTNATIVE_PREVIEW_PORT` can isolate benchmark sessions
from a running preview. See [benchmark instructions](../benchmarks/README.md).

The actual WidgetSamples navigation regression is opt-in through the environment
variable DOTNATIVE_WINUI_NAVIGATION_TEST=1. It selects Light/System/Dark through
native radio accessibility providers, checks that their labels fit inside the actual
native control bounds and that sidebar text/icons contrast with its themed surface,
exercises header buttons and the sidebar invocation callback for twelve
transitions, preserves a nonzero Home scroll offset, and checks animation rendering callbacks for retained visible pages
and a stable root/sidebar. It also checks scoped controls are released.
Run tests/native/run-winui-navigation.ps1 with AppDirectory pointing to a bundled
preview and OutputDirectory pointing to the evidence directory.
Native leaf measurement uses an invisible host in the XAML island so control
templates resolve before measurement. Protocol padding and borders are excluded
from native intrinsic measurements because Taffy already accounts for them.
This is a regression check, not a frame-time benchmark.

The Windows runtime is initialized before registering or creating the host window,
so its callbacks retain the self-contained WinUI activation context. The window
regression (`DOTNATIVE_WINUI_WINDOW_TEST=1`, enabled by the PowerShell runner)
checks input-factory lookup from the host callback and exercises the native move
loop and resize. Sidebar selection delegates transitions to an existing navigation
host; it does not start a competing content-translation animation.

The WidgetSamples regression also opens the automatic-height 10,000-row list,
scrolls rapidly in both directions, and checks rendered viewport coverage and
bounded mounted rows on composition frames. This covers a finite tested scroll
rate; it does not promise zero latency for arbitrary jumps beyond the cache.
