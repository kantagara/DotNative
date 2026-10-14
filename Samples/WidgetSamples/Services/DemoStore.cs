using DotNative;

namespace WidgetSamples.Services;

// One instance for the application session, shared by both screens.
public sealed class DemoStore(ISystemAppearance system)
{
    public State<int> Count { get; } = new(0);
    public State<ThemeMode> Mode { get; } = new(ThemeMode.System);
    public bool IsDark =>
        (Mode.Value == ThemeMode.System ? system.Theme : Mode.Value) == ThemeMode.Dark;
    public ThemeData Theme =>
        (IsDark ? ThemeData.Dark : ThemeData.Light) with
        {
            Mode = Mode.Value,
        };
    public State<int> CompletedVisits { get; } = new(0);
    public Color Surface => IsDark ? new Color(45, 47, 54) : new Color(242, 244, 248);

    public void Add() => Count.Value++;

    public void Remove() => Count.Value = Math.Max(0, Count.Value - 1);
}

// A fresh instance for each cart screen. Popping the screen ends the visit.
public sealed class CartVisit(DemoStore store) : IDisposable
{
    public State<string> Note { get; } = new("");

    public void Dispose() => store.CompletedVisits.Value++;
}
