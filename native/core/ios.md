# iOS adapter

The iOS-only module `src/platform/ios.rs` uses objc2 0.6.4 and framework bindings
0.3.2. Cargo enables these dependencies only for iOS (including Simulator). UIKit
APIs follow the generated signatures; CGRect uses CGPoint + CGSize, with f32
logical coordinates promoted to CGFloat and no manual pixel rounding.

## Runner integration

On the OS main thread, once per process, after creating a host UIView:

```rust
use objc2::MainThreadMarker;
use core::platform::ios;

let mtm = MainThreadMarker::new().expect("runner must initialize on main");
ios::initialize(host_view, mtm)?; // host_view: Retained<UIView>
```

The `core` library name is the Cargo library name in this repository. Pass an
exclusive mount view whose content bounds match the C# SET_VIEWPORT dimensions.
The runner positions this host within its safe area and reports the remaining
overlap through `dotnative_app_insets` (`WindowInsets` on the C# build context).
Do not attach Auto Layout constraints to managed nodes or externally mutate
their hierarchy/transforms.

The existing C# flush calls now validate and lay out the candidate, commit the
engine, and sync its registry before returning. No second C# call or per-control
FFI wrapper is needed. On iOS, flushing before initialization is a fatal protocol
error. Non-iOS headless behavior is unchanged. The process-lifetime session/reset
restriction from Step 3 still applies.

A standalone Rust-owned Engine can instead use `NativeMeasurer::new(mtm)` and a
`NativeRegistry::new(host, mtm)`, followed by `engine.flush(bytes)?` and
`registry.sync(&engine)?`. Keep that registry paired with the same engine session;
node IDs must not be recycled by substituting another engine.

## Memory, threading and presentation

The registry owns a `HashMap<u32, Retained<UIView>>`. UIKit superviews have their
own strong child references. Deletion calls removeFromSuperview and drops the
map's retain. Dropping a registry detaches all managed views. The caller's host
is retained by the registry; managed views are not retained after removal. UIKit
classes and MainThreadMarker keep the registry/measurer !Send and !Sync.

VStack maps to UIView; Text maps to a UILabel subclass for content padding; Button
maps to an unconfigured custom UIButton. The subclass has only one Cell<CGFloat>
ivar and two UILabel overrides with exact Objective-C ABI signatures. objc2 owns
its Rust ivar initialization/destruction. No raw retained-pointer casts are used.
Generated setters that mark nil as potentially unsafe always receive valid non-nil
UIFont/UIColor values. All other retained conversions use typed superclass casts.

Style consumes the already validated Rust Style, not a cast of the wire payload.
The SET_STYLE payload is 24 bytes INCLUDING its 4-byte node ID. Colors use sRGB
RGBA/255, corner radius is in points, and masksToBounds is true as requested.
This clips visual overflow even though Taffy permits logical overflow. Text and
font changes reset previous values, including empty titles and transparent colors.
NSString conversion is length-aware and supports embedded NUL.

Frames and padding remain Taffy's responsibility. UILabel's content rectangle and
UIButton's contentEdgeInsets position text inside the already-sized border box.
Button contentEdgeInsets is deliberately the legacy API: it is deprecated for
configured buttons, but these custom buttons never use UIButtonConfiguration.
Title labels are single-line/truncating; Text labels wrap without a line limit.

Changed hierarchies are detached before being attached in final child order.
This also handles ancestry inversions without creating temporary UIKit cycles.
Retained node identity survives sibling reordering. Layers use a scoped
CATransaction with implicit actions disabled. UIKit drawing itself occurs through
its normal run loop; the adapter does not rasterize controls into a custom canvas.

The engine is committed before visible synchronization. Protocol/measurement
failure leaves the screen unchanged. Native allocation errors/Objective-C
exceptions are fatal; this is not an attempt to rollback a partly applied UIKit
transaction. No exception may cross the existing C ABI as a recoverable return.

## Native measurement

NativeMeasurer reuses detached UILabel and UIButton prototypes on the main thread,
configures them from candidate text/font, then invokes sizeThatFits with Taffy's
known dimensions and available content size. Prototype padding is zero. Known
dimensions override measured values; nonfinite/negative results are rejected.
Max-content uses CGFloat's greatest finite width/height; min-content passes zero.
Text follows UIKit word-wrapping behavior, not a separate CSS text layout engine.

Visible views are never used for speculative measurement. The prototypes live
only in the measurer; autorelease pools bound temporary NSString/color/font objects
per measure and sync. Explicit font sizes do not automatically scale with Dynamic
Type. Trait/font-metric invalidation and accessibility semantics are future work.

## Verification

```sh
cargo test --manifest-path native/core/Cargo.toml
cargo clippy --manifest-path native/core/Cargo.toml --target aarch64-apple-ios-sim --all-targets -- -D warnings
cargo build --manifest-path native/core/Cargo.toml --target aarch64-apple-ios --release
cargo build --manifest-path native/core/Cargo.toml --target aarch64-apple-ios-sim --example ios_smoke
xcrun simctl spawn booted "$PWD/native/core/target/aarch64-apple-ios-sim/debug/examples/ios_smoke"
```

Start an available Simulator before the last command. The smoke executable runs
on its main thread and uses real UIKit objects. It checks native wrapping/size,
leaf padding, text/styles, identity/reorder, failed-candidate isolation, Unicode
and NUL, deletion with weak-reference deallocation after pool drainage, and the
actual C ABI -> committed UIKit hierarchy path.

Verified with iOS 26.5 Simulator on Apple Silicon; iOS ARM64 release cross-build
also succeeds. This is native object/layout integration verification, not a visual
screenshot test or a packaged iOS application. No events, routing or camera/list
code is included.
