# DotNative core — Steps 3–4

`src/lib.rs` exports the requested synchronous C ABI. `src/protocol.rs` reads
unaligned packed bytes with explicit little-endian conversion. `src/engine.rs`
owns the UI model, persistent Taffy IDs and calculated logical frames.

Build and verify:

```sh
cargo test --manifest-path native/core/Cargo.toml
cargo clippy --manifest-path native/core/Cargo.toml --all-targets -- -D warnings
cargo build --manifest-path native/core/Cargo.toml --release
```

The crate pins Taffy 0.11.0 and commits Cargo.lock. Its
[measure API](https://docs.rs/taffy/0.11.0/taffy/struct.TaffyTree.html#method.compute_layout_with_measure)
accepts known dimensions and available content space. Only Flexbox/tree/std
features are enabled. No UI serialization framework is used.

## Ownership and validation

The parser borrows strings during parsing and copies retained text into `Arc<str>`
while constructing the candidate model. No input slices or pointers survive flush.
Every field read is bounded; fixed-array conversions occur only after exact-sized
reads. Header/version/count/length, opcodes, kinds, UTF-8 and finite numeric fields
are checked before semantic validation. Node references, ordered hierarchy, depth,
reachability, cycles, unique parents and deletion ordering are validated before
committing state. IDs must increase across batches, even after deletion.

`Engine::flush` returns a Rust Result and leaves its committed model, layout tree
and ID mapping unchanged if validation or measurement fails. The global C ABI
cannot report an error with its void return type: it logs and aborts. Unwinding is
caught in debug builds, and release uses panic=abort. Native memory validity is a
caller precondition; checking pointer provenance from an arbitrary address is
impossible. The public C header documents the boundary.

The global FFI engine binds to its first calling thread and rejects other threads
and reentrant access. On iOS, the runner calls `platform::ios::initialize(host, mtm)` before any C#
flush. The iOS entry path checks `MainThreadMarker::new()` and uses the actual
OS main thread. Other platforms currently enforce thread affinity only.
There is one process-lifetime FFI session; no reset API is exposed yet.
Independent Rust-owned Engine values can be used in unit tests.

## Layout and native measurement

Root width/height match the supplied viewport. VStack is a flex column; children
stretch horizontally, use intrinsic vertical size and do not shrink. Overflow is
allowed (scrolling requires a later scroll/list node). Taffy owns padding and
frames; frames are relative to the parent. Pixel rounding is disabled so the
platform can apply the correct display scale. Background/radius/foreground are
retained for the later presentation adapter.

`Measure` receives the owned Node plus Taffy's known dimensions/available content
space. Return a finite, nonnegative content size excluding protocol padding. The
iOS adapter wraps Text using UILabel and renders Button titles on one line.
Font sizes are explicit points; Dynamic Type and external font-metric change
notifications are not implemented. Measurement must not mutate visible UI before commit. A changed text
or style dirties the Taffy node; viewport and hierarchy changes also invalidate
layout through Taffy's APIs. Calculated frames are checked for numeric overflow.

`RequireNativeMeasure`, used by the non-iOS default FFI instance, explicitly errors when
intrinsic leaf measurement is requested. It does not generate dummy typography.
On non-iOS platforms, container-only trees work before platform initialization.
The iOS FFI path requires explicit host initialization for every kind of tree. Rust tests inject known
fixture sizes to verify layout; those fixtures are excluded from release builds.

## Performance scope

The engine retains native-facing IDs and Taffy's cached layout between batches.
For transactional validation it currently clones the model, Taffy tree and ID map
per batch, then commits by swapping them. Text payloads are shared in Arc storage.
This is an O(n) memory/validation baseline, not a claimed zero-overhead engine.
Consecutive postorder deletes are validated against one reachable-node set and
unlinked in one pass, avoiding a full tree scan for each deleted node. Adversarial
batches that alternate deletion runs with hierarchy edits can still repeat scans.

Before targeting large active UI trees, replace whole-tree transactional clones
with a validated operation plan/undo journal and benchmark realistic batches.
Native list virtualization in Step 5 should keep only visible cells in layout.
The 16 MiB batch, 100k node and 256-depth limits bound basic resource use; the C ABI
is a trusted in-process boundary, not a parser service for arbitrary remote input.

The iOS objc2 implementation and Simulator checks are described in
[ios.md](ios.md). JNI, event callbacks, camera and lists remain unimplemented.
iOS device artifacts cross-compile; real-device execution, application packaging
and C# NativeAOT static linking still require a runner.
