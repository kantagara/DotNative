# Nested scrolling

`ScrollView.NestedScrolling` and `ListView<T>.NestedScrolling` default to
`NestedScrollMode.Contain`. Set `NestedScrollMode.Chain` explicitly to opt in.

```csharp
new ListView<Item>(items, item => new Text(item.Name), item => item.Id)
{
    Height = 240,
    NestedScrolling = NestedScrollMode.Chain,
};
```

Vertical Android scrolling implements both modes using AndroidX NestedScrollView:

- Contain disables nested touch/non-touch dispatch and prevents ancestor touch
  interception during the gesture. Momentum stays in the list; a new gesture
  outside the list scrolls the outer screen.
- Chain uses AndroidX parent/child nested scrolling, including unconsumed fling
  distance at content boundaries.

Changing the mode updates the existing native view. It does not recreate rows.
Clipping remains enabled in both modes.

Platform coverage: the policy currently controls Android only. iOS continues to
use UIKit UIScrollView and Mac uses NSScrollView with their existing native
behavior. Explicit cross-platform momentum chaining/containment is not implemented
for these backends. UIKit bounce is independent of momentum transfer; Contain is
not a request to disable elastic edge feedback.

## Horizontal scrolling

```csharp
new ScrollView(new HStack(cards))
{
    ScrollAxis = Axis.Horizontal,
}.Height(180);
```

`ScrollAxis` defaults to `Axis.Vertical`. It controls the scroll viewport; the
child's `HStack`/`VStack` controls how items are laid out. Set a bounded viewport
in the scrolling direction. Changing axis preserves managed IDs and child views,
but resets the native scroll offset. Android replaces only the scroll wrapper.

Android horizontal scrolling uses `HorizontalScrollView`. `Contain` prevents
ancestor gesture interception. `Chain` permits the platform's normal gesture
interception; AndroidX's vertical unconsumed-fling transfer does not apply to this
horizontal wrapper. UIKit and AppKit use native scroll views on either axis,
with the non-scrolling content dimension constrained to the viewport.

## Android regression check

After rebuilding the Counter native library/APK, run its activity test explicitly:

```sh
bash Samples/Counter/Platform/Android/gradlew -p Samples/Counter/Platform/Android \
  -Pandroid.testInstrumentationRunnerArguments.class=com.dotnative.runtime.CounterTest \
  connectedDebugAndroidTest
```

The test checks counter updates and native view identity, horizontal content
extent and actual scrolling, then both axis transitions with retained children.
It uses one activity because the current native runtime owns one session per
process. Select the class explicitly; legacy runner discovery is unreliable on
API 36.
