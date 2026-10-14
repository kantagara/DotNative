# Incremental rendering

State reads belong to a mounted element occurrence. When a component reads
`State<T>` (including state in a DI service), a change invalidates that component
and marks the path to the root for resolution. Multiple changes still schedule
one UI-thread render. Ancestors reuse their previous Build result unless their
own dependencies changed.

Keep reusable components in fields:

```csharp
public sealed class Screen : Component
{
    private readonly Counter _counter = new();
    private readonly ExpensiveContent _content = new();

    public override Element Build(BuildContext context) =>
        new VStack(_counter, _content);
}
```

A clean retained instance reuses its resolved subtree. Diff stops at that same
node reference. Callback and input registries update only for changed snapshots;
callbacks in skipped subtrees remain registered. Creating a new `Counter()` in
Build still creates a new description and executes its Build. Keys preserve
native node identity; they do not transfer component state between new instances.
Component inputs must be immutable or reactive. Mutating ordinary fields on a
retained child does not notify the renderer; explicit `RequestRebuild()` and
`RenderNow()` force a full rebuild, as does the Hot Reload hook.

Context lookups track the requested type and resolved value, including missing
providers. A changed provider rebuilds components that read that value. Inherited
text styling is resolved again when its context changes, even if the enclosing
component did not explicitly read Theme. Context values should be immutable or
expose reactive state. Escaped BuildContext instances remain invalid outside Build.
Context changes conservatively revisit descendant descriptions; equal contexts
and clean branches are skipped. Dirty containers still reconcile their immediate
sibling lists. This is not a guarantee of O(1) updates for arbitrary flat trees.

Candidate resolution does not change live subscriptions. Build/diff failure keeps
the previous subscriptions and native snapshot; transport failure faults the host
as before. Unmounted occurrences unsubscribe, including multiple occurrences of a
shared component instance.

## Native presentation

The iOS, Android, macOS, Linux and Windows registries remember the last applied
logical frame. Unchanged frames do not call UIKit/AppKit setFrame, Android JNI
frame, GTK child transforms, or Win32 SetWindowPos. Removal also removes the
cached frame. UIKit continues to own NavigationPage frames for native
transitions. macOS inner control frames also account for padding changes;
scroll document extents remain independent from the viewport frame.

On iOS and macOS, hidden, enabled, visual, gradient, background-image and box
decoration layers are applied only when those properties change or the frame
moved. setFrame resets CATransform3D, so a geometry change still re-applies
visual. Android, GTK and Win32 already gated equivalent setters on applied
snapshots.

Applied node snapshots share the engine's persistent map and Arc payloads instead
of deep-copying every node after each sync. Native sync still scans active nodes
for hierarchy and paint order; this change removes redundant setters and payload
copies, not every O(n) scan.

## Layout batches

Validated presentation-only batches commit their model without cloning/reconciling
a Taffy tree, calling compute_layout, or scanning frames for finiteness:

- Switch, slider, radio and navigation control values; text-field focus changes.
- Nested scrolling policy changes.
- Header opacity changes that do not alter the page inset policy.

Text-field placeholder changes require layout. NavigationPage opacity crossing
exactly 1.0 also requires layout: the current layout policy changes reserved bar
space at that boundary. Header options remain conservatively layout-affecting,
as do text, style, image, hierarchy, viewport, flex and scroll-axis commands.
Malformed commands still fail before commit. Structural model validation remains
in place even when layout is skipped. Cached layout generations retain their own
baseline models so a later geometry batch can catch up correctly.

## Verification and limits

The managed regression includes a 10,000-text-node clean sibling branch. A counter
update must visit fewer than 20 elements in resolution and fewer than 10 nodes in
diff, without rebuilding its parent or the clean component. It also checks theme
changes, unrelated context, callback refresh, failed Build, unmount and full reload.
Rust tests count layout passes and check geometry after presentation-only commits,
invalid batches, later text/placeholder updates and header opacity boundaries.

These are work-count/correctness tests, not device frame-time benchmarks. Actual
startup, allocations, memory overhead of retained resolution entries, scrolling,
and frame-time percentiles still need measurement on devices. `ListView` is virtualized by
default; see [virtual lists](virtual-lists.md) for its bounded realization window.
