# WidgetSamples

Run on an already booted iOS simulator:

```sh
dotnet run --project Samples/WidgetSamples/WidgetSamples.csproj
```

Run on macOS:

```sh
bash Samples/WidgetSamples/Platform/Mac/run.sh
```

## Shared state and navigation demo

1. Add an item on the home screen, then select **Open cart**.
2. Add another item and enter a note.
3. Go back: the shared cart retains both items and **Completed visits** increases.
4. Open the cart again: the cart is retained, but the new visit has an empty note.
5. Switch the theme on either screen: both use the same application state.

`Program.cs` registers `DemoStore` as a singleton and `CartVisit` as scoped.
`Screens/HomeScreen.cs` and `Screens/CartScreen.cs` receive dependencies through
constructors. The navigator retains the previous screen instance while another
screen is open and disposes the popped screen's scope. Existing input samples
remain on the home screen.

On iOS, navigation uses a UIKit header, automatic Back button and native push/pop
transitions. Other platforms currently use the managed stack and explicit Back
button. Modals and deep links remain unimplemented.

The appearance picker selects one of System, Light or Dark. System reacts to the OS appearance
without restarting the app. State here is in memory for the application
session; the demo does not persist the cart or notes.

### Adaptive dock sample

Desktop builds use a full-height navigation sidebar (`SideDock`), with the screen
content alongside it. The menu button collapses it to a narrow rail; navigation
items slide and fade out. Content reflows immediately when the sidebar toggles. Mobile builds use `Scaffold` with a real
UIKit `UITabBar` on iOS and Material `BottomNavigationView` on Android.

```csharp
new Scaffold(body)
{
    BottomNavigationBar = new BottomNavigationBar(
        [new("Home", BottomNavigationIcon.Home), new("Cart", BottomNavigationIcon.Favorites)],
        currentIndex: selected.Value,
        onTap: index => selected.Value = index),
    BottomBarVisible = dockVisible.Value,
    Duration = TimeSpan.FromMilliseconds(260),
    Easing = TransitionEasing.EaseOutCubic,
};
```

Place `Scaffold` inside a navigation screen; `NavigationHost` remains the app root.
`CurrentIndex` and visibility are controlled by application state. `OnTap` selects
or navigates according to app policy. Native bottom navigation accepts 2–5 items
and uses platform icons, selection feedback and accessibility. It is mobile only.
Keep a reveal action in `Body` when hiding the bar. Hidden controls are disabled;
space stays reserved to avoid layout jumps, and Reduce Motion skips transitions.
`BottomInset` optionally adds space; the sample uses zero because its host already
handles the system safe area. On iOS, native tabs own the bottom gesture inset;
screens without tabs retain that inset in their content layout. Arbitrary custom tab builders and center notches
are intentionally outside this native control API.

Desktop `SideDock` accepts `DockItem` actions, controlled `isOpen` /
`onOpenChanged`, `CurrentIndex`, `Title`, `ExpandedWidth`, and `Duration` / `Easing` settings.
`DockItem.Icon` optionally prefixes the native button label with a text glyph.
The selected item uses a distinct background. Navigation items scroll independently
of the screen body; the expanded sidebar reserves its own content column.
For custom button feedback, use `.OnState(InteractionState.Hovered, new(...))`
and `.Transition(new StyleTransition(...))`. `PrimaryButton` supplies opacity
feedback by default; GTK no longer paints a second hover background over it.
