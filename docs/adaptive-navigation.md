# Desktop sidebar and mobile bottom navigation

`SideDock` defaults to a native AppKit split-view sidebar on macOS and a WinUI 3
`NavigationView` island on Windows. Linux uses the composed sidebar. Select
`DesktopNavigationPresentation.Composed` explicitly to use the same composed
implementation on every desktop platform.

```csharp
new SideDock(body, [
    new DockItem("Home", OpenHome, "⌂") {
        MacOSSymbol = "house",
        WindowsGlyph = "\uE80F"
    },
    new DockItem("Settings", OpenSettings, "⚙") {
        MacOSSymbol = "gearshape",
        WindowsGlyph = "\uE713"
    }
], isOpen, value => isOpen = value) {
    Title = "My application",
    CurrentIndex = selectedIndex,
    ExpandedWidth = 260
};
```

Native sidebar appearance follows the enclosing application's `ThemeData.Mode`.
Set `Appearance = ThemeMode.System` to follow the OS independently, or explicitly
choose `Light` or `Dark`. Leaving `Appearance` null follows the application.
Changing this value updates the existing native control without rebuilding it.

The application owns selection and open state. `CurrentIndex = -1` clears the
selection. Native symbols have dedicated image/glyph slots, separate from labels.
`Icon` remains a text fallback for composed rendering. Native material, selection,
focus, hover, and accessibility come from the operating system. They can therefore
change between OS versions. macOS 26 supplies the split-view glass treatment.
The native presentation currently exposes title, items, symbols, selection,
open state, and expanded width (180–1000 points). Arbitrary per-row colors,
materials, dimensions and animation curves are not yet exposed by this wrapper.
`Duration`, `Easing`, and `BottomInset` apply to the composed sidebar only.

The Windows CLI builds `native/winui` with .NET 10 NativeAOT and includes the pinned
Windows App SDK runtime, activation manifest and XAML resource index beside the
renderer. Keep that output directory intact when distributing a preview. A missing
bridge is an explicit error. This does not add a managed JIT runtime to the renderer.

On mobile, use the `Scaffold.BottomNavigationBar` API:

```csharp
new Scaffold(body) {
    BottomNavigationBar = new BottomNavigationBar([
        new("Home", BottomNavigationIcon.Home),
        new("Search", BottomNavigationIcon.Search),
        new("Profile", BottomNavigationIcon.Profile)
    ], selectedIndex, index => selectedIndex = index),
    BottomBarVisible = true,
    Duration = TimeSpan.FromMilliseconds(240),
    Easing = TransitionEasing.EaseOutCubic
};
```

This hosts `UITabBar` on iOS and Material `BottomNavigationView` on Android. The bar
belongs at the bottom of the scaffold; keep `BottomInset` at zero unless deliberate
extra spacing is needed. Platform safe areas are counted once. `BottomInset` is
additional application spacing, not the system gesture inset. UIKit controls its
own visual inset/glass appearance on newer iOS versions.

Use bottom destinations for primary navigation, and put transient actions in the
screen or toolbar. The widget demo also includes action items to exercise events
and hide/reveal; those actions are not a recommended application navigation model.
