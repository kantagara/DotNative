# DotNative plugins

DotNative plugins use native Kotlin/Swift implementations and managed channel APIs,
or managed implementations where appropriate. See [plugin authoring](../docs/plugin-authoring.md)
for the current registration, build and dependency contracts.

The publication inventory includes 34 sibling repositories named `DotNative.*`.
This is a repository count, not a claim that 34 plugins are production-tested on
five platforms. `DotNative.Localization` is a localization toolchain; several
capabilities are managed. Each repository's README and build targets define its
supported implementations. A source file or successful build is not device E2E evidence.

`DeviceInfo` and `FilePicker` also have bundled copies here. Do not assume those
copies and the independently versioned sibling repositories have identical APIs
or support. Pin the version your application actually uses.

## Implemented contracts

- Namespaced request/reply channels and event subscriptions with native handlers.
- Plugin discovery from the application dependency graph and generated registration.
- Android dependency declarations and Apple native source compilation.
- Declarative iOS XCFramework dependencies, pinned SHA-256 verification,
  resource/privacy bundle staging and dynamic framework signing.
- Shared iOS notification delegate and APNs lifecycle observers.

See [Apple dependency tests and limits](../docs/plugin-authoring.md#declarative-ios-binary-sdk-dependencies)
and [publication evidence](../docs/publication-readiness.md). Explicit app configuration
is still required for permissions, entitlements, service identifiers and signing.

## Verification and plans

Native source compilation, managed regression tests, packaging fixtures and live
device tests establish different things. Only the last can validate permission UI,
notification delivery, camera behavior or real SDK service responses. Consult the
individual plugin's evidence before adoption; there is no blanket production guarantee.

Host adapters for **.NET MAUI, Avalonia and Uno Platform are planned**, not implemented.
The current native plugin runtime is integrated with DotNative.
