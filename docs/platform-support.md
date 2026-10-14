# Preview support and evidence

This is a capability/evidence matrix, not a production certification. CI results
apply to their exact commit. Physical hardware, emulators and build-only checks
are different evidence. The public preview uses the existing adapters; no Winio
or Qt migration has been implemented.

| Platform | Current implementation | Evidence available | Important limits |
| --- | --- | --- | --- |
| macOS | AppKit controls; NSSplitViewController/NSTableView sidebar | Apple Silicon interactive demo; native focus/navigation/font CI | Intel run path unverified; no notarized installer |
| Windows | WinUI 3 controls, text measurement, scrolling and NavigationView; native OS-window shell | WinUI native create/measure/layout/invoke/update/delete smoke passed in Windows 11 ARM64 VM with x64 renderer | Advanced-style migration remains incomplete; physical ARM64, DPI/IME/accessibility need broader coverage; old Win32 results do not validate WinUI |
| Linux | GTK4 controls and composed sidebar | Ubuntu ARM64 UTM demo; Ubuntu x64 GTK interaction/plugin CI | No Qt/KDE or libadwaita sidebar adapter; no claim of matching every desktop's visual language |
| iOS | UIKit, native navigation and UITabBar | ARM64 simulator demo, bottom-bar show/hide and build checks | Physical-device distribution/signing is not covered by simulator evidence; no App Store release proof |
| Android | Android Views, Material BottomNavigationView | ARM64 emulator demo; targeted bottom-bar runtime test including system-inset placement | Platform .NET binding NativeAOT path experimental; SDK asset versions pinned; no broad device/OEM coverage |

## First-party plugin support

The framework targets five platforms, but a public API or generated plugin
scaffold is not proof that an implementation exists. Current first-party service
coverage is deliberately narrower:

| Plugin/capability | Android | iOS | macOS | Windows | Linux |
| --- | --- | --- | --- | --- | --- |
| `DotNative.FilePicker` | System document provider | UIKit document picker | AppKit `NSOpenPanel` selected a fixture and read its stream in the Apple Silicon app preview | Windows 11 ARM64 `IFileOpenDialog` selected a fixture; native handler read and released its stream | GTK4 4.8.3 dialog selected a fixture and read its stream in a Debian 12 ARM64 Xvfb preview; NuGet manifest discovery and archive build verified |
| `DotNative.DeviceInfo` | Unsupported, throws | iOS major version | Unsupported, throws | Unsupported, throws | Unsupported, throws |
| `DotNative.SecureStorage` | DataStore + Tink + Android Keystore; Kotlin compilation verified, device runtime unverified | Native Keychain; Swift compilation verified, device runtime unverified | Keychain CRUD, enumeration, deletion and separate-process persistence runtime-tested | Credential Manager CRUD and separate-process persistence runtime-tested | Secret Service/libsecret CRUD, enumeration and separate-process persistence runtime-tested on Debian 12 ARM64 with GNOME Keyring |
| `DotNative.Preferences` | API/package reserved; implementation absent | API/package reserved; implementation absent | JSON file, typed values, concurrent access runtime-tested | JSON file, typed values, concurrent access runtime-tested | JSON file, typed values and concurrent access runtime-tested on Debian 12 ARM64 |
| `DotNative.Paths` | API/package reserved; implementation absent | API/package reserved; implementation absent | Runtime-tested | Runtime-tested | Runtime-tested on Debian 12 ARM64 |
| `DotNative.Launcher` | API/package reserved; implementation absent | API/package reserved; implementation absent | Local-only HTTP URI handed to `/usr/bin/open` and the OS handler started | Local-only HTTP URI handed to the Windows shell URL association | `xdg-open` launched Netsurf under Xvfb with a `BROWSER` override; default desktop association remains unverified |
| `DotNative.Connectivity` | API/package reserved; implementation absent | API/package reserved; implementation absent | Snapshot, initial watch event and cancellation cleanup runtime-tested; network-change event unverified | Snapshot, initial watch event and cancellation cleanup runtime-tested; network-change event unverified | Snapshot, initial watch event and cancellation cleanup tested on Debian 12 ARM64; network-change event unverified |
| `DotNative.LocalDatabase` | API/package reserved; implementation absent | API/package reserved; implementation absent | SQLite CRUD, queries and transaction rollback runtime-tested | SQLite CRUD, queries and transaction rollback runtime-tested | SQLite CRUD, queries and transaction rollback runtime-tested on Debian 12 ARM64 |

Six additional capability packages live in separate, unpublished local
repositories beside this checkout. They expose a shared C# API and package
manifest shape for all five platforms, but current implementations are desktop
only. Android Keystore/Tink and iOS native Keychain integration, mobile
paths/preferences/launcher/connectivity/database adapters, and DeviceInfo
desktop implementations remain absent. Do not use Preferences or ordinary app
files for secrets. SecureStorage throws when its OS-protected store is missing;
it never falls back to plaintext.

A validation host referencing these six packages published and ran as macOS
ARM64 NativeAOT; it exercised Paths, typed Preferences, a Connectivity snapshot,
SQLite transactions, and Keychain. Windows 11 ARM64 passed the corresponding
managed runtime checks and published and ran the validation host as NativeAOT
after installing the .NET 10 SDK; its output confirmed all six service checks.
The Windows publish emitted NuGet advisory NU1903 for `SQLitePCLRaw.lib.e_sqlite3`
2.1.10. On Linux ARM64, all six managed packages built
without warnings; the validation host passed under both the .NET 9 JIT and Linux
ARM64 NativeAOT, including GNOME Keyring CRUD, enumeration and separate-process
persistence. Launcher dispatched a loopback-only HTTP URL through the OS URL
association on macOS and Windows. Linux `xdg-open` launched Netsurf under Xvfb
with an explicit `BROWSER` override; the desktop's default browser association
was not tested. Connectivity returned its initial watch snapshot and cleaned
up on cancellation on all three desktop OSes; an actual network-change event
was not simulated. GTK4 FilePicker package discovery generated a native archive with
the exported registration symbol. A separate Debian 12 ARM64 Xvfb app preview
opened the real GTK4 dialog, selected a fixture, and read its expected bytes
through the managed stream API with a NativeAOT DevHost. Apple Silicon AppKit
preview and Windows 11 ARM64 Win32 harness runs also selected files and read
their expected bytes. The Windows Rust renderer and WinUI NativeAOT renderer
both built and started in the VM, but its preview session did not expose the
modal picker to UI automation, so the full Windows preview-to-managed-stream
flow remains unconfirmed. The Linux UI run used Xvfb, not a physical desktop.

FilePicker currently supports a single local file and read-only scoped streams;
it does not persist grants or offer type filters or multi-selection. The Windows
native plugin was exercised directly in an ARM64 Win32 harness. A later full
preview attempt built the Rust renderer and WinUI NativeAOT renderer, but the
VM did not make the modal dialog discoverable to the automation process. Its
modal native dialog cannot be dismissed by cancellation until it returns. Missing presenters and remote
renderer/host OS mismatches must report errors. See the package README for
evidence.

Third-party native integrations require their own platform matrices, permissions,
SDK dependencies and validation evidence.

## Cross-platform limits

- `ListView` is virtualized by default and mounts a bounded viewport plus overscan,
  supports automatic row heights, stable keys and scroll anchoring on all five
  platforms. Container reuse and full cross-item content recycling are distinct;
  see [virtual lists](virtual-lists.md) for backend details and current limits.
- Styling and navigation differ by adapter. Native presentation may intentionally
  vary by OS release. See `adaptive-navigation.md` for exposed sidebar options.
- Native views, textures and external SDK build inputs need their own validation. See `plugin-authoring.md` for the current contract.
- Desktop NativeAOT uses a host/renderer process pair and loopback transport.
  Development hot reload uses a managed host. These are different configurations.
- General accessibility, assistive-technology interaction, IME composition and
  multilingual text need more platform/device testing than the existing checks.
- No published, reproducible end-to-end benchmark establishes universal 120 Hz,
  startup or memory claims. See `production-roadmap.md` for measurement criteria.

## Verification at the start of the release pass (2026-10-05)

Commit `1532995`: Windows/Linux desktop workflow passed. The native GTK input,
Windows text and AppKit jobs passed in Core checks. The core job stopped at a
formatting failure before its managed/Rust/template steps; it must be rerun after
repair. Local results alone do not replace that rerun.

- [Core checks](https://github.com/kantagara/DotNative/actions/workflows/ci.yml)
- [Desktop checks](https://github.com/kantagara/DotNative/actions/workflows/desktop.yml)

## Local desktop plugin validation (2026-10-08)

On Debian 12 ARM64 with .NET SDK 9.0.318, all six desktop service packages built
with zero warnings/errors. The validation host passed Paths, typed/concurrent
Preferences, a Connectivity snapshot, SQLite CRUD/rollback, and GNOME Keyring
SecureStorage; a secret was recovered and deleted across separate processes.
With Secret Service absent, SecureStorage raised
`SecureStorageUnavailableException` and explicitly reported that no plaintext
fallback was used. The same validation host published and ran as Linux ARM64
NativeAOT. GTK4 4.8.3 FilePicker source compiled with warnings-as-errors, and
`dotnative plugin build --platform linux` produced an archive containing the
exported FilePicker registration. A Debian 12 ARM64 Xvfb app-preview run opened
the GTK4 dialog, selected a fixture, and read its bytes through the managed
stream API with a NativeAOT DevHost. The repository's .NET harness passed all
40 tests. These local ARM64 results do not replace the separate CI workflows
or certify x64, physical desktop presentation, or release packaging. Windows
11 ARM64 NativeAOT publish and service runtime checks passed with the .NET 10
SDK; the full Windows FilePicker preview UI flow remains unverified.

Release-specific clean-build evidence belongs in the release notes, including
commit, OS, architecture and exact command. Do not promote a build-only check to
a runtime or performance claim.

SecureStorage mobile update: the separate GitHub plugin now includes Android
DataStore/Tink/Keystore and iOS Keychain backends. This supersedes earlier
desktop-only SecureStorage statements above. Device/simulator runtime verification
of these new mobile backends remains outstanding.
