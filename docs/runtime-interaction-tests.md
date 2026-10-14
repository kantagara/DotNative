# Runtime interaction tests

These tests execute the complete preview path: OS input → native control → Rust
event transport → C# state/style resolution → native presentation. The managed
code runs in the desktop development host. They do **not** validate on-device
NativeAOT packaging, physical devices, or every style/backend combination.

The dedicated fixture is `tests/native/InteractionApp`. Its production runners
are copied from the template into a temporary directory; Counter and WidgetSamples
are not altered. There is one ordered scenario per platform, containing multiple
assertions. This is separate from the managed test-group and Rust unit-test counts.

## Executed coverage (2026-10-02)

| Scenario | Android 16 / API 36.1 ARM64 emulator | iOS 26.5 ARM64 simulator |
| --- | --- | --- |
| Radio touch selects and updates managed counter | Passed, injected system touch | Passed, XCTest tap |
| Pressed style while pointer is held | Passed, native wrapper alpha 0.4 | Passed, simulator screenshot during XCTest hold |
| Selected style and reset | Passed, native alpha 0.65 → 1 | Passed, screenshot pixel checks |
| Cancellation | Passed, injected ACTION_CANCEL, no selection | Passed, drag outside, no selection |
| Button drag outside cancels activation | Passed | Passed |
| Button tap updates managed counter | Passed | Passed |
| Disable radio while held | Passed, alpha 0.2 and no action on release | Passed, pressed → disabled pixels during the same hold; no action on release |
| Disabled radio rejects input; re-enable works | Passed | Passed, includes disabled screenshot pixel check |
| Hover enter/exit on Button, RadioButton, TextField, Switch, Slider | Passed, system mouse motion and native alpha | Not exercised |
| Native view identity across updates | Passed, same Java object | Not asserted |

Android uses `UiAutomation.injectInputEvent`, not `performClick`, `setPressed`, or
manual invocation of a hover callback. UIKit receives XCTest-generated gestures.
The iOS screenshot assertions sample an unobscured red background at base,
selected and disabled opacity, with a small color tolerance.

During an eight-second XCTest hold, the host independently captures simulator
screenshots with `simctl`. `PixelProbe.swift` reads pixels using CoreGraphics.
The observer requires both pressed `(255,153,153)` and then disabled
`(255,204,204)` pixels before the release marker in the disable-during-hold
scenario. XCTest checks the managed counter afterward. Evidence is saved in
`held-pixels.json` and the named PNG captures; missing phases or pixels fail the
runner even if XCTest passes. The iOS fixture delays disabling for four seconds
so the input driver and screenshot observer can first verify the pressed state;
Android/Windows retain their shorter delay.

The first iOS preview launch exposed two link failures: CoreText was omitted from
the CLI's Apple link command, and a plugin-free app had no registration symbol.
The CLI now links CoreText and builds an empty registration function when no Swift
plugins are present. The successful iOS run exercises both fixes.

## Windows system input

Executed on Windows 11 ARM64 (build 26100.8875) in Parallels, with an x64 native
renderer under Windows emulation and an ARM64 managed host. The PowerShell harness
uses `SendInput` in the signed-in desktop session, queries actual native focus and
wrapper alpha, and checks managed counters. It covers:

- Hover enter/exit on Button, RadioButton, TextField, Switch and Slider.
- Held radio/button press, selection/reset, drag-out cancellation, rapid repeated clicks.
- Disabling a held radio, rejection on release, re-enable and stable radio HWND.
- Tab/Shift+Tab traversal, focused opacity on TextField/Switch/Slider, and Space radio activation.
- Native smoke, including compositor-clock frame delivery and callback cleanup.

The Windows native text smoke also validates custom-font registration, variable
width and reset, letter spacing, uniform line height, visible-line ellipsis for
hard breaks, start/middle/end elision without splitting surrogate pairs or combining
clusters, UTF-16 rich
run ranges (including a preceding surrogate pair), and actual red glyph pixels
followed by foreground reset. The same checks run offscreen through
`cargo test --features desktop,dev-host --lib directwrite_layout_and_rendered_pixels`
and are wired into the Windows CI job. CI does not run the interactive mouse and
keyboard harness. Resource teardown is explicit before the UI/test thread exits.

This run exposed lost rapid clicks (`WM_LBUTTONDBLCLK`), reverse Tab traversal
caused by HWND paint stacking, and unchecked radios losing `WS_TABSTOP` through
`BM_SETCHECK`. The renderer now preserves ordinary press/release behavior for
rapid clicks and traverses eligible controls in authored tree order.

Build `dotnative-desktop` for `x86_64-pc-windows-gnu` with features `desktop,dev-host`
and copy it as `InteractionApp.exe` into a bundle directory. Publish its adjacent
managed host (choose the runtime matching your Windows machine):

```sh
dotnet publish tools/DotNative.DevHost/DotNative.DevHost.csproj -c Release -r win-arm64 --self-contained true -p:DotNativeApp="$PWD/tests/native/InteractionApp" -p:DotNativeStartupType=InteractionApp.Program -p:DotNativePlatform=WINDOWS -o /path/to/bundle/managed
```

Run from an interactive Windows desktop, with preview port 4974 free:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/native/windows-interactions.ps1 -AppDirectory C:\path\to\bundle -OutputDirectory C:\path\to\results
```

The test temporarily owns mouse/keyboard input, closes its own preview afterward,
and restores the previous foreground window. Results include `PASS.txt` or
`FAIL.txt`, a transcript, smoke/preview logs, and a screenshot on success. The
screenshot is diagnostic; opacity assertions read native alpha rather than pixels.

The separate `tests/native/windows-navigation.ps1` harness runs WidgetSamples for
five push/Back cycles. It samples the first push every 25 ms, requires at least six
distinct compositor frames, rejects long near-black snapshot bands and checks
transition completion timing. Frame diversity alone does not establish that a
transition is flicker-free. With `-VerifyFade`, the harness also compares pixels
that match in the initial and final pages: those pixels must stay unchanged in
the sampled intermediate frames. This detects the background flash caused by
fading both source-over layers simultaneously. The harness fits the window inside
the display before capture. It does not measure every presented frame or refresh
rate on a physical 120 Hz display. Native smoke separately checks that a reactive
style update during slide/fade cannot expose either live page HWND.

Regression comparison on Windows 11 ARM64 with an x64 renderer: the fade pixel
check rejects commit `d5b2b1f` at 150 ms (156 of 811 matching endpoint pixels
changed). The direct HWND-tree snapshot implementation passes the same check in
light and dark modes. This is evidence for that regression, not a guarantee that
all animation artifacts or every display configuration are covered.

## GTK system input

Executed on Debian 12 ARM64, GTK 4.8, in a dedicated Xvfb/AT-SPI session. The
harness uses XTest events through `xdotool`; AT-SPI only locates controls. Screenshots
and `pixels.json` verify hover on all five controls, pressed/selected/reset colors,
disabling during a hold, focus/blur, Tab/Shift+Tab and Space activation. Managed
counters verify selection, drag-out cancellation and rapid repeated clicks.
Colored fixture borders provide a sample surface above native control chrome.

This test found hover failing to recover after disabling and re-enabling a held
radio. GTK now resets its hover controller on loss of sensitivity/unmapping and
recovers on pointer motion, with duplicate events suppressed. The full system-input
scenario and existing native GTK smoke pass together. The Ubuntu CI job runs this
harness and uploads its logs, screenshots and pixel records; the local run does
not establish results for every GTK version or Wayland.

Build `dotnative-desktop` with `desktop,dev-host`, then publish the managed fixture
as above with `DotNativePlatform=LINUX` and a matching `linux-arm64`/`linux-x64`
runtime. Dependencies include GTK 4.8+, Xvfb, xauth, dbus-x11, at-spi2-core,
python3-pyatspi, python3-pil, xdotool and ImageMagick. Preview port 4975 must be free.

```sh
bash tests/native/run-linux-interactions.sh /path/to/dotnative-desktop /path/to/managed /tmp/gtk-input
```

The runner owns a fresh X11/D-Bus session and its preview processes, and stops them
on success or failure. Native smoke runs before the system-input scenario. This
is an X11 software-rendering test, not a hardware/GPU or Wayland qualification.

## Run mobile tests

Requirements: macOS, repository .NET/Rust/Xcode development prerequisites, and a
booted ARM64 device. Android additionally needs SDK/NDK and Java 17; iOS UI tests
need the Ruby `xcodeproj` gem. The script uses the repository CLI and its exclusive
preview ports, so stop any existing preview for the selected platform first.

```sh
python3 tests/native/run-mobile-interactions.py android --device emulator-5556 --output /tmp/dotnative-android-run
python3 tests/native/run-mobile-interactions.py ios --device YOUR_SIMULATOR_UDID --output /tmp/dotnative-ios-run
```

Use a fresh output directory for each run (Xcode will not overwrite an xcresult
bundle). The script stages the fixture, starts the preview, waits for readiness,
runs instrumentation/XCTest and stops its own process group in `finally`.
Android's HTML report and XML results are copied into the output directory; iOS
writes `input.xcresult`, `held-pixels.json` and hold screenshots. Build/preview/test logs stay alongside them. A failure
returns nonzero and names the relevant log. Emulator/simulator lifetime is owned
by the caller. The script neither wipes devices nor deletes installed user apps.

The fresh Android AVD used locally was named `DotNative_InteractionTests`; the
existing emulator was full, so no existing application data was removed.

## Windows control-label typography

The native Windows smoke now prints real BUTTON/RadioButton controls into a GDI
surface and asserts red glyph pixels. It checks variable-font width changes in
both intrinsic measurement and actual painting, font-axis and spacing reset,
right/RTL alignment, stable HWND identity and unchanged native accessible text.
The system-input harness separately verifies that the new label painting preserves
radio selection, held presses, drag-out cancellation, keyboard focus and Space.
It also samples a screen pixel outside the native button bounds to prove that the
Win32 click-through layered shadow is visible rather than merely accepted by the API.
The same harness positions a magenta Text descendant outside a narrow unclipped
container and samples the escaped pixels, covering native reparenting rather than
only validating the serialized `ClipToBounds(false)` flag.
It also samples orange pixels outside an untransformed source rectangle after
DirectComposition applies scale and rotation to an empty visual box.
These control-paint checks run in the interactive Windows smoke; the offscreen
DirectWrite CI test exercises the shared text engine without requiring a desktop.
The shared managed style test also compiles and retains the complete `ITextFlow`
contract on Button and RadioButton. The interaction fixture applies spacing,
line-height, line-limit, overflow and decoration styles to both controls during
the native Windows system-input run.

## Preview handshake regression

`preview-handshake.py` starts a real managed host with `--once`. It verifies that
an empty connection, an invalid hello and a silent client do not consume the
application session. A subsequent valid hello must receive a native command batch;
only disconnecting that valid client ends the process. The regression failed on
the previous host, which exited after the empty connection.

```sh
python3 tests/native/preview-handshake.py --host /path/to/managed/DotNative.DevHost --platform LINUX --output /tmp/preview-handshake
```

The host limits a pre-handshake read to five seconds and removes the read timeout
once a valid hello is accepted. `DOTNATIVE_DEVHOST_TRACE=1` enables native handshake
stage/retry diagnostics on stderr; it does not log UI payloads. The Linux CI job
runs the real TCP regression before its GTK input test. After the fix, ten
consecutive launches on the Windows VM passed the native interaction harness,
including a launch with a timed-out first TCP connection attempt.

## Gaps still open

- Native ARM64 Windows renderer execution and full Windows visual parity are unverified.
- Physical high-refresh Windows presentation cadence is unmeasured; the navigation
  movement is compositor-driven, but 120 Hz should be qualified on target hardware.
- Existing AppKit smoke checks cover native objects and callbacks, including
  real focus ownership, but not this complete system-input round trip. The local
  standalone macOS input probe reports `AXIsProcessTrusted() == false`; a repeatable
  system-input harness needs Accessibility permission. GTK Wayland remains untested.
- Physical devices, NativeAOT releases, keyboard navigation beyond the Windows fixture, accessibility
  focus, mobile hover on iPad, and all gesture
  cancellation combinations remain unverified.
- Windows offscreen text and GTK system-input tests have CI jobs. Interactive
  Windows input and mobile device tests remain local qualification steps.


## Navigation transition runtime tests

The `tests/native/NavigationApp` fixture runs 24 round trips: Slide, Fade and None,
four curves, and 150/600 ms configured durations. Each push and pop is timed at the
managed await boundary. It verifies the departing DI scope is retained during
push and disposed exactly once after pop completion. These are distinct from the
34 managed test groups.

Executed locally on 2026-10-05:

- iOS 26.5 ARM64 simulator: all 24 round trips, native Back, system swipe completion
  and cancellation, including returning to system gestures after custom animation.
  Custom Slide/Fade tests cancel then complete a real edge drag, check retained
  edits and scope disposal counts, and independently capture screenshot pixels
  while the finger is held to prove intermediate rendering.
- Android 16 ARM64 emulator: all 24 round trips and system Back. The input test also
  samples native fade alpha and an intermediate screenshot pixel. Predictive Back
  tests inject real OS gestures from both edges for Slide and Fade, assert live
  progress on the retained native views, cancel without losing edits or disposing
  the scope, and commit with exactly one disposal.
- macOS/AppKit: all 24 round trips. `macos-navigation-smoke.m` additionally samples
  actual Core Animation presentation layers for slide/fade and all four curves,
  checks intermediate progress and completion ordering, retains native identity,
  and updates page style during an active animation.
- GTK 4 / Linux ARM64 in Xvfb with the Cairo renderer: all 24 round trips. The
  desktop timing runner also supports hiding/remapping the window mid-transition
  to exercise completion when GTK stops frame-clock callbacks.

These establish runtime behavior on those environments. They do not establish
120 Hz physical-device performance, absence of every possible rendering artifact,
older Android runtime compatibility or Wayland/GPU parity.
The existing Windows navigation harness remains the Windows regression check.

```sh
python3 tests/native/run-mobile-interactions.py ios --navigation --device SIMULATOR_UDID --output /tmp/navigation-ios
python3 tests/native/run-mobile-interactions.py android --navigation --device emulator-5554 --output /tmp/navigation-android
```

On Android 14+, use gestural system navigation: the predictive tests deliberately
fail if injected edge input never reaches the OS Back callback. Normal timing
runs require system animations enabled. A separate iOS run checks that Reduce
Motion keeps the outgoing pixels stationary during a cancellable custom gesture;
the runner restores the simulator preference afterward:

```sh
python3 tests/native/run-mobile-interactions.py ios --navigation --reduce-motion --device SIMULATOR_UDID --output /tmp/navigation-ios-reduced-motion
```

Desktop: publish `tools/DotNative.DevHost` with
`-p:DotNativeApp="$PWD/tests/native/NavigationApp"`,
`-p:DotNativeStartupType=InteractionApp.Program` and the matching
`-p:DotNativePlatform=MACOS` or `LINUX`. Then run:

```sh
python3 tests/native/run-navigation-timing.py MACOS --native /path/to/native-runner --managed /path/to/DotNative.DevHost --output /tmp/navigation-mac
xvfb-run -a dbus-run-session -- env GTK_A11Y=none GDK_BACKEND=x11 GSK_RENDERER=cairo python3 tests/native/run-navigation-timing.py LINUX --native /path/to/dotnative-desktop --managed /path/to/DotNative.DevHost --hide-during-transition --output /tmp/navigation-linux
```

The AppKit presentation-layer regression and GTK timing/hidden-window scenarios
are included in CI; mobile suites require the simulator/emulator commands above.
