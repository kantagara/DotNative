# DotNative command protocol v1

This specifies the shared contract implemented by the C# writer and Rust engine
in `native/core`. iOS presentation and intrinsic OS measurement are implemented in `platform/ios.rs`;
Android and macOS presentation are implemented in their corresponding platform modules. All integers and IEEE-754 binary32 floats are
little-endian. Fields are packed with no padding. Dimensions are logical points
on iOS and density-independent units on Android, never physical pixels.

Each flush contains a 16-byte header: ASCII `DNTV`, u16 version (1), u16 header
size (16), u32 total byte length including header, u32 command count. Each command
is u8 opcode, u32 payload byte length, then exactly that many payload bytes.
Maximum batch size is 16 MiB. IDs are nonzero u32, monotonically allocated for
the lifetime of an engine session and never reused. ID 0 represents no root.

Payloads, in order:

- `1 CREATE`: u32 node ID, u8 kind (1 VStack, 2 Text, 3 Button).
- `2 SET_TEXT`: u32 node ID, u32 UTF-8 byte count, UTF-8 bytes (no terminator).
  Empty strings and embedded NUL are valid; native conversion must use lengths.
- `3 SET_STYLE`: u32 node ID, f32 uniform padding, u8 R/G/B/A background,
  f32 corner radius, u8 R/G/B/A foreground, f32 font size. Exactly 24 bytes.
  Values replace the entire style, including defaults, so removed styles reset.
- `4 SET_CHILDREN`: u32 parent ID, u32 count, count × u32 ordered child IDs.
  Replaces the complete child list. Container kinds accept children.
- `5 SET_ROOT`: u32 node ID; zero detaches the root.
- `6 DELETE`: u32 node ID. Deletes a single detached leaf; the writer emits
  detached obsolete subtrees in postorder. Deleting a leaf also unlinks it from
  its obsolete parent. Must release native controls, layout state and events.
- `7 SET_VIEWPORT`: f32 width, f32 height. Root content bounds after host safe-area
  handling. Both must be finite and nonnegative.

All style distances must be finite/nonnegative; font size must be finite/positive.
Colors are straight-alpha sRGB bytes. VStack means flex-column; Text and Button
are measured native leaves (iOS Text wraps, Button titles use a single line). The engine must measure text and buttons with their
font/padding constraints, including multiline text. Padding applies uniformly
to containers and leaves; map it exactly once. Corner radius clips the native view.
Text/Button labels have foreground color and font size in logical units.

The writer creates nodes and sets properties before installing their ordered
children, attaches the root, then deletes obsolete subtrees. IDs may be temporarily
unattached within a batch. The engine computes layout and updates presentation
once after the complete batch. `SET_FRAME` is deliberately absent: Taffy owns all
frames, including on rotation via `SET_VIEWPORT`.

## FFI lifetime, ownership and failure

`void flush_commands(const uint8_t *buffer, size_t len)` runs synchronously on the
OS main thread. The bytes are borrowed, pinned managed memory, valid only until
the call returns. The engine must not retain pointers/slices into this memory or
queue deferred reads. Text and retained properties require engine-owned storage.
The handoff avoids copying the full buffer, but serialization, UTF-8 encoding,
diffing, allocations, native updates and FFI transitions still have costs.

The parser must validate the entire batch before mutation: header, version,
lengths, counts, unknown opcodes, UTF-8, numeric values, unique IDs, valid kinds,
references, parent uniqueness, cycles, legal removals and final reachability.
Use checked arithmetic and bounded reads; never cast an unaligned pointer into
a Rust struct. Unknown opcodes/versions are errors rather than silently skipped.

The requested void ABI cannot report recoverable rejection. For v1, returning
means successful commit. A native error must be fail-stop (log and terminate),
never silently reject and return; no panic may unwind across C. Before supporting
recoverable native errors, revise both sides together to return a status and
define atomic commit/rollback semantics. A thrown managed transport exception
faults the render host; it must not retry against an uncertain native tree.

The Rust validator also limits live nodes to 100,000 and tree depth to 256.
The FFI instance is process-lifetime in this checkpoint; dispose detaches/releases
its tree but does not reset the ID high-water mark. A new host requires a new
process until a session reset API is added with runner lifecycle support.

There is one render host per engine session. The runner owns session startup and
shutdown. A new host requires a reset/new engine session; v1 has no context handle.
NativeTransport does not load or initialize an engine. Static linking and runner
lifecycle belong to the later platform/build stages.

## Events

Button actions stay in C#, indexed by native node ID. The later native event
adapter must deliver a click ID on the main thread, outside an active flush, to
the render host's `DispatchClick`. It must catch all managed exceptions at any
unmanaged entry boundary. No delegate pointer or managed object reference is
serialized. Removed IDs are ignored and never reused. The reverse event adapter
is intentionally not implemented in Steps 1–2.

## Layout and scroll extensions

- `8 SET_LAYOUT`: u32 ID, f32 width, height, gap, grow; u8 direction,
  main alignment, cross alignment, reserved zero. Exactly 24 bytes. Width and
  height use -1 for automatic sizing; other distances are finite/nonnegative.
  Direction: 0 column, 1 row. This command preserves the separate shrink factor.
- `14 SET_FLEX_SHRINK`: u32 ID, f32 finite/nonnegative shrink factor. Exactly 8
  bytes. Default 0 preserves intrinsic/fixed sizes. Positive factors permit
  shrinking below the automatic minimum content size. The C# SetLayout writer
  emits this after opcode 8, including zero when a previous shrink is reset.
- `15 SET_SCROLL_AXIS`: u32 scroll node ID, u8 axis (0 vertical, 1 horizontal).
  Exactly 5 bytes. Only ScrollView accepts it; its default is vertical. Axis is
  independent of the child container's layout direction and of opcode 12's
  Android nested scroll policy.

The new writer and native runtime must ship together. Existing v1 payloads remain
readable by the new engine; older engines reject these new opcodes.

Navigation hosts use relative positioning. A host may be the root or the sole
child of a root DesktopNavigation shell; its pages use the shell content viewport. NavigationPage children
are absolute and overlap in that coordinate system during transitions.

## Transaction storage

The candidate model uses a persistent map with copy-on-write node payloads.
Unchanged nodes and map branches are shared. Two Taffy trees and ID maps alternate
between committed state and staging; each staging tree is reconciled against its
own model generation, not the latest active model. This retains two layout caches
instead of cloning the full tree and ID map on every successful flush.

A failed candidate is discarded without changing the active state. The next
layout attempt seeds a new staging tree from the active one. Validation and
reconciliation still scan nodes; this is not a constant-time flush or list
virtualization. Native measurement remains outside presentation mutations.

## Extended styling records

All offsets below are payload offsets (including the u32 node id). Existing v1
records retain their original sizes. New managed/native packages must ship
together: old runtimes reject new opcodes; there is no capability negotiation.
All record lengths and enum/flag values are checked. Invalid input leaves the
previous committed model and frames unchanged.

- **16 SetExtendedLayout**, 58 bytes: id; four signed finite f32 margins at 4,
  8, 12, 16 (left/top/right/bottom); min width, min height, max width, max height,
  flex basis at 20..36 (f32, -1 means auto); wrap u8 at 40 (0 none, 1 wrap,
  2 reverse); align-self u8 at 41 (0 auto, 1 stretch, 2 start, 3 center, 4 end);
  four padding f32 values at 42, 46, 50, 54. Padding is either all -1 (use the
  uniform SetStyle padding) or all nonnegative. Min/max pairs must be ordered.
- **17 SetVisualStyle**, 53 bytes: id; opacity f32 at 4, border width at 8;
  border RGBA at 12, shadow RGBA at 16; shadow blur/x/y at 20/24/28;
  translation x/y at 32/36; scale x/y at 40/44; rotation degrees at 48;
  clip bool at 52. Opacity is 0..1; widths/blur are nonnegative; other floats
  must be finite. Default opacity and scale are 1, clipping is true. Visual
  changes do not trigger layout but still undergo platform style validation.
- **18 SetTypography**, 26 + N bytes: id; weight u16 at 4 (100..900 by 100);
  italic bool at 6; alignment at 7 (left/center/right/justify/start/end); signed max-lines
  i32 at 8 (-1 default, 0 unlimited, positive limit); overflow u8 at 12
  (default/wrap/clip/start-ellipsis/middle-ellipsis/end-ellipsis); letter spacing
  f32 at 13; line height f32 at 17 (0 default, otherwise positive); decoration
  bits at 21 (1 underline, 2 strikethrough); UTF-8 font-family byte count u32 at
  22 and bytes at 26. Families reject embedded NUL and more than 4096 UTF-8 bytes.
  Typography invalidates native measurement caches.
- **19 SetLayoutBehavior**, 36 bytes: id; width-percent, height-percent and
  aspect ratio f32 at 4/8/12 (-1 unspecified; ratio must otherwise be positive);
  position at 16 (relative/absolute); visibility at 17 (visible/hidden/collapsed);
  inset presence mask at 18 (left/top/right/bottom bits); reserved zero at 19;
  four signed finite inset values at 20/24/28/32. Percent values use 100 for
  the containing block's full dimension. Unset inset values are ignored.

SetLayout preserves the separate extended/behavior records. The managed setter
writes SetLayout, SetFlexShrink, SetExtendedLayout and SetLayoutBehavior together,
including defaults on reset, and checks their combined capacity before writing.

- **20 SetAdvancedLayout**, 56 + 5 × tracks bytes: id; flow/align-content/reverse/grid
  at 4–7; row/column gap f32 at 8/12 (-1 uses uniform spacing); six signed finite
  logical edges at 16–39 (margin start/end, padding start/end, inset start/end);
  presence bits at 40, reserved zero at 41; column/span/row/span u16 at 42/44/46/48;
  column/row track counts u16 at 50/52, reserved zero u16 at 54. Each track is a
  unit byte (auto/points/percent/fraction) plus nonnegative f32. Auto requires zero;
  fractions require positive values. Counts and spans are bounded to 128; a
  placement plus span may not exceed 129. Zero placement means automatic.
- **21 SetGradient**, 22 + 8 × stops bytes: id; kind at 4 (none/linear/radial/conic),
  stop count at 5; start X/Y and end X/Y f32 at 6/10/14/18, normalized 0–1.
  Each stop is an offset f32 followed by RGBA bytes. Active gradients require
  2–64 increasing stops beginning at 0 and ending at 1. Linear endpoints differ;
  radial end X is a positive radius fraction. None requires zero stops.
  Gradients require a container. They are presentation-only and do not invalidate geometry.

- **22 SetZIndex**, 8 bytes: id followed by signed i32 sibling paint priority.
  All signed values are valid. Ties preserve child order. Does not reflow layout.
- **23 SetImageFit**, 5 bytes: id and fit byte (contain/cover/fill/none/scale-down).
  Requires an Image node; defaults to contain. Presentation-only.
- **24 SetImageTint**, 9 bytes: id, enabled byte (0/1), RGBA bytes. Enabled zero
  restores original image colors. Requires an Image node. Presentation-only.
- **25 SetBackgroundImage**, 9 + N bytes: id, fit byte (same as opcode 23), encoded
  byte length u32 at offset 5 and bytes at offset 9. Zero length removes the
  background; otherwise the image decoder must accept it before commit. Maximum
  encoded length is 8 MiB. Requires a container and never contributes to layout.

### Box decoration (opcode 26)

**26 SetBoxDecoration**, exactly 57 bytes: node id u32, presence mask u8,
four f32 corner radii (TL, TR, BR, BL), then four 9-byte side entries (L, T, R, B).
Each side is width f32, color RGBA u8×4, line style u8 (0 solid, 1 dashed, 2 dotted).
Mask bit 0 enables corner overrides, bits 1–4 enable individual side overrides;
other bits are rejected. All radii/widths must be finite and nonnegative, and all
style bytes valid, even for absent overrides. Absent values fall back to uniform
style/visual properties. Mask zero resets all overrides. Changes are transactional
and presentation-only, with no layout/measurement pass.

### Interaction and rich text

**27 SetEnabled**, 5 bytes: id u32, enabled u8 (0/1). Only Button, TextField,
Switch, Slider and RadioButton accept it. Default true; presentation-only.

Native events 7 (PressedChanged) and 8 (HoverChanged) carry boolean 0/1 values;
FocusChanged remains event 4 and, like events 7/8, requires a boolean value (0 or 1).
RadioButton, Switch and Slider now emit focus changes as well as Button/TextField. The managed host resolves state style rules and
sends ordinary style commands. State events for removed nodes are ignored.

**28 SetTextRuns**: id u32, count u32 (0–256), followed by runs. Each run contains
UTF-8 byte count u32 and text bytes, presence mask u8, font size f32, RGBA u8×4,
weight u16, italic u8, decoration u8, UTF-8 family byte count u32 and family bytes.
Mask bits 0–5 override size/color/weight/italic/family/decoration respectively.
Size must be positive finite, weight 100–900 in steps of 100, italic 0/1,
decoration 0–3, family at most 4096 bytes without NUL. Reserved bits are rejected.
All fields are validated even when absent. Only Text accepts runs. Concatenated
run text must equal the node's text at batch end. Count zero clears runs.
Run changes invalidate intrinsic measurement and are transactional.

### Font assets and variations

**29 SetFontVariations**: id u32, axis count u8 (0–16), followed by 8-byte entries:
four ASCII alphanumeric tag bytes and finite f32 coordinate. Duplicate tags are
rejected; order is canonicalized. Count zero resets axes. Textual controls only;
changes invalidate measurement. Unsupported axes use native font behavior;
Win32 rejects nonempty variation settings before commit.

**30 SetFontSource**: id u32, family UTF-8 byte count u32 (0–4096), family bytes,
font byte count u32 (0–8 MiB), font bytes. Empty family plus zero bytes clears the
selection. Nonempty data requires a single SFNT TrueType/OpenType font with valid
table ranges and a matching Unicode name-table family (name ID 1 or 16).
Font identity also requires a Unicode PostScript name (name ID 6) and OS/2 weight/slant
metadata. Distinct faces of one family can be registered; face-data conflicts,
family aliases and duplicate family/weight/slant combinations are rejected.
At batch end the node's typography family must match. Textual controls only.
Registration is validated before measurement/commit and bounded by a 64-face,
32 MiB UI-thread cache. Native registrations can outlive rejected batches.
Any source load invalidates existing nodes' font metrics to replace prior fallback.

**39 RegisterFont**: session font key u32 (nonzero), followed by the same family
and font-byte encoding as opcode 30. Registration is independent of a mounted node.
Keys cannot be reused; the session registry is bounded to 64 entries and 32 MiB.
A rejected batch does not retain its session keys.

**40 SetFontReference**: textual node id u32, previously registered session font
key u32. Selects the cached asset, retaining its exact face and weight/slant metadata
without resending font bytes. Deleting the registering control does not invalidate
references. Use opcode 30 with empty family/data to clear selection.
Managed rendering deduplicates by family and content hash, commits keys only after
successful transport submission, and creates a fresh registry for each RenderHost.
Both commands require the matching native runtime shipped with this framework.

### Host accessibility bridge

`dotnative_app_accessibility(i32 reduced_motion, f32 font_scale, i32 high_contrast)`
accepts boolean flags and a finite 0.5–5 scale. Runners publish before startup and
on changes. Remote preview hello bodies now contain 32 bytes: the existing first
20 bytes, then these three fields. Managed preview also accepts the legacy
20-byte hello with default preferences. Native-to-managed message kind **8** has
the same 12-byte preference body; kind 7 remains plugin replies. Preferences are
applied in managed rendering, so no extra node opcode is required for scaling or
reduced motion. High contrast is exposed reactively to application code.

### Preview frame clock

Native-to-managed kind **9**, empty body, requests one animation frame. Managed
preview calls `AdvanceAnimationFrame` and replies with kind **102**, empty body,
even when startup has not mounted a host yet. Only one pulse can be outstanding;
slow hosts do not accumulate animation requests. Input and plugin messages remain
independent. Windows IPC wakeups drain commands without emitting a new pulse;
only compositor-clock notifications or fallback timer ticks emit pulses, avoiding
an acknowledgement feedback loop.
Legacy native previews without pulses keep the managed fallback timer.

Collection sources are normalized to a standalone selected TTF/OTF by the managed
loader before opcode 30 is emitted. TTC/OTC bytes and face indexes do not cross
the wire; source-level collection limits are 32 MiB/256 faces, while the existing
8 MiB per-face wire/registration limit is unchanged.

### Desktop navigation

Kind **15** is a desktop navigation container with exactly one content child.
**33 SetDesktopNavigation** carries id u32, selected i32 (-1 or an item index),
open u32 (0/1), pane width f32 (180–1000), item count u32 (0–256), theme u32 (0 system, 1 light, 2 dark), title string,
then label/icon string pairs. Each string is a u32 byte length and strict UTF-8.
On AppKit, `sf:` prefixes a system symbol name; other icon strings are text.
Windows interprets icons as Segoe Fluent Icons glyphs. Event kind 3 reports an
item index or -1 for an open-state toggle. Kind 15 is rejected by mobile/GTK
renderers; managed `SideDock` chooses the composed GTK presentation instead.

## Mobile text input options

`SetTextInput` (opcode 34) accepts a nine-byte payload, or thirteen bytes when an explicit mobile placeholder color is supplied. The optional last four bytes are RGBA; omitting them restores the platform default. A truncated optional color is rejected atomically. The first nine bytes are: node ID (`u32`), keyboard
(`u8`, 0–5), action (`u8`, 0–5), capitalization (`u8`, 0–3), autofill (`u8`, 0–5),
and flags (`u8`: password=1, multiline=2, autocorrect=4). Enum values follow
`TextInputOptions.cs`; the default suffix is `00 00 00 00 04`. Only TextField
nodes accept this operation. Unknown values/bits, password+multiline, and
multiline with a non-text keyboard are rejected transactionally.

Changing multiline mode on an already committed node is rejected: the managed
diff creates a new editor ID because UIKit changes native editor class. Other
options update in place and support resetting to defaults. Old native runtimes
reject the unknown opcode; rebuild the native runner with the managed framework.

Native event kind 9 (`TextSubmitted`) carries current UTF-8 text using the existing
event envelope. Disabled, deleted and replaced input nodes cannot invoke stale
submit callbacks. Native keyboard hints and multiline rendering are currently
implemented on iOS and Android; desktop adapters reject non-default options.

## Scroll metrics and measured layout

Opcode **35 SetScrollPosition**: node ID u32, request revision u32, requested
logical offset f32 (finite, nonnegative), observe u8 (0/1). Only ScrollView nodes
accept it. Native adapters apply a request once per revision, clamp it to the
content range, and report event **10 ScrollChanged**: value=offset,
text=`revision:viewportExtent` using invariant decimal formatting. Native scroll
events are coalesced per node while retaining their order relative to other input.
Managed controllers ignore events from superseded requests. Implemented on iOS,
Android, macOS, Windows and Linux.

Opcode **36 ObserveLayout**: node ID u32, observation revision u32 (zero disables).
Event **11 LayoutMeasured**: value=committed height, text=`revision:width`.
Geometry comes from the layout engine using native intrinsic measurements and is
reported only when the observation revision or size changes. It is queued before
presentation scroll events and delivered after the flush returns; no managed
callback can reenter a layout commit. Reports are coalesced per node. ListView
uses revisions to reject measurements from obsolete item snapshots. Observation
changes alone do not run layout. Rebuild native runners with the managed library.

Opcode **37 SetVirtualList**: node ID u32. Marks a newly created vertical
ScrollView with one VStack document as a virtual collection. Changing an already
mounted ordinary ScrollView to this backend is rejected; the managed diff
replaces the node instead. The existing children/frame commands define the
realized row window and extent spacers. UIKit presents these through
UICollectionView cells and Android through RecyclerView holders with stable node
IDs. Their content binds existing native row subtrees; recycling detaches the
old content. Neither adapter calls managed builders during native layout.
Desktop uses the existing native scroll views with reusable row containers
(NSView on macOS, HWND on Windows, GtkFixed on Linux). All three unbind retired
content before reusing a container; at most 32 idle containers are retained per
list. The managed window still owns row realization.
