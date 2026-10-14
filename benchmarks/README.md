# dotNative / MAUI performance fixtures

The `dotnative-maui-v4` contract uses the same 10,000 immutable records and actions in both applications. Fixed rows are 64 logical units; automatic rows contain a 48-unit button and detail content with controlled extents of 0, 16 or 80 units. These extents are whole physical pixels at the Pixel 7 density (2.625), avoiding different per-row/cumulative rounding. This isolates list performance from platform font metrics; native core tests separately cover intrinsic content sizing. Both lists have a 320 × 320 logical-unit viewport, 14-unit row fonts and an explicit light theme. The v4 readiness marker rejects older fixtures. Actions cover jump/end, fixed/automatic sizing, updating a visible record, growing/shrinking its content, prepend, reverse, clear and reset.

`Shared/OperationProbe.cs` measures **synchronous managed calls only**. It does not measure rendering, frame delivery or input-to-presentation latency. OS tools below collect separate metrics.

## Build fixtures

Stage dotNative with `python3 benchmarks/stage-dotnative.py artifacts/benchmark-dotnative`, then use its generated `Platform/{Android,iOS,Mac}/run.sh` scripts. These compile source with NativeAOT and Release native runners. Android uses a local debug signing key with `debuggable false` and `profileable` enabled. Do not use a downloaded desktop preview as a mobile/macOS comparison target.

MAUI requires .NET 10 with platform workloads. Build `benchmarks/Maui/MauiBenchmark.csproj` with `-c Release -f <TFM> -p:TargetFrameworks=<TFM> -p:RuntimeIdentifier=<RID>`:

| Platform | TFM | RID |
| --- | --- | --- |
| Android | net10.0-android | android-arm64 |
| iOS simulator | net10.0-ios | iossimulator-arm64 |
| iPhone | net10.0-ios | ios-arm64 |
| Mac Catalyst | net10.0-maccatalyst | maccatalyst-arm64 |
| Windows | net10.0-windows10.0.19041.0 | win-x64 or win-arm64 |

Do not restore/build different MAUI targets concurrently into the same `obj` directory. Simulator builds must have valid ad-hoc signatures; device builds require normal provisioning. `windows/build-maui.ps1` copies only sources into a Windows-local directory and publishes a self-contained, unpackaged app.

Record SDK/workload versions, runtime mode, architecture, trimming and AOT settings with each comparison. MAUI is pinned to Controls 10.0.110; the current defaults are not claimed to match dotNative's NativeAOT settings. Windows dotNative uses the native host plus a bundled DevHost process; count
**both** processes. For the Release baseline, build the staged fixture with
`dotnative run windows --bindings --project artifacts/benchmark-dotnative --build-only`
on Windows so the C# host is NativeAOT. A normal `--hot-reload` host uses the CLR
and belongs in a separate development-mode comparison. ARM Windows running x64 apps is an emulated test.

## Android

Build/install instrumentation:

```sh
cd benchmarks/android
./gradlew :runner:assembleDebugAndroidTest
adb install -r runner/build/outputs/apk/androidTest/debug/runner-debug-androidTest.apk
```

From the repository root, run each framework sequentially on the same device:

```sh
python3 benchmarks/run-android.py --serial DEVICE --framework dotnative \
  --apk APP.apk --output artifacts/perf-android-dotnative --iterations 10 \
  --runtime-mode nativeaot --build-revision COMMIT
```

Repeat with `--framework maui`, its APK and its actual runtime mode. Metadata includes APK size/hash, OS fingerprint and collector revision; battery and thermal snapshots are saved before/after the run. A separate `MemoryTest` records live `dumpsys meminfo` at initial/fixed-jump/variable-jump/clear-reset states in `memory.txt`, outside timed frame/startup capture; it terminates only its target test app. Post-benchmark snapshots of an already-killed process are invalid. Emulator smoke tests require `--allow-emulator`. AndroidX Macrobenchmark captures cold startup, fixed/variable scrolling and mutations, preserving JSON and Perfetto traces. `CompilationMode.None` preserves packaged Release behavior. Cold-start mode applies only to startup; interaction scenarios restart the app in setup so it remains alive during measurement. Missing frame samples are failures, never zero-time results.

## iOS and macOS

Provide the Release app and an explicit iOS destination ID; the collector installs that exact artifact before launch. Keep the macOS app at its specified path. Keep the desktop unlocked. Generate/run XCTest with:

```sh
python3 benchmarks/run-apple.py --framework dotnative --platform ios \
  --app APP.app --destination 'platform=iOS Simulator,id=UDID' \
  --output artifacts/perf-ios-dotnative --iterations 10
```

Use `--platform macos --destination 'platform=macOS,arch=arm64'` for desktop, and repeat with `--framework maui`. Physical iOS testing also requires `--team TEAM_ID` and a provisioned test runner. Requires the Ruby `xcodeproj` gem and Xcode. Keep each output directory separate. The macOS XCTest runner is ad-hoc signed and launches the exact `--app` URL;
this avoids accidentally measuring another registered build with the same bundle ID.
The runner waits for generation-specific completion and visible target rows, not
only an action status. `--test startup|fixedRows|variableRows|mutations` selects one
journey; `--test scrolling` selects both row-sizing journeys. Record `--runtime-mode` and `--build-revision`; unknown values do not qualify
as a release comparison. Metadata includes bundle bytes, OS/model and collector revision.
XCTest retains startup metrics, memory and UI journeys in `results.xcresult`, with structured metrics exported to `metrics.json`. Physical iOS metadata records the phone OS/model separately from the Mac host. iOS held drags verify the 320 × 320 viewport and record observed row movement in `scroll-evidence.json`, including XCTest's warmup iteration. This remains smoke evidence until paired displacement and timing are qualified. iOS requests scrolling/deceleration signposts; check the actual reported metric identifiers and frame counts before interpreting them. Zero frame counts do not establish FPS, even when XCTest also reports a frame-rate value. macOS clock metrics include automation and idle waits and **are not FPS**.

The MAUI iOS fixture declares a launch screen to prevent legacy iPhone compatibility scaling. Check the final signed bundle's `Info.plist` after rebuilding: a cached manifest can retain the older settings. Bundle sizes are uncompressed `.app` bytes, not IPA download sizes. XCTest absolute/peak physical memory counters measure footprint; the plain `Memory Physical` counter measures a change during the journey. Startup requests Apple's first-frame presentation metric, with termination outside the measurement. This is distinct from a responsive-launch metric or OS-cache-cold startup. Metadata records the exported launch sample count; a successful launch journey with no samples does not qualify as startup performance evidence.

## Windows

The compiled driver can also run the WidgetSamples core regression locally:
`Driver.exe --core-regression C:\bench\WidgetSamples C:\bench\core-results`.
This exercises navigation, theme/radio state, window move/resize and fast list
scrolling without requiring a PowerShell script execution policy change.

Run in the interactive logged-in user session:

```powershell
dotnet publish benchmarks\windows\Driver -c Release -r win-x64 --self-contained true -o C:\bench\driver
benchmarks\windows\run.ps1 -Framework maui -Executable C:\bench\MauiBenchmark.exe `
  -Driver C:\bench\driver\Driver.exe -Output C:\bench\results-maui -Iterations 10
```

Repeat with dotNative's bundled `dotnative-desktop.exe`; its self-contained managed host must be beside it under `managed`. The harness sets `DOTNATIVE_PREVIEW_PORT=14974` in both child processes to isolate the fixture from an existing preview; override with `-PreviewPort` if occupied. Add `-VirtualMachine` inside a VM. The harness measures process launch to accessible `Ready` status and action-to-new-status latency, plus process-tree working set, private committed bytes, and private resident RAM (`private_working_set_bytes`). Memory counters refresh after every action; older outputs without `private_working_set_bytes` must not be used as retention measurements. The harness aligns the client area to 1100 × 1000 and records renderer/managed-host hashes separately. It checks generation-specific completion, visible first/Jump/End rows (except an
empty list), including jump after clear. Metadata records the executable's PE
architecture separately from the driver/OS architecture, and application-directory
bytes excluding PDBs and logs. That is uncompressed disk size, not archive size.
The Windows collector does not yet capture compositor frame timing or scroll gestures. Windows FPS comparisons need an additional ETW/PresentMon capture.

Set `DOTNATIVE_BENCH_RUNTIME_MODE` and `DOTNATIVE_BENCH_BUILD_REVISION` to the actual
build settings/revision; the collector records unknown when they are absent.

Set `DOTNATIVE_BENCH_ROUNDS=10` to repeat mutation journeys in each process without restarting it; `round` in the raw samples identifies retention trends. The default is one journey.

## Interpretation and remaining coverage

Two-iteration runs, emulator/simulator runs and VM runs are smoke checks. Their raw files are useful for debugging, not HN performance claims. Run at least ten repetitions on the same physical hardware, without concurrent builds or other benchmarks, alternate framework order, and retain every sample and failure. Compare startup, memory, UI latency and frame timings separately; never convert automation journey durations or `BENCH_OP` values to FPS.

MAUI uses `ObservableCollection` replacement notifications for Update/Expand so a visible changed row stays in view; structural actions replace the collection. Structural snapshot replacement in MAUI and keyed `SetItems` in dotNative have different anchoring semantics. Before publication, verify visible content, scroll distance, text scale and window size, and either align those semantics or disclose them. All renderers reuse compatible native row subtrees containing layout, text and buttons within a list. Stateful controls, active interactions and incompatible structures fall back to creation/deletion; see [virtual list lifecycle](../docs/virtual-lists.md). WinUI additionally reuses up to 32 text controls and 32 buttons, excluding focused/pressed controls and controls with asynchronous decoration resources. Row-subtree recycling keeps logical IDs distinct and retains no idle row trees. Forms, navigation, sustained memory retention, accessibility and input latency need additional matched fixtures; this list suite does not establish their performance.

Windows results must identify the renderer revision: historical Win32 runs are not
WinUI 3 baselines. Current Windows UI uses WinUI 3 with a bounded virtual-list
window. Windows journey timings do not measure frame pacing or establish FPS.

Android v4 scrolling uses ten held drags (five forward, five backward). Each starts with a 40%-viewport drag over 384 ms, corrects observed displacement to 37.5% of the viewport while the pointer remains held, then releases at 2400 ms. This removes touch-slop/coalescing differences and residual fling velocity. Corrections are bounded to 25% of the nominal drag and recorded; their frames remain in the metric. Input uses absolute deadlines and acknowledged dispatch. Actual per-drag duration is bounded to 2400–2440 ms; paired runs must differ by at most 20 ms per drag. The collector records actual displacement from common fully visible row anchors for every drag. Qualify paired results before comparing frame timing:

```sh
python3 benchmarks/verify-android-scroll.py artifacts/DOTNATIVE_RUN artifacts/MAUI_RUN
```

The verifier requires the same physical device, viewport, gesture, scenario and repetition count, at most two physical pixels of displacement difference per drag, and net drift below 10% of one gesture per iteration (native touch slop consumes part of each drag). A successful individual run alone does not establish scroll parity. Previous v2 fling results and v4 controlled-drag results measure different journeys. Apple scrolling still needs equivalent observed-distance qualification.

For slow USB transfers, `--defer-traces` pulls the AndroidX result JSON and leaves raw Perfetto files on the device. Metadata records `trace_remote_directory`; copy that directory before uninstalling the collector or clearing its storage. Deferred traces are not yet backed up locally.

Local Pixel 7 validation (2026-10-08, contract v4): variable scrolling passed ten repetitions per framework with identical initial row positions and 3,150 physical pixels of observed movement in each repetition. P95 frame CPU time was 8.94 ms for DotNative NativeAOT and 9.86 ms for MAUI Mono AOT Release; P95 frame overrun was -5.67/-4.86 ms. AndroidX reported zero thermal-throttle sleeps. These are controlled held-drag measurements, not FPS or a general application-performance claim. Initial live PSS snapshots were 84.2/134.2 MiB; single snapshots do not establish memory retention. Fixed-row qualification is a one-repetition smoke check, not a statistical performance baseline.

Local iPhone 16 Pro validation (2026-10-08, iOS 27.0 beta, Xcode 26.6, contract v4): fixed/variable scrolling and mutation journeys pass for both Release fixtures. UIKit row buttons now surrender their touch when list scrolling starts. The MAUI launch screen prevents the older 402 × 402 compatibility-scaled viewport; both signed apps report 320 × 320. Uncompressed bundles are 9.58 MiB DotNative NativeAOT / 44.68 MiB MAUI Mono AOT Release. A single measured variable-row iteration reports absolute physical footprint of 28,608.280 / 53,708.568 kB; these are XCTest footprint counters, not Android PSS or retention statistics. Scroll runs include a warmup plus one measured iteration. Observed displacement differs slightly, and both apps report zero scroll frame counts, so their reported FPS is excluded from performance comparisons. Failed and compatibility-scaled probes are also excluded. Ten-iteration startup attempts did not produce a complete metric series: XCTest exported no samples or rejected inconsistent per-iteration metric counts, for both responsive-launch and first-frame probes. Those runs are excluded from startup comparisons; the collector rejects incomplete startup sample counts. This device/SDK combination has not produced a qualified startup baseline.
