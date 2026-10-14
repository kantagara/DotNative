# Virtual lists

`ListView<T>` is virtualized by default. It mounts only a viewport plus cache and overscan window inside a native
collection on iOS (UICollectionView) and Android (RecyclerView), and a native
scroll view with pooled native row containers on macOS and Linux, and WinUI 3 scrolling on Windows.

Existing `ListView` calls now realize a bounded window instead of every row.
`Height` keeps its previous default of 200. Prefer an explicit height or
`FillAvailableHeight` for new code. Keep the list instance in a field and call
`SetItems` when its data changes; the constructor takes a snapshot.

```csharp
private readonly ListView<Game> games = new(
    initialGames,
    game => new VStack(
        new Text(game.Title).FontSize(20),
        new Text(game.Description)),
    game => game.Id.ToString())
{
    Height = 400,
    Overscan = 4,
};
```

`CacheExtent` is the prepared distance in logical pixels on **each** side of the
viewport. The default (`null`) uses one viewport; `0` retains row-only overscan.
`Overscan` adds extra rows beyond that distance. Increase the cache for fast
scrolling or more expensive rows; memory and initial measurement work grow with
this window, not with the entire item source. The WidgetSamples demo uses 1,200
pixels plus eight rows on each side.

`FillAvailableHeight = true` lets the list grow/shrink into its parent's remaining
height. Give the parent a bounded height; `Height` remains the initial viewport
estimate until native scroll metrics arrive. The default is a fixed `Height`.

The three-argument constructor automatically measures row heights using committed
native layout geometry. Unseen rows initially estimate 56 logical pixels; measured
heights replace those estimates. Text wrapping, row expansion, state updates and
other changes that trigger native layout update heights again. Width changes
invalidate cached measurements. Each row must have finite intrinsic height; do
not use viewport-relative or flex-fill height to size a row in unbounded content.
The minimum accounted row height is one logical pixel.

Keep the list instance in your screen/component. Call `SetItems(snapshot)` on the
UI thread to replace its data. Keys must be unique and nonempty. Unchanged keys
retain native identity while visible; rows outside the window and their reactive
subscriptions are released. Keep durable row state in your data/store, not in a
component that may leave the window. Supply immutable item snapshots so updates
can be distinguished from unchanged data.

Inserts, reorders and height changes preserve the first visible item's key and
its offset where possible. `KeepEndVisible = true` follows appended data only
when already at the end. Height corrections at the end preserve the bottom.
`ScrollToIndex(index)` and `ScrollToEnd()` work with estimates and settle as the
new visible window is measured. The scrollbar's total extent therefore changes
as previously unseen rows are measured.

For known heights, the fourth constructor argument can be a fixed `float` or a
`Func<T, float>`. These modes skip measurement and constrain each row to the
supplied extent. Include row padding/spacing inside that extent.

`Controller.Offset` and `Controller.ViewportExtent` expose logical-pixel scroll
metrics. `Controller.JumpTo(offset)` requests a nonanimated scroll; stale native
notifications cannot undo newer requests. A controller can attach to only one
mounted scroll view. Ordinary `ScrollView` also accepts `Controller` and
`OnScrolled` for custom scrolling behavior.

Mobile cell containers are reused by the native collection adapters. Android
uses stable IDs and DiffUtil updates; iOS dequeues UICollectionViewCells with a
layout driven by committed row geometry. Recycling removes the previous content
before a cell binds another row. Managed components outside the window are disposed. Compatible native row
subtrees are rebound to new logical IDs in the same commit.
Native cells cover the managed viewport/overscan window plus extent spacers; the
managed side continues to choose which data items are realized. This is not yet
full native ownership of the entire item source.

AppKit and GTK use reusable NSView and GtkFixed row containers. A bounded pool
retains at most 32 idle containers per list in addition to the active window.
Windows now uses WinUI 3 ScrollViewer/Canvas for the bounded row window; its old
HWND pool is no longer used. These are not full native item-source adapters.
Measurement uses separate reusable native prototypes. AppKit and GTK retain at
most one prototype per supported text/form control kind; UIKit also separates
single-line and multiline input and reuses its tab-bar prototype. Android keeps
one prototype per measured control kind. Additional prototypes are created on
first use, remain detached from visible content, and are released with their
renderer/activity. Images are not added to these pools. Windows reuses its text,
button and radio measurement controls separately from its visible leaf pool.
This bounds prototype count, not retained text byte size. These changes reduce
control creation; per-platform RAM and frame-time improvements require measurement.

Native scroll metrics and programmatic jumps work through the shared controller.
Height metadata is O(item count). A Fenwick index updates a measured height and
finds the visible item in O(log n), without allocating arrays per measurement.
Replacing the dataset or invalidating measurements after a width change remains
O(n). Pixel scrolling inside the same realization window retains the window description.
Overlapping rows retain their wrapper and subscriptions; departed rows release
their subscriptions and captured models. Unchanged keyed item values reuse their mounted row components; changed
content updates the existing native row and its callbacks.

All five renderers recycle complete compatible row subtrees composed of layout
containers, text and buttons. Matching stays within one surviving list and requires
identical ordered control structure; trees over 256 nodes, trees with surviving
descendants, active focus/press/hover, background images, or stateful controls use
the normal create/delete path. WinUI also excludes decorated wrappers and SVG
borders; it retains the row Canvas and descendants but replaces the outer Border
to avoid WinUI reparenting failures. This is structural reuse of native views,
not retention of the previous managed component or its item model.

Logical node IDs remain unique. Native callbacks are rebound at enqueue time on
UIKit, AppKit, Android and GTK; already queued events keep their old logical ID.
WinUI callbacks read the updated node identity. Layout reports also keep their
logical identity, so old measurements cannot update a different item. Rebinding
reapplies presentation and geometry. Unmatched trees are released, and no idle
row-subtree cache remains after clearing the list. Native container and WinUI leaf
pools retain their existing bounded limits.

Stateful form-row recycling, native item-source ownership and physical-device
performance benchmarks remain separate work. No FPS or MAUI performance claim is implied.

## Verification

Managed tests cover 10,000 rows, bounded windows, stable identity, callback and
subscription cleanup, changing heights, width invalidation, stale measurements,
anchor preservation, empty data and scrolling to the end. Rust tests check
transactional observation updates, real layout changes and event coalescing.

Native interaction tests verify mobile cell-container identity reuse across distant
windows, correct row callbacks, growing/shrinking content, anchors and empty data:

```sh
python3 tests/native/run-mobile-interactions.py android --virtual-list --device emulator-5554
python3 tests/native/run-mobile-interactions.py ios --virtual-list --device <simulator-uuid>
```

Desktop native smoke tests check container identity across distant windows,
keyed reorder, grow/shrink, native scroll events with request revisions, and
empty cleanup. Run AppKit locally:

```sh
cargo run --manifest-path native/core/Cargo.toml --features dev-host --example desktop_virtual_list_smoke
```

Linux and Windows include the same scenario in `dotnative-desktop --smoke`.
Linux additionally supports the 10,000-row system-input fixture:

```sh
bash tests/native/run-linux-interactions.sh <native-runner> <managed-fixture-dir> <output-dir> --virtual-list
```

The WidgetSamples home screen includes a 10,000-row demo with expandable content.

## Implementation references

The behavior is checked against published framework contracts:

- [MAUI item sizing](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/layout?view=net-maui-10.0)
  separates individual row measurement from the optimization for uniform sizes.
- [Android DiffUtil](https://developer.android.com/reference/androidx/recyclerview/widget/DiffUtil.Callback)
  distinguishes item identity from content equality. DotNative uses stable keys
  for identity and `EqualityComparer<T>.Default` for immutable item content.
- [RecyclerView](https://developer.android.com/develop/ui/views/layout/recyclerview)
  reuses views through its adapter and view holders. MAUI's
  [iOS controller](https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/Handlers/Items/iOS/ItemsViewController.cs)
  dequeues collection cells. DotNative now uses those native cell containers;
  reuse of complete row templates and native item-source ownership remain distinct
  follow-up work.

- [GTK list factories](https://gnome.pages.gitlab.gnome.org/gtk/gtk4/class.ListItemFactory.html)
  separate container creation from binding and unbinding item content. Desktop
  pools follow this lifecycle while retaining DotNative's existing layout engine.

These references guide observable behavior; DotNative uses its own height index.
