# Production milestones

The current release priorities are core correctness and a reproducible MAUI
comparison. OTA hosting, a web dashboard and new plugins are deferred until those
gates pass. The workstreams below describe the broader roadmap. They are not a claim that the
current framework is production-ready. Changes should remain small, tested
commits. The initial repository import groups the existing prototype by module;
it does not reconstruct the historical development sequence. Some import commits
need subsequent modules before the complete tree builds.

## 1. Lifecycle and state restoration

Deliver UI-thread lifecycle notifications and preserve in-memory state while its
application session remains alive. Local and DI-shared state use the same
`State<T>` API; no MVVM adapter is planned.

Disk persistence is out of scope for the core and this milestone. It will be a
separate plugin. OS restoration is also a separate contract, not implicit disk
serialization of every store.

Acceptance checks:

- Background/foreground signals are ordered and delivered on the UI thread.
- Shared DI state invalidates its readers and removes obsolete subscriptions.
- Pending startup is canceled and late work cannot mount into a dead session.
- Native and managed objects from the previous session are released.

## 2. Navigation and forms

Introduce stable route entries, native push/pop and modal presentation, a DI scope
per screen, system Back integration and validated deep-link routing. Screens need
explicit active/inactive/disposed lifetimes; render rebuilds must not recreate
screens or their scopes. Restoration must include the route stack.

The first mobile editor slice is implemented: password mode, keyboard/action and
autofill hints, capitalization, autocorrection, multiline input and submitted
events. See [mobile text input](text-input.md) for scope and verification.

Complete TextField behavior before broadening the widget catalog: secure input,
keyboard type/action, selection, copy/paste, composition, autofill, focus traversal
and keyboard avoidance. Platform affordances must be tested on each platform.

Acceptance checks:

- Push/pop and modal dismissal preserve the expected state and dispose the right
  DI scope, including asynchronous disposal.
- Interactive Back cancellation does not prematurely dispose a screen.
- External links route consistently during cold and warm startup.
- IME marked text survives unrelated renders; edits preserve caret/selection.
- The focused field remains visible above the software keyboard.

## 3. Virtualization and performance

Establish a reproducible baseline before replacing transactional clones or root
rebuilds. Measure managed reconciliation, Rust parsing/layout, native presentation
and end-to-end frames separately. Host-only results cannot establish device UI
performance.

Implemented: [ListView virtualizes by default](virtual-lists.md) with a bounded native
window on iOS/Android/macOS/Windows/Linux, stable keys, anchor correction, automatic measured
heights and explicit fixed/variable extents. Mobile adapters reuse UICollectionView
and RecyclerView cell containers; macOS and Linux adapters pool NSView and GtkFixed
row containers. Compatible layout/text/button subtrees are recycled within each
list, while departed managed subscriptions are released. Stateful form-row reuse,
full native item-source ownership and device performance
baselines remain future work. Height updates and offset lookup use an incremental O(log n)
index; equal keyed rows retain their mounted components
across dataset snapshots.

Acceptance checks:

- A 10,000-item data set realizes a bounded viewport and overscan window.
- Recycling never sends an event to a previous item and releases subscriptions.
- Insert/remove/reorder preserves visible item identity and scroll anchoring.
- Reports record device, OS, build mode, commit, sample size, cold/warm runs,
  startup, memory, allocations and frame-time percentiles.
- Optimizations retain malformed-buffer rollback and native ownership guarantees.

## Continuous verification

Core CI runs C# tests, Rust tests, formatting and template drift checks. It does
not replace device tests, native runner smoke tests or store distribution tests.
The first two milestones must be exercised by an actual stateful multi-screen
sample generated from the template, not only by isolated unit tests.

## Local validation

Run `bash scripts/check-core.sh` locally for formatting, managed/CLI tests, Rust tests and template/sample parity. Native UI and performance journeys run locally on the matching OS/device using `tests/native` and `benchmarks`; retain their raw logs. GitHub validation workflows are manual (`workflow_dispatch`) to avoid spending hosted-runner minutes on every push and PR. Do not dispatch them without an explicit request.
