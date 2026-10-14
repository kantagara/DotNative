# Screen headers

Implement `INavigationScreen` on a screen component. `BuildOptions` runs inside
reactive rendering: reading `State<T>` updates the header together with the screen.
It must not mutate state, just like `Build`.

```csharp
public sealed class MessagesScreen : Component, INavigationScreen
{
    private readonly State<bool> _editing = new(false);

    public ScreenOptions BuildOptions(BuildContext context) => new()
    {
        Title = "",
        Leading = [new ToolbarAction(_editing.Value ? "Done" : "Edit",
            () => _editing.Value = !_editing.Value)],
    };

    public override Element Build(BuildContext context) =>
        new Text(_editing.Value ? "Editing messages" : "Messages");
}
```

- `Title = null` uses the title passed to `PushAsync`, or the component type name.
- `Title = ""` removes only the title, preserving toolbar actions.
- `HeaderVisible = false` removes the header and releases its layout space.
- `BackButtonVisible` controls the visible Back button, not system Back handling.
- `Leading` and `Trailing` contain ordered actions, at most 32 in total.
- `Transition` selects `PlatformDefault`, `Slide`, `Fade`, or `None`, plus a
  duration and timing curve.
- Callbacks execute on the UI thread. Asynchronous callbacks must observe their
  exceptions (the sample's `NavigationActions` demonstrates this).

Set defaults with `builder.Services.AddDotNativeNavigation(new ScreenOptions { ... })`.
Screens without `INavigationScreen` use these defaults. Screens implementing it
return their complete options; options are not partially merged.

## Platform implementation

- iOS: `UINavigationController`, per-screen `UINavigationItem` and retained
  `UIBarButtonItem` actions. Header visibility follows UIKit push/pop completion
  and interactive navigation. A binary header command updates presentation;
  actions return through the existing queued event channel.
- Android: native `android.widget.Toolbar`, native leading buttons, trailing menu
  actions with overflow. The system Back callback pops managed navigation when
  possible, including when the header is hidden. Root Back uses OS behavior.
  Retained page views animate through Android property animators. Predictive Back
  gesture progress is not implemented yet.
- Mac: a native AppKit `NSToolbar` owns actions in the window chrome. The screen
  title becomes the window title; no extra header row consumes content space.
  AppKit manages toolbar sizing and overflow. Retained pages use Core Animation
  for slide/fade, with native completion driving managed Back disposal.
- Windows: retained Win32 pages, native header actions and DirectComposition
  `Slide`/`Fade` navigation. `None` switches immediately. Windows accessibility
  settings that disable client-area animation take precedence.
- Linux: retained GTK pages use the GTK frame clock and snapshot translation;
  moving a page does not change its layout or the window minimum size. GTK
  animation preferences are respected, and hidden windows still complete navigation.

System status-bar visibility, integrated search and
platform title-bar configuration are not exposed by this first header API.
Hiding the header preserves the existing system status bar and desktop chrome.

WidgetSamples uses a large iOS title, Add item/Cart
commands. The content contains a button to restore the header and, on iOS,
a slider for the header background opacity.

## Navigation transitions

Configure a screen's transition together with its other navigation options:

```csharp
public ScreenOptions BuildOptions(BuildContext context) => new()
{
    Title = "Messages",
    Transition = new NavigationTransition
    {
        Kind = NavigationTransitionKind.Fade,
        Duration = TimeSpan.FromMilliseconds(240),
        Easing = TransitionEasing.EaseOutCubic,
    },
};
```

The defaults are `PlatformDefault`, 180 ms, and `EaseInOut`. Timing choices are
`Linear`, `EaseInOut`, `EaseOutCubic`, and `EaseInCubic`. A duration must be
positive and at most ten seconds, except `None`, which also accepts zero.
`Slide`, `Fade`, and `None` are implemented on iOS, Android, macOS, Windows and
Linux. Duration and easing apply to `Slide` and `Fade`; `None` switches immediately.
All five backends retain the departing screen until native completion before
releasing its managed scope. Fade keeps the outgoing page opaque underneath the
incoming page.

On iOS, `PlatformDefault` preserves UIKit's system timing and interactive Back
(including cancellation); its `Duration` and `Easing` values are intentionally not
overrides of UIKit. Custom iOS `Slide` and `Fade` also support an interactive
edge swipe: UIKit's percent-driven controller owns progress, cancellation and
completion. Cancelling keeps the current screen and its scope; completing releases
the departing scope only after UIKit reports the destination as shown. With Reduce
Motion enabled, the outgoing page remains stationary until the gesture commits.
On Android, macOS, Windows and Linux, `PlatformDefault` selects the renderer's slide
transition with the configured duration/easing. OS reduce-motion/disabled-animation
settings take precedence when starting a transition. Android 14/API 34 and newer
use `OnBackAnimationCallback` for predictive Back from either edge. Progress
follows the system gesture; release/cancel settles using the configured duration
and easing for the remaining distance. With `None` or system animations disabled,
the page stays still until the gesture commits. Android 30–33 retain ordinary
system Back. The template and bundled samples opt into the platform Back callback
in `AndroidManifest.xml`.

Native runtime tests and their limits are described in
[runtime interaction tests](runtime-interaction-tests.md#navigation-transition-runtime-tests).

## iOS large titles and background opacity

```csharp
public ScreenOptions BuildOptions(BuildContext context) => new()
{
    Title = "Messages",
    TitleDisplay = NavigationTitleDisplay.Large,
    HeaderBackgroundOpacity = 0.85f,
};
```

`TitleDisplay` defaults to `Inline`. `Large` uses UIKit's navigation title,
with the screen's root `ScrollView` providing scrolling. Keep that scroll view
as the screen root so UIKit can coordinate its content insets and title.
The native controller owns title geometry; the title is not a C# text node.

`HeaderBackgroundOpacity` defaults to `1`, accepts finite values from `0` to
`1`, and affects the background only. It does not dim the title or actions and
is not a blur intensity setting. A large title uses a transparent scroll-edge
appearance so UIKit's title remains visible; opacity applies to its collapsed
bar. At values below `1`, content extends behind the header. Changes are reactive and preserve screen/controller identity.

Both options currently affect **iOS only**. Android and Mac retain their inline
headers; they do not emulate iOS large titles or transparent chrome.

The implementation uses the same public UIKit primitives as
[react-native-screens](https://github.com/software-mansion/react-native-screens/blob/main/ios/legacy/RNSScreenStackHeaderConfig.mm):
`prefersLargeTitles`, `largeTitleDisplayMode`, and `UINavigationBarAppearance`.
