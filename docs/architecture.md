# DotNative architecture

This is the map of the system. Other files under `docs/` go deeper on one
contract (protocol fields, plugins, theming). Read this first when the tree
feels too large.

---

## Preface: what this is

DotNative is a mobile and desktop UI stack with a split brain:

- **C# describes the UI.** You write components, state, and layout in ordinary
  .NET. `Build` returns an immutable description, not a live native control.
- **Rust owns the live tree.** It parses a compact binary command stream,
  computes Flexbox (and some grid) layout with Taffy, and talks to the real
  OS toolkit: UIKit, AppKit, Android Views, WinUI 3, or GTK.
- **Native controls are real.** A `Button` is a `UIButton` / `NSButton` /
  Android button / WinUI Button. There is no web view, no Skia scene graph as
  the product surface, and no JavaScript VM.

The point of the split is the same idea as React Native, without a JS engine
in the middle. C# is the application language. Rust is the renderer. The
boundary is a versioned binary protocol (`DNTV`), not JSON, not objects
crossing FFI, not a DOM.

A frame of work looks like this:

```
State change
  → C# rebuilds dirty component branches
  → Diff against the last committed snapshot
  → CommandBuffer writes only changed opcodes
  → NativeTransport pins the bytes and calls flush_commands
  → Rust parses, validates, applies a copy-on-write model
  → Taffy layouts (unless the batch cannot change geometry)
  → Platform registry creates/updates/skips native views
  → Next display tick polls native events back into C#
```

Idle ticks do not rebuild the tree. The OS display link only drains the
managed dispatcher and polls events. Rebuild happens when `State<T>` (or
viewport, hot reload, explicit `RequestRebuild`) says so.

If you remember one sentence: **C# proposes, Rust commits, the OS draws.**

---

## 1. Repository layout

| Path | Role |
| --- | --- |
| `src/DotNative/` | Managed framework: DSL, renderer, DI, plugins client |
| `native/core/` | Rust crate: protocol, engine, Taffy, platform adapters, plugin queue |
| `plugins/` | First-party capability packages (FilePicker, DeviceInfo) |
| `packaging/` | NuGet targets, NativeAOT entry, native build glue |
| `templates/DotNative.App/` | `dotnet new dotnative` app skeleton |
| `Samples/` | WidgetSamples, Counter, and platform runners |
| `tests/` | Managed regressions |
| `tools/` | CLI, DevHost, native build, USB tunnel |
| `docs/` | Contracts and this architecture note |

Generated junk stays in `bin/`, `obj/`, Cargo `target/`, Gradle folders.

---

## 2. Process and thread model

There is **one engine session per process**, bound to the OS UI thread that
first calls into Rust. A second thread touching `flush_commands` is fatal.
Re-entrant flush (native code calling back into C# which flushes again) is
also rejected.

Typical release process:

1. The platform runner (`main.m`, `NativeHost.java`, WinUI/GTK host) starts.
2. It creates the native surface and calls Rust `initialize` (iOS/macOS also
   install a view registry and a measurer).
3. It loads the NativeAOT app and calls `dotnative_app_start(width, height)`.
4. Generated `AppEntry` builds `ApplicationSession`, which runs `Program.Main`
   on the UI dispatcher.
5. `builder.Build().Run<App>()` mounts `RenderHost` and flushes the first tree.
6. The OS event loop owns the process after `Run` returns. Returning from
   `Main` does not exit.

Each display tick (`CADisplayLink`, Android `Choreographer`, macOS timer):

- `dotnative_app_tick` drains posted C# work.
- Polls plugin replies.
- Polls native UI events and dispatches them into `RenderHost`.

Native input handlers enqueue events in a Rust thread-local queue. They must
not call C# during a layout commit. C# reads that queue on the tick, then
runs callbacks under `DispatcherSynchronizationContext`, so `await` inside a
click handler resumes on the UI thread unless you `ConfigureAwait(false)`.

Development preview is different: C# can run on the Mac under `dotnet watch`,
while the device/simulator is a native renderer talking over loopback TCP
(`native/core/src/dev_host.rs`, ports 4971–4973). The binary protocol is the
same. `PresentationTarget` tells the app which OS is rendering, which can
differ from `OperatingSystem` on the development host.

---

## 3. Rust core

Crate: `native/core`. Entry: `src/lib.rs`.

### 3.1 Modules

- `protocol.rs` — little-endian reader, opcodes, `Kind`, style/layout records.
- `engine.rs` — committed model, Taffy tree, ID map, `flush`.
- `events.rs` — click/text/value/focus/navigation/hover queue.
- `plugins.rs` — second binary transport for method channels.
- `platform/` — iOS, Android, macOS, Windows, Linux adapters.
- `dev_host.rs` — optional TCP preview (feature `dev-host`).
- `box_geometry.rs`, `gradients.rs`, `image_fit.rs`, font helpers — paint math.

The public C ABI is small on purpose:

| Export | Job |
| --- | --- |
| `flush_commands(buffer, len)` | Parse + commit UI batch |
| `dotnative_poll_event` / `dotnative_poll_click` | Drain input |
| `dotnative_register_channel` / `dotnative_send_plugin` / `dotnative_poll_plugin` | Plugins |
| `dotnative_app_*` | Owned by the **managed** NativeAOT image, not this crate |

`flush_commands` is `unsafe extern "C"`, void, fail-stop. The buffer is
**borrowed until return**. Rust must copy anything it keeps (`Arc<str>`,
image bytes). A parse/tree/measure error logs and `abort()`s at the FFI
boundary because v1 has no status code. Engine unit tests use `Engine::flush`
which returns `Result` and leaves the previous tree intact on failure.

### 3.2 Wire format (the UI channel)

Every flush starts with 16 bytes:

```
"DNTV" | u16 version=1 | u16 header=16 | u32 total_length | u32 command_count
```

Then `count` records: `u8 opcode`, `u32 payload_len`, payload bytes.
Max batch: 16 MiB. Integers and `f32` are little-endian. No padding, no
native struct overlay. The parser never casts the buffer to a Rust struct.

Node IDs are nonzero `u32`, **monotonic for the session, never reused**.
ID 0 means “no root”. C# allocates them in `RenderHost.NextId`.

Kinds (also Android JNI integers via `Kind::wire_id`):

1 VStack, 2 Text, 3 Button, 4 ScrollView, 5 TextField, 6 Switch, 7 Slider,
8 Image, 9 Icon, 10 Navigation, 11 NavigationPage, 12 RadioButton,
13 NavigationHeader.

`HStack` is not a kind. It is a container with `direction = row`.

Opcodes today include create/text/style/children/root/delete/viewport,
layout, control, image, header, scroll policy/axis, flex shrink, extended
and advanced layout, visual, typography, behavior, gradient, z-index,
image fit/tint, background image, box decoration, enabled, text runs,
font variations. Field layouts live in `docs/protocol.md` and
`CommandBuffer.cs`.

The writer’s order: create nodes and set properties, set children, set
root, delete obsolete subtrees in postorder. A node may be temporarily
unattached inside one batch. Rust validates the **whole** batch, then
commits.

### 3.3 Model and layout

`Engine` holds:

- `model`: persistent `im::HashMap<u32, Arc<Node>>` plus root and viewport.
- `tree`: Taffy tree.
- `ids`: map from protocol ID → Taffy node ID.
- `spare`: previous generation, for transactional swap.

`flush`:

1. Parse commands (borrowed).
2. Clone the model (cheap: persistent map + Arc nodes).
3. `apply` commands with copy-on-write. Unchanged nodes keep the same
   `Arc` pointer. That is why
   `unchanged_nodes_share_storage_across_flushes` exists.
4. Validate styles / decode changed images **before** commit.
5. Decide `needs_layout`. Presentation-only batches (switch/slider/radio
   value, most header opacity, visual, gradient, z-index, box decoration,
   enabled, nested scroll policy, background image) **skip Taffy entirely**
   and just replace `self.model`.
6. Otherwise reconcile Taffy: unlink removed children, skip style writes
   when `Arc::ptr_eq` says the node is unchanged, `mark_dirty` when text /
   style / control / image changed, `compute_layout_with_measure` from root,
   check frames are finite, swap into place.

Limits: 100,000 live nodes, depth 256. IDs must increase across batches
even after delete.

Leaf size comes from a `Measure` implementation. On iOS that is a detached
`UILabel` / `UIButton` prototype — never the on-screen control — so a
failed candidate cannot mutate visible UI. Images are decoded in this
phase too. `RequireNativeMeasure` is the stub used when no platform
measurer is installed; container-only tests can still layout.

There is no `SET_FRAME` opcode. Taffy owns geometry. Rotation is
`SET_VIEWPORT`.

### 3.4 Platform registries

After a successful `engine.flush`, the platform `sync`s the committed tree
onto native views.

Shared rules:

- Views are keyed by protocol ID.
- `applied` remembers the last `Arc<Node>` (shared with the engine map,
  not a deep copy).
- `frames` remembers the last logical rectangle. Unchanged geometry does
  not call `setFrame` / JNI `frame` / `SetWindowPos`.
- iOS `NavigationPage` frames stay under UIKit for native push transitions.
- Configure/text/style/control already skip when the applied snapshot
  matches.

iOS/macOS extra: `setHidden`, `setEnabled`, visual, gradient, background
image, and box-decoration layers run only when those properties changed
**or** the frame moved. `setFrame` resets `CATransform3D` to identity, so
a move still re-applies visual. Helper: `platform/apple_box.rs`
`layer_needs_sync`.

Android cannot construct Views in Rust. It JNI-calls Java `NativeHost`
(`create`, `configure`, `measure`, `frame`, …) and passes `kind.wire_id()`
because Rust enum discriminants are not the wire IDs. iOS/macOS match on
`Kind` in-process and allocate UIKit/AppKit objects directly.

Sync is not transactional with UIKit/Android. Engine commit is. If
presentation throws, that is fatal. Objective-C exceptions are not turned
into Rust `Err`.

---

## 4. Talking to .NET

### 4.1 UI commands

C# `CommandBuffer` writes the same bytes the Rust parser reads.
`NativeTransport.Flush` pins the span and calls `flush_commands`. The span
must stay valid for the call; the next `CommandBuffer` mutation invalidates
it. Empty buffers are not sent (`RenderHost.Flush` returns early).

`LibraryImport("core", EntryPoint = "flush_commands")` is the in-process
path. NativeAOT static hosts link this as `__Internal` / a static archive
via packaging targets. There is no `DllImport` runtime search in a proper
app build.

Remote preview wraps the same payload in a TCP envelope. The native side
still calls `Engine::flush` on the UI thread.

### 4.2 Events (native → C#)

Native controls call `events::emit(id, kind, value, text)`.

Kinds: 1 click, 2 text, 3 value, 4 focus, 5 navigation shown, 6 navigation
started, 7 pressed, 8 hover. Pressed/hover coalesce if the last queued
event is the same id/kind/value.

`dotnative_poll_event` copies one event into a caller buffer. C#
`NativeTransport.DispatchPendingEvents` runs on the tick and
`RenderHost.DispatchInput` looks up the node ID in `_clicks` / `_inputs`.

Button closures live **only in C#**, indexed by ID. Rust never sees a
managed delegate.

### 4.3 Failure

A thrown exception during `Flush` faults the `RenderHost` (`_faulted`).
You cannot retry against an uncertain native tree; create a new session.
A Rust validation failure in tests rolls back; at the C ABI it aborts.

---

## 5. How you write an app in .NET

### 5.1 Startup

```csharp
public static class Program
{
    public static void Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.AddSingleton<ICart, Cart>();
        builder.Build().Run<App>();
    }
}

public sealed class App : Component
{
    public override Element Build(BuildContext context) =>
        new Text("Hello");
}
```

`Program.Main` is public, static, parameterless, `void` or `Task`. No
top-level statements, no `async void`. `Run<App>()` mounts the first frame
and returns. The native loop keeps the process alive.

DI is `Microsoft.Extensions.DependencyInjection`. The provider lives for
the native session. `App` can take constructor services;
`ActivatorUtilities` builds it if it is not registered. Register types
explicitly — no assembly scanning — so NativeAOT trim stays honest.

`BuildContext` is **not** the DI container. It is a tree-local lookup for
`Theme` and other `ContextScope<T>` values, valid only during synchronous
`Build`. Capture values in callbacks, not the context object.

See `docs/startup-and-di.md`.

### 5.2 Descriptions, not views

`Component.Build` must return a fresh immutable `Element` tree. It must
not mutate state and must not touch the OS. Side effects belong in
callbacks and services.

Primitives are records: `VStack`, `HStack`, `Text`, `Button`, `ScrollView`,
`TextField`, `Switch`, `Slider`, `Image`, `Icon`, `RadioButton`, plus
navigation nodes. Fluent styling (`Padding`, `BackgroundColor`, …) is
constrained by capability interfaces so you cannot compile
`new Switch(...).FontSize(20)` if that node does not text-style.

`ListView<T>` virtualizes by default: only the viewport, cache and overscan rows
are mounted, with stable keys and automatic height measurement. Compatible native
row subtrees are recycled. See [virtual lists](virtual-lists.md).

Keep **stateful component instances in fields**. `Build` should return
`new VStack(_counter, _content)`, not `new Counter()` every time. Identity
of the `Component` object is how incremental resolve skips work.

```csharp
public sealed class Screen : Component
{
    private readonly Counter _counter = new();

    public override Element Build(BuildContext context) =>
        new VStack(_counter, new Text("static"));
}
```

Keys (`.WithKey("row-1")`) preserve **native** node IDs across sibling
reorder. They do not move C# component state from a discarded instance to
a new one.

### 5.3 State

`State<T>` is UI-thread confined. Read during `Build` registers a
dependency on that **mounted element occurrence**. Write after `Build`
publishes to subscribers.

That used to invalidate the entire `RenderHost`. It now dirties the
component entry (`DirtySelf` / `DirtyBranch`) and schedules one render.
Siblings that did not read the state are not rebuilt.

`FocusNode` is the same pattern for text-field focus. Theme changes go
through `ISystemAppearance` / `Theme.Of(context)`.

Do not mutate ordinary fields on a retained child and expect a redraw.
Either put the data in `State<T>`, or call `RequestRebuild` / `RenderNow`.

### 5.4 Resolve, then diff, then flush

`RenderHost.Render`:

1. `ElementResolver.Resolve` walks from the root.
   - Same `Component` instance, clean branch, equal context → **return the
     old entry** (no `Build`, no child walk).
   - Dirty component → `Build`, then visit the result.
   - Containers match children by instance identity first, then position.
     Keys still matter later for native IDs.
2. `Diff` against `_current` snapshots.
   - `ReferenceEquals(node, old.Node)` → keep the snapshot, do not emit
     commands, do not recurse.
   - Otherwise emit `Create` / `SetText` / `SetStyle` / … only for fields
     that changed. `SetChildren` only if order or length changed.
   - Kind or key mismatch → delete the old native node, allocate a new ID.
3. `Flush` if the buffer is not empty.
4. `Commit` resolver subscriptions. A failed `Build` never commits, so
   the previous tree and subscriptions stay live.

Tests in `ComponentInvalidationTests`: 10,000 static texts plus a counter.
Updating the counter visits fewer than 20 resolver entries and diffs fewer
than 10 nodes.

Hot reload (`[assembly: MetadataUpdateHandler]`) calls `RequestRebuild`,
which forces a full resolve. That is a preview feature (`docs/hot-reload.md`),
not on-device NativeAOT patching.

### 5.5 Navigation and chrome

`NativeNavigationNode` / page / header map to OS navigation. Header options
(title, buttons, large title, opacity) are protocol commands. iOS owns
some chrome frames. Details: `docs/navigation-headers.md`.

---

## 6. NativeAOT

Release apps are **NativeAOT**, not CoreCLR + DLLs, not Mono.

Packaging (`packaging/DotNative.App/buildTransitive/DotNative.App.targets`):

- `NativeLib=Static`, `IlcExportUnmanagedEntrypoints=true`
- `OutputType=Library` — the OS runner is the executable; C# exports C
  entry points (`dotnative_app_start`, `tick`, `resize`, `stop`, …)
- `InvariantGlobalization=true` by default
- RID defines `DOTNATIVE_IOS` / `ANDROID` / `MACOS` / `WINDOWS` / `LINUX`

The generated `AppEntry` (`packaging/DotNative.App/tools/AppEntry.cs`) is
the bridge the runner calls. It constructs `ApplicationSession` with
`NativeTransport` and a queue dispatcher, then invokes your `Program.Main`.

Rust `core` is compiled as a static archive (or linked `.so` on Android)
and pulled in by `DotNative.Native.*` packages. Plugin native sources are
collected transitively at publish and compiled/linked into the same image.

Consequences:

- No runtime `Assembly.Load` plugin model.
- Reflection that the trimmer cannot see will break. Register DI types
  with closed generics. `[DynamicallyAccessedMembers]` is on `Run<T>`.
- Changing C# requires a rebuild. Hot reload is the CoreCLR preview host,
  not the shipped AOT binary.
- Android still uses a Java Activity as the process host; the AOT library
  is `libYourApp.so`. JNI exports the Activity needs must stay rooted.
  That is not a Mono VM. SDK names like `mono.android.jar` are leftover
  naming from the .NET Android workload.

`docs/platform-bindings.md` describes an opt-in proof that a
`net*-ios` / `net*-android` bindings assembly can be compiled into the
same image and called from C#. It is not a general MAUI plugin loader.

---

## 7. Plugins

Plugins are **not** extra node kinds. The renderer still only knows the
kinds in `NodeKind`. A plugin is a C# service plus native code, talking
over a **second** binary channel.

### 7.1 Why a second channel

UI flush is a tree diff: monotonic IDs, no RPC, abort on corruption.
File picking, device info, and similar need request/response, cancellation,
and native-owned handles. Mixing that into `flush_commands` would couple
layout to I/O.

### 7.2 IDs

Each channel identity is `SHA-256("identity/v<major>")` first little-endian
`u32`. C#, Swift, and Kotlin share that number. Rename the C# symbol
freely; changing identity/version is a new wire contract. Registration
rejects collisions. Max 256 channels.

### 7.3 Call shape

Request: `u64 callId`, `u32 channel`, `u32 kind`, payload.

- Kind 1: invoke. Payload is a codec map `{ method, arguments }`.
- Kind 2: cancel.
- Kind 3: session reset (internal).

Reply: `u64 callId`, `u32 status` (0 ok, 1 error map, 2 cancel), value.

C# `MethodChannel.InvokeAsync` encodes with `PluginCodec` (null, bool,
i64, UTF-8 string, bytes, string-keyed maps, lists; 1 MiB; nesting 16).
The UI dispatcher sends. Rust looks up the native handler, **borrows**
bytes only for the callback, and queues a copied reply. C# polls replies
on the tick.

At most 256 in-flight calls. Pressure becomes a `busy` error, not a wait.

Native handlers must copy the buffer if work is asynchronous. They never
hand a native pointer to C# as a managed object. FilePicker returns a
handle integer; dropping/`cancelPick` releases the OS resource even if
success races cancel.

### 7.4 Authoring

A plugin package typically contains:

- `net9.0` (or the app TFM) C# API, NativeAOT-safe
- `native/ios/*.m` or RID archives, Kotlin/Swift as the build allows
- `buildTransitive/*.targets` that add `DotNativeNativeSource` items

The app references `DotNative.App` (or the plugin NuGet). Publish
collects native inputs and links them. There is no copy into the app’s
`ios/` folder and no per-plugin Xcode project.

`IFilePicker` / `IDeviceInfo` are injected with ordinary DI
(`AddFilePicker()`). The implementation holds `IPlatformChannels`.

Visual plugins (a `CameraPreview` node) are a **future core contract**:
stable type ids, measure/update/dispose hooks, generated registration.
They are not the method-channel path. Do not smuggle a camera view
through `Image` bytes.

Full wire rules: `docs/plugin-channels.md`. Package layout:
`plugins/README.md`.

---

## 8. Incremental rendering (the performance contract)

Three layers, all required:

1. **C# dirty components** — skip `Build` and skip `Diff` on clean
   retained instances. This is the only thing that stops an O(tree)
   managed walk.
2. **Thin wire** — only changed opcodes. Unchanged properties do not
   cross FFI. Identical `RenderNow` with no state change flushes nothing.
3. **Rust/native skip** — presentation-only batches skip Taffy.
   Unchanged frames skip `setFrame`. Unchanged Apple visual/hidden/enabled
   skip layer writes.

What is still O(n) on a geometry commit: Taffy from the root (clean
subtrees are cached inside Taffy), a scan of node IDs for hierarchy
changes, platform hierarchy attach. Virtualized lists are not implemented.

Keep instances in fields or the first layer does not fire.

---

## 9. Development loop vs release

| | Release | `dotnative run … --hot-reload` |
| --- | --- | --- |
| C# runtime | NativeAOT | CoreCLR + `dotnet watch` |
| Where C# runs | On device | Usually on the Mac |
| Renderer | In-process Rust | Device/simulator Rust + TCP |
| Code edits | Rebuild | Supported metadata updates rebuild UI |
| Plugins | Linked native | Preview host may not transport every plugin |

Preview ports bind loopback. Android uses `adb reverse`. Stop one CLI
session before starting another for the same platform.

---

## 10. How to find code when you are lost

**“Where does a Button become a UIButton?”**
`RenderHost` diffs → `CommandBuffer.Create` → `flush_commands` →
`engine.apply(Create)` → `platform/ios.rs` `create_view`.

**“Why didn’t my State update the screen?”**
Did `Build` read `state.Value`? Is the component instance stable? Did you
write from a background thread? Did `Build` throw (previous tree stays)?

**“Why is native work happening for a slider drag?”**
Control value is presentation-only in Rust (no Taffy). The platform still
updates that one control if `applied.control` changed. It should not
`setFrame` the whole tree.

**“Why did a node get a new ID?”**
Kind changed, or key changed, or an unkeyed sibling was inserted and
positional match failed. IDs never go back to a deleted node.

**“Can I call UIKit from C#?”**
Not through this renderer. Use a plugin channel, or the opt-in platform
bindings proof. Do not reach into `RenderHost`’s native views.

**“Is this React Native?”**
Same *shape* (describe UI in a high-level language, native views, Yoga-
class layout). Different language, different engine, different protocol.
Hermes is a JS VM. This stack has none.

---

## 11. Explicit non-goals (today)

- WebView, CameraView, VideoPlayer as core nodes
- Native list virtualization / cell recycling
- Recoverable `flush_commands` errors (void ABI)
- Runtime-downloaded plugins
- Sharing one engine session across threads
- Using `BuildContext` after `Build` returns
- Mutating a retained component’s fields as a notification mechanism

---

## Related docs

- [Protocol v1](protocol.md)
- [Incremental rendering](incremental-rendering.md)
- [Startup and DI](startup-and-di.md)
- [Plugin channels](plugin-channels.md)
- [Plugin permissions](plugin-permissions.md)
- [NativeAOT platform bindings proof](platform-bindings.md)
- [Hot reload preview](hot-reload.md)
- [Theming](theming.md)
- [Styling capabilities](styling-capabilities.md)
- [Navigation headers](navigation-headers.md)
- [Nested scrolling](nested-scrolling.md)
