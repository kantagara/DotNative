# Styling capabilities

> Windows now uses the full WinUI 3 renderer. Win32-specific implementation and
> pixel-test descriptions below document the previous backend and do not establish
> WinUI parity. See [current Windows coverage](desktop-preview.md#limits) for
> implemented styles and explicit rejections during migration.


Styles are immutable C# descriptions. RenderHost diffs them and sends explicit
binary records to Rust; Taffy owns geometry and native controls own presentation.
`Node` itself grants no styling capability. Concrete node types retain their type
through fluent calls, and setters are internal to prevent bypassing capabilities.

## Implemented catalog

- `ILayoutable`: Width, Height, AutoWidth, AutoHeight, WidthPercent, HeightPercent,
  MinWidth, MinHeight, MaxWidth, MaxHeight, AspectRatio, Margin, Grow, Shrink,
  FlexBasis, AlignSelf, Position, Insets, Left, Top, Right, Bottom, Visibility, ZIndex.
- Containers: Spacing, MainAlignment, CrossAlignment, Wrap, RowGap, ColumnGap,
  AlignContent, Reverse. Grid adds immutable point/percent/fraction/auto tracks;
  children use GridColumn/GridRow with explicit or automatic placement and spans.
- `IPaddable`: uniform, horizontal/vertical and four-edge Padding.
- `IVisualBox`: BackgroundColor, CornerRadius, Opacity, Border, Shadow,
  ClipToBounds, Translate, Scale, Rotate.
- `ITextual`: FontSize, FontFamily, FontWeight, Bold, Italic, TextAlignment,
  TextColor (also ForegroundColor).
- `ITextFlow` (Text): MaxLines, TextOverflow, LetterSpacing, LineHeight,
  TextDecoration, Underline, Strikethrough.
- `ITintable`: TintColor on icons. Image also has nullable TintColor and Fit
  (Contain, Cover, Fill, None, ScaleDown).
- Layout direction: inherited FlowDirection, MarginInline, PaddingInline,
  InsetInline; TextAlignment.Start/End resolve against the inherited direction.
- Container backgrounds: BackgroundGradient with linear, radial and conic gradients;
  BackgroundImage with the same fitting modes as Image.

Text, Button and TextField have layout, box, padding and typography capabilities.
Container nodes have layout, box and padding. Switch and Slider expose layout;
wrap them in a styled Container for decoration. Image has layout, box, fitting and tint; Icon
also has tint. RadioButton retains its existing capabilities. Native text inputs
remain single-line; multiline editing is a control feature, not implied by Text's
line-flow API.

## Semantics and resetting

Distances are logical pixels. Edge order is **left, top, right, bottom**. Two-value
spacing overloads mean **horizontal, vertical**. Margin and position offsets may
be negative; padding, dimensions and border widths may not. NaN/infinity and
invalid enum values are rejected before serialization and by the native parser.

```csharp
new Container(
    new Text("A styled native card")
        .FontFamily("Arial")
        .FontWeight(FontWeight.SemiBold)
        .FontSize(20)
        .LetterSpacing(0.4f)
        .LineHeight(28)
        .MaxLines(2)
        .TextOverflow(TextOverflow.EllipsisEnd)
)
    .WidthPercent(100)
    .MaxWidth(420)
    .Padding(horizontal: 20, vertical: 14)
    .Margin(0, 8, 0, 12)
    .BackgroundColor(Color.White)
    .CornerRadius(12)
    .Border(1, new Color(220, 224, 230))
    .Shadow(new Color(0, 0, 0, 50), blur: 8, offsetY: 3)
    .ClipToBounds(false);
```

Percent dimensions use 100 for the parent's full size. Fixed Width/Height clear
corresponding percentages. AutoWidth/AutoHeight clear both. Null removes a
min/max dimension, aspect ratio, flex basis or position edge. Min must not exceed
max; remove an old conflicting constraint before setting a new one. The root
fills the viewport; navigation retains its own page-sizing contract.

`Position(Absolute)` removes the node from normal flex flow. Hidden preserves
layout space and hides native content; Collapsed removes layout space as well.
Returning to Visible restores both. A fresh unstyled description resets all
previously applied styles while preserving the node's native identity.

Borders draw inside the existing box; they do not consume extra layout space.
Transforms apply visually around the center in scale, rotate, translate order;
they do not reflow siblings. Rotation is clockwise degrees. ClipToBounds defaults
to true; turn it off on the shadowed node and any relevant ancestors to allow an
outer shadow. Native scroll viewports still clip scrolling content. FontFamily
uses installed native family names and falls back to the system font. Null resets
the family. Font names do not bundle font files. TextAlignment uses physical
Left/Center/Right and Justify. MaxLines=0 is unlimited; null restores the control's
default. Text decoration flags combine underline and strikethrough.

## Grid, direction and gradients

```csharp
new Grid(
    new GridDefinition([GridTrack.Points(120), GridTrack.Fraction()]),
    new Text("Label"),
    new Text("Flexible column"),
    new Text("Full row").GridColumn(1, span: 2)
).RowGap(8).ColumnGap(12);

new Container(new Text("Gradient"))
    .Padding(16)
    .BackgroundGradient(Gradient.Linear(
        new(0, 0), new(1, 1),
        new(0, Color.Red), new(1, Color.Black)));
```

Grid lines are one-based; zero chooses automatic placement. Explicit tracks and
placements are bounded to 128. Percent tracks use 100 for full width/height.
Auto tracks use content sizing; fractions share remaining space. Grid's default
row distribution stretches auto tracks; AlignContent(Start) keeps intrinsic rows.
RowGap/ColumnGap override Spacing independently; null restores Spacing. Reverse
reverses a flex axis. Logical edges override physical edges regardless of call
order; null restores the physical edge. Direction inherits from the parent and
defaults to left-to-right at the root. Direction changes relayout descendants.
Native text shaping remains platform-owned; this API controls layout direction
and start/end alignment, not forced Unicode bidi embedding.

Gradient points and stops use normalized 0–1 coordinates. There must be 2–64
strictly increasing stops including both endpoints. A radial radius is a fraction
of the smaller box dimension. Conic gradients start at twelve o'clock and run
clockwise. Stops/tracks are copied, so later mutations cannot change a description.
BackgroundGradient(null) removes the gradient. The fluent API is restricted to
containers: wrap text, images or controls to give them a gradient background. A gradient draws over the solid
background and under content, clipped to the box's corner radius. Gradients use
CALayer, Android Shader and GTK snapshot primitives; Win32 caches a GDI+ raster
with a maximum dimension of 2048 pixels. Native interpolation may differ slightly.

## Images and sibling stacking

```csharp
new Image(source).Width(160).Height(100)
    .Fit(ImageFit.Cover).TintColor(new Color(20, 100, 220, 180));

new Container(new Text("Photo card").Padding(16))
    .Height(180).BackgroundImage(source).CornerRadius(12);

new VStack(
    new Text("Front").Position(PositionMode.Absolute).ZIndex(2),
    new Text("Back").Position(PositionMode.Absolute).ZIndex(1)
);
```

Image fitting is centered. Contain preserves the whole image, Cover fills the
box while cropping overflow, Fill stretches each axis independently, None uses
the platform's intrinsic logical size, and ScaleDown is Contain without upscaling.
The default is Contain; fitting does not change intrinsic layout measurement.
Image.TintColor uses the original image alpha as a mask: output alpha is source
alpha multiplied by tint alpha. Null restores original RGB and alpha.

BackgroundImage defaults to Cover, is always clipped to the rounded box, and
paints above the solid color/gradient but below children. Null removes it. It
creates no layout children, consumes no input and does not change intrinsic size.
Invalid encoded assets are rejected before the batch commits, including for
fixed-size containers. Images use the existing 8 MiB encoded-asset limit.

ZIndex orders siblings from low to high. Equal values preserve child order;
negative values are supported. It changes native stacking and hit-testing order,
not flex/grid layout. Keyboard and accessibility traversal follow the native
toolkit and may reflect native child order. ZIndex does not escape a parent's
clipping or stacking boundary. Native navigation pages retain their stack
order and reject a nonzero page ZIndex. Root ZIndex has no sibling to order against.
ZIndex, image fitting, tint and backgrounds do not trigger a layout pass. Painting
interpolation, intrinsic image density and color management remain platform-specific.

## Corners and borders

`CornerRadius(topLeft, topRight, bottomRight, bottomLeft)` sets physical corners
in clockwise order. `BorderLeft`, `BorderTop`, `BorderRight`, and `BorderBottom`
accept a `BorderSide(width, color, BorderLineStyle)`; Solid, Dashed and Dotted are
implemented in UIKit, AppKit, Android, GTK and Win32. The three-argument `Border`
overload sets all four sides. Colors include alpha.

Borders paint inside the box without changing layout. Oversized corner radii
shrink proportionally to fit the box; opposing border widths similarly shrink
when their sum exceeds the corresponding dimension. Side wedges divide corner
joins. Dashed/dotted sides use the rounded center path, clipped to their own
wedge; dash phase is not continuous across sides with different widths/styles.

`BorderSide.None` explicitly removes a side. Passing null to a side restores the
uniform border; `CornerRadii(null)` restores the uniform corner radius. Calling
uniform `Border(width, color)` clears all edge overrides, and uniform
`CornerRadius(radius)` clears all corner overrides. Reactive removal restores
defaults without recreating controls. GTK smoke checks rendered corner pixels,
hollow borders, and gaps in dashed/dotted lines; other backends are compile-checked
and still require device visual verification.

## Reactive animations

`AnimationController` is a UI-thread reactive float source. Read Value in Build
and map it to an existing style, for example Translate, Opacity, Width or rotation.
Await AnimateToAsync from an event handler; a new target starts at the last
presented value and cancels the previous task. Stop and Dispose cancel pending
work. SnapTo sets the value immediately. Zero duration supports an application's
reduced-motion preference. Dispose controllers with their owning DI scope or
screen; constructing one during Build would lose ownership on every rebuild.

```csharp
// Create once on the UI thread and dispose with the owner.
using var motion = new AnimationController(dispatcher);
// Inside a retained component's Build:
// return new Container(content).Translate(motion.Value, 0);
await motion.AnimateToAsync(80, TimeSpan.FromMilliseconds(250), Easings.EaseInOut);
```

The controller uses monotonic elapsed time, coalesces queued frames and updates
through IUiDispatcher. Linear, EaseInOut and EaseOutCubic easings are included;
custom easing must return finite values. Superseded/canceled tasks must be
observed, just like other cancellable async operations. Timing uses a 16 ms timer,
not a native display-link/vsync scheduler. This is explicit reactive animation,
not automatic transitions between two arbitrary style descriptions; use the declarative API below for that.

## Declarative transitions and interaction states

`Transition(new StyleTransition(duration, properties, easing))` animates subsequent
changes to opacity, transforms, foreground/background colors, borders, corners and
shadows. Mounts render immediately. Layout, font metrics, gradients and images
switch immediately. Choose properties using `TransitionProperties` flags. Linear,
EaseInOut and EaseOutCubic are supported; duration zero or `Transition(null)` snaps
to the target. Retargeting starts from the last presented frame. Removing a node
or disposing its RenderHost cancels its work. `RenderHost.ReducedMotion=true`
finishes all active transitions and prevents new ones until reset.

Packaged runners advance transitions in their native frame callback (CADisplayLink
on iOS and macOS 14+, Choreographer on Android, GTK frame clock, and Windows 11
DirectComposition clock when available; older systems use UI timers). Preview forwards bounded frame pulses to the managed host.
Alternate hosts can call `AdvanceAnimationFrame` from their own display callback;
otherwise a coalesced 16 ms timer dispatches to the UI thread. Frames update native
presentation directly without rebuilding components. Backend restrictions still
apply to animated properties.

```csharp
new Button("Save", Save)
    .BackgroundColor(Color.Blue)
    .OnState(InteractionState.Hovered, new(Opacity: 0.85f))
    .OnState(InteractionState.Pressed, new(Opacity: 0.6f, TranslationY: 2))
    .OnState(InteractionState.Disabled, new(Opacity: 0.35f))
    .Enabled(canSave)
    .Transition(new StyleTransition(TimeSpan.FromMilliseconds(150)));
```

Button and RadioButton support Hovered (pointer devices), Focused, Pressed and
Disabled; RadioButton also supports Selected. TextField and Slider support Hovered,
Focused and Disabled; Switch additionally supports Selected. Pressed is still
rejected on TextField, Switch and Slider: native drag/cancellation semantics for
those controls are not yet exposed by this contract.
Focus follows the native responder/focus owner and its keyboard-navigation policy;
styling does not force touch controls to acquire focus. GTK includes the internal
text editor in TextField focus tracking. Switch Selected follows its controlled boolean value.
Foreground overrides require a textual control; Switch/Slider state rules style
their enclosing visual box. Invalid
state/control combinations are rejected. A combined state mask requires every bit.
Matching rules apply in declaration order and only override supplied properties.
Returning to the base state restores base properties. `ClearStateStyles` removes
rules. `Enabled(false)` disables Button, TextField, RadioButton, Switch or Slider
in the native toolkit and also blocks managed action dispatch. It does not disable
an entire ancestor subtree. Native pointer/focus behavior remains toolkit-owned.
State styles are resolved after theme inheritance and can use transitions.

Managed tests cover hover/focus/press resets, Selected combinations, disabling
while active, late interaction events, re-enabling and clearing rules without
replacing controls.
AppKit and GTK smoke tests exercise real focus ownership on RadioButton, Switch
and Slider. Hover controller callbacks and radio highlight/gesture resets
are exercised through the native AppKit/GTK objects. GTK radio press tracking
observes click/key controllers without consuming native input; release, gesture
cancellation, disabling, focus loss and unmapping clear the pressed state. AppKit also checks that
tracking-area replacement does not accumulate areas and repeated native radio
clicks preserve single-choice behavior. These callback checks do not replace
system pointer/keyboard tests on every OS. GTK/X11 now has a separate XTest input
runner with rendered-pixel assertions, including hover recovery after disabling
and re-enabling a held radio. The Windows smoke has now run in a Windows 11 VM. Separate
[system-input tests](runtime-interaction-tests.md) exercise the complete native → C# →
presentation path on Windows, GTK/X11, Android and iOS virtual devices; the coverage table lists which gestures and rendered states were
actually checked. Compilation alone is not a runtime test.
Run the AppKit check from the repository root:

```sh
cargo build --manifest-path native/core/Cargo.toml
clang -fobjc-arc -framework AppKit tests/native/macos-focus-smoke.m -L native/core/target/debug -lcore -Wl,-rpath,"$PWD/native/core/target/debug" -o /tmp/dotnative-focus-smoke
/tmp/dotnative-focus-smoke
```

## Rich text

```csharp
Text.Rich(
    new TextRun("Hello "),
    new TextRun("world", FontSize: 28, Weight: FontWeight.Bold, Foreground: Color.Blue),
    new TextRun(" 🙂", Decoration: TextDecoration.Underline)
).FontSize(18).MaxWidth(320);
```

Runs form one native text element and share paragraph alignment, wrapping and
line-height policy. Nullable run font size, family, weight, italic, foreground and
decoration inherit from Text. Inputs are copied; at most 256 runs are allowed.
UTF-16 ranges on Apple/Android/Windows and UTF-8 ranges in Pango are derived from complete
run strings, so surrogate pairs are preserved. Native measurement uses the same
attributed text as painting. Replacing rich Text with ordinary Text clears runs
without changing native identity. UIKit, AppKit, Android, GTK and Win32 Text support
runs. Win32 keeps the native STATIC control and uses DirectWrite for measurement
and Direct2D for drawing its attributed layout.

## Font assets and variable fonts

```csharp
// Embed fonts as .NET EmbeddedResource items to include them in AOT applications.
using var stream = typeof(Program).Assembly.GetManifestResourceStream("MyApp.Fonts.Example.ttf")!;
var face = await FontSource.FromStreamAsync("Example", stream);
var title = new Text("Variable typography")
    .FontSource(face)
    .FontVariations(new FontVariationSettings(new FontAxis("wdth", 125)))
    .FontSize(24);
```

`FontSource.FromBytes`, `FromFileAsync` and `FromStreamAsync` copy a single-face
TTF/OTF font (12 bytes–8 MiB). Use the font's embedded Unicode family name;
metadata mismatch, corrupt table ranges and native decoder failures are rejected.
WOFF is not supported. Separate Regular/Bold/Italic files
can share a family: attach each loaded source to a node using the matching
`.FontWeight(...)`/`.Italic()` style. Registration uses the embedded PostScript
face name. UIKit and AppKit preserve an explicitly attached face; changing the
weight alone does not replace an attached Bold file with the Regular file.
Attach the Regular source to switch back. Family-only selection delegates weight
and slant matching to the native toolkit.
Files need a Unicode PostScript name and OS/2 style metadata. Reusing a face name
with different bytes, registering the same face under another family alias, or
registering two faces with identical family/weight/slant is rejected. Static
width variants with the same weight/slant therefore need distinct families;
variable width axes remain supported.
A stream remains caller-owned. File paths are runtime filesystem paths; embedding
resources is the portable packaging option. Reuse a loaded `FontSource` instance.

Registration happens before intrinsic measurement on all five backends. Loading a
font also invalidates existing family users. Font registration is cached for the
UI-thread/process lifetime, limited to 64 faces and 32 MiB of source bytes.
Removing a node or `.FontSource(null)` resets its selection without unloading the
shared font. Registration may remain cached if a later operation rejects the
batch; tree state is still transactional. GTK uses private temporary font files.
Using `.FontFamily(...)` clears that node's asset selection.

`FontVariationSettings` accepts up to 16 unique four-character ASCII letter/digit
tags and finite coordinates. Inputs are copied and sorted. `null` or empty
settings restore default coordinates; unknown axes and out-of-range coordinates
follow the native font's behavior. UIKit, AppKit, Android and GTK apply axes to
measurement and visible text, including rich runs. Win32 Text applies axes with
DirectWrite; other Win32 controls still reject them. The GTK, AppKit and Windows
native font tests load an original variable test font, verify increased glyph
width and verify reset. Windows custom memory fonts require DirectWrite 3 (Windows
10); variable axes require IDWriteTextLayout4 on supported Windows 10/11 versions.
Unsupported interfaces fail validation before the candidate tree commits.

### TTC/OTC collections

```csharp
var bold = await FontSource.FromCollectionFileAsync("Example", "Fonts/Example.ttc", faceIndex: 1);
var label = new Text("Collection face").FontSource(bold).FontWeight(FontWeight.Bold);
```

`FromCollectionBytes`, `FromCollectionFileAsync` and `FromCollectionStreamAsync`
select one zero-based face from a TTC/OTC v1/v2 collection. Collection input is
bounded to 32 MiB and 256 faces; the extracted TTF/OTF remains bounded to 8 MiB.
The stream overload leaves its stream open and supports cancellation. The declared
family must match the selected face's internal family at native registration.

Extraction handles shared tables and TrueType/CFF outlines, checks selected table
ranges/alignment/duplicates, rebuilds table offsets and checksums, and removes
DSIG data that cannot authenticate a repacked file. These rules follow the
[OpenType collection format](https://learn.microsoft.com/en-us/typography/opentype/spec/otff).
The native wire format is unchanged. Use a consistent source for each PostScript
face: a repacked collection face and an independently packaged font may have
different bytes and will trigger the existing conflicting-face check.

The test suite has original TTC and OTC fixtures, malformed/truncated input cases,
checksum checks, cancellation and stream-ownership checks. To feed the C# extracted
faces into the real AppKit font registration/measurement test:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project tests/DotNative.Tests -- --export-font-collections /tmp/dotnative-collection-smoke
DOTNATIVE_FONT_COLLECTION_FIXTURES=/tmp/dotnative-collection-smoke cargo test --manifest-path native/core/Cargo.toml --features dev-host asset_family_and_variation_change_native_glyph_metrics
```

## System accessibility preferences

Packaged runners and desktop preview publish live `AccessibilityPreferences`.
Reduced motion finishes active declarative style transitions and makes subsequent
ones immediate. An application's `ReducedMotion = false` cannot override the OS.
This policy covers `StyleTransition`; custom `AnimationController` logic remains
application-owned.

Text size, explicit rich-run sizes, letter spacing and line height scale before
native measurement. `.FontScaling(false)` opts a textual node out. iOS uses the
preferred body font ratio, Android uses configuration font scale, and GTK uses
text DPI relative to 96 DPI. Win32 reads the same `TextScaleFactor` preference as
[Windows Forms](https://source.dot.net/System.Windows.Forms.Primitives/System/Windows/Forms/Internals/ScaleHelper.cs.html),
accepting the OS range 100–225% and falling back to 100% for missing/invalid values.
AppKit currently reports scale 1; its preferred text-style API does not guarantee
tracking the accessibility text-size setting. Window/display DPI scaling remains separate. Reported ratios are bounded to
0.5–5. Changes recompute from authored values, so repeated updates do not compound.

Inject `ISystemAccessibility` and read `.Preferences` during `Build()` for reactive
palette/layout choices. `HighContrast` does not automatically rewrite app colors.
iOS/macOS use their accessibility contrast setting, GTK recognizes HighContrast
theme names, Windows uses its high-contrast flag, and Android API 34+ reports
medium/high contrast as true (older versions report false). Custom hosts can call
`RenderHost.SetSystemAccessibility` or `ApplicationSession.SetSystemAccessibility`.

## Desktop frame scheduling

GTK preview pumps visible updates from the widget's frame clock. macOS 14+ runners
use a view-owned display link that tracks its display; older macOS runners retain
the 60 Hz timer fallback. Invisible windows retain a 100 ms event pump so async
startup, plugin replies and shutdown can progress when display callbacks pause.
Callbacks and timers are removed at shutdown. These paths have runtime checks for
visible frames, hidden-window progress and cleanup.

Windows 11 dynamically resolves
[`DCompositionWaitForCompositorClock`](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-dcompositionwaitforcompositorclock)
and waits off the UI thread with a 100 ms timeout. Notifications are coalesced
until the UI acknowledges them; teardown joins the worker before unloading its
DLL. Timeout/occlusion/error paths back off rather than busy-spin, while a 100 ms
UI timer keeps async work alive. Systems without this API use the existing 16 ms
UI timer. IPC wakeups never generate extra animation frames. The worker's queue,
shutdown and backoff are unit tested; frame delivery and callback cleanup also pass in a Windows 11 ARM64 VM running
the x64 renderer under emulation. Physical Windows hardware remains untested.

Run the AppKit runner smoke in a logged-in macOS session (it briefly opens a window):

```sh
xcrun clang -fobjc-arc -framework AppKit -framework QuartzCore tests/native/macos-runner-smoke.m -o /tmp/dotnative-runner-smoke
/tmp/dotnative-runner-smoke
```

## Backend status and limits

The GTK native smoke also checks opacity, typography, gradient/background reset,
sibling stacking, stable image identity and actual rendered pixels for fitting
and alpha-mask tint application/reset.
Shared layout has unit tests for bounds, percentage/absolute positioning, padding,
constraints, wrapping, reset and rollback. Presentation and typography are wired
into UIKit, AppKit, Android Views and GTK. Text measurement uses the same font
configuration as visible controls. Visual-only changes bypass layout.

`Button` and `RadioButton` implement `ITextFlow`, so the public C# API exposes
line limits, wrapping/truncation, letter and line spacing, underline and
strikethrough consistently with `Text`. UIKit/AppKit use attributed native
titles, GTK uses the native Pango label, Android configures the native TextView,
and Win32 uses the same DirectWrite layout for measurement and painting while
retaining native control input and accessibility.

The Win32 preview supports margins/padding/constraints/positioning, opacity,
borders, translation, fonts, italic, underline/strikethrough and basic alignment.
Text supports DirectWrite rich runs. Text, Button and RadioButton share DirectWrite
variable fonts, paragraph layout, alignment and bidirectional shaping. The Button
and RadioButton C# APIs expose font axes and alignment; the `ITextFlow` helpers
remain specific to Text. Native paragraph styles include letter spacing, custom
line height, positive line limits, justification and start/middle/end ellipsis. Measurement and drawing share the same native layout; measurement caches
include run styles, variation axes, spacing and flow direction. Grayscale text
antialiasing supports opacity composition without ClearType color fringes.
Ellipsis replaces complete native glyph clusters with a DirectWrite inline object,
retaining the original accessible text and rich-run offsets. Hard line breaks
beyond a positive line limit are elided on the last visible line.
Win32 renders bounded, premultiplied-alpha software shadows in a separate
click-through layered child window when clipping is disabled, preserving the
native control HWND. Unclipped Win32 descendants are reparented to the nearest
clipping HWND with accumulated position, scroll offset, visibility and opacity,
so they can paint outside intermediate container bounds. DirectComposition applies
centered scale and rotation to Image and empty visual-box surfaces; deletion and
reset remove the composition visual and uncloak the source HWND. Win32 rejects
transforms on text, controls and nonempty containers before commit because an HWND
redirected surface does not include its native child windows. Variable fonts and advanced paragraph typography on editable TextField remain
unsupported. Button and RadioButton retain their native HWNDs, accessible labels,
input and focus behavior; radio indicators use Windows theme drawing with a
classic fallback. These are explicit limitations, not equivalent support.
GTK line limits use ellipsis; combining a positive limit with explicit Wrap or
Clip is rejected before commit. Android shadows use a bounded, cached software bitmap for the shadow alone; native
controls stay accelerated. Native shaping/fallback and truncation conventions can
still differ across operating systems. GTK X11 and Win32 now have local runtime pixel and system-input checks; broader
platform coverage remains necessary. Cross-compilation alone does not establish visual parity.

## Remaining production work

This catalog is a substantial extension, **not “every style” or a production
readiness claim**. Remaining work includes
broader interaction-state coverage, Win32 transformed control/subtree composition and advanced
editable-text typography, AppKit accessibility text scaling, broader Windows hardware coverage, and
closing the Win32/GTK validation gaps. Each addition needs native behavior,
measurement where relevant, reset handling, and platform tests; do not add no-op
fluent methods to make the catalog look complete.

`ListView` is virtualized by default. WebView, CameraView and VideoPlayer remain
separate optional capabilities, outside the styling contract.
