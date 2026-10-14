# System appearance

`ISystemAppearance` is available through application DI. Its `Theme` property is
`ThemeMode.Light` or `ThemeMode.Dark` and participates in the same reactive read
tracking as `State<T>`. Native iOS and Mac runners report the OS preference;
Android reports its UI mode at startup and configuration changes.

Keep the user's selection in ordinary application state:

```csharp
public sealed class AppearanceStore(ISystemAppearance system)
{
    public State<ThemeMode> Mode { get; } = new(ThemeMode.System);

    public ThemeData Current
    {
        get
        {
            var effective = Mode.Value == ThemeMode.System ? system.Theme : Mode.Value;
            return (effective == ThemeMode.Dark ? ThemeData.Dark : ThemeData.Light)
                with { Mode = Mode.Value };
        }
    }
}
```

Pass `Current` to `new Theme(store.Current, child)`. `ThemeData.Mode` carries the
native appearance preference separately from the palette: iOS navigation and its
window use the system appearance for `System`, or the selected explicit override.
The preference is never inferred from the background color's luminance.

WidgetSamples defaults to System. Its appearance picker selects one of System, Light or Dark.
The sample stores the selection only in memory. No persistence is involved.

Native navigation is currently implemented on iOS. Its standard, compact and
scroll-edge navigation bar appearances share the palette's opaque background to
avoid material/background changes during a push. Android/Mac native navigation
and complete window-chrome theme overrides are separate remaining work.
