using System.Buffers.Binary;
using DotNative;

internal static class DockTests
{
    public static void Run()
    {
        SystemThemeContrast();
        NativeSidebarEvents(NativePlatform.MacOS);
        NativeSidebarEvents(NativePlatform.Windows);
        NativeSidebarTheme(NativePlatform.MacOS);
        NativeSidebarTheme(NativePlatform.Windows);
        foreach (bool mobile in new[] { false, true })
        {
            var open = new State<bool>(true);
            var selected = new State<int>(0);
            int actions = 0;
            var clock = new AnimationTests.ManualClock();
            var dispatcher = new Dispatcher();
            var native = new NativeModel();
            using var host = new RenderHost(
                new Factory(() =>
                    mobile
                        ? new Scaffold(new Text("body"))
                        {
                            BottomNavigationBar = new BottomNavigationBar(
                                [new("First"), new("Second")],
                                selected.Value,
                                i => selected.Value = i
                            ),
                            BottomBarVisible = open.Value,
                            Duration = TimeSpan.FromSeconds(1),
                            Easing = TransitionEasing.Linear,
                        }
                        : new SideDock(
                            new Text("body"),
                            [new("Add", () => actions++)],
                            open.Value,
                            value => open.Value = value
                        )
                        {
                            Duration = TimeSpan.FromSeconds(1),
                            Easing = TransitionEasing.Linear,
                            Presentation = DesktopNavigationPresentation.Composed,
                        }
                ),
                dispatcher,
                native,
                clock
            );
            host.RenderNow();
            var action = mobile
                ? native.Nodes.Single(pair => pair.Value.Kind == 14).Key
                : native.Find("Add");
            var panel = native.Nodes[native.Root].Children[1];
            if (!mobile)
                Check(host.DispatchClick(action) && actions == 1);
            if (mobile)
            {
                host.DispatchInput(action, NativeEventKind.ValueChanged, 1, "");
                dispatcher.Drain();
                Check(selected.Value == 1);
                Check(
                    BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[action].BottomNavigation)
                        == 1
                );
                foreach (float invalid in new[] { -1f, 2f, .5f, float.NaN, float.PositiveInfinity })
                    host.DispatchInput(action, NativeEventKind.ValueChanged, invalid, "");
                Check(selected.Value == 1);
            }
            open.Value = false;
            dispatcher.Drain();
            Check(!native.Nodes[action].Enabled && !host.DispatchClick(action));
            if (mobile)
            {
                host.DispatchInput(action, NativeEventKind.ValueChanged, 0, "");
                Check(selected.Value == 1);
            }
            clock.Advance(.5);
            dispatcher.Drain();
            float opacity = BinaryPrimitives.ReadSingleLittleEndian(
                native.Nodes[panel].VisualStyle
            );
            Check(opacity > 0 && opacity < 1);
            open.Value = true;
            dispatcher.Drain();
            Check(native.Nodes[action].Enabled);
            host.ReducedMotion = true;
            Check(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[panel].VisualStyle) == 1);
            open.Value = false;
            dispatcher.Drain();
            Check(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[panel].VisualStyle) == 0);
        }
    }

    private static void NativeSidebarEvents(NativePlatform platform)
    {
        var native = new NativeModel();
        var open = new State<bool>(true);
        int calls = 0;
        var dispatcher = new Dispatcher();
        using var host = new RenderHost(
            new Factory(() =>
                new ContextScope<PresentationTarget>(
                    new(platform),
                    new SideDock(
                        new Text("body"),
                        [new("Home", () => calls++)],
                        open.Value,
                        value => open.Value = value
                    )
                    {
                        CurrentIndex = 0,
                    }
                )
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        uint id = native.Nodes.Single(p => p.Value.Kind == 15).Key;
        Check(native.Nodes[id].DesktopNavigation.Length > 16);
        host.DispatchInput(id, NativeEventKind.ValueChanged, 0, "");
        Check(calls == 1);
        foreach (float invalid in new[] { -2f, 1f, .5f, float.NaN, float.PositiveInfinity })
            host.DispatchInput(id, NativeEventKind.ValueChanged, invalid, "");
        Check(calls == 1 && open.Value);
        host.DispatchInput(id, NativeEventKind.ValueChanged, -1, "");
        dispatcher.Drain();
        Check(!open.Value && native.Nodes.ContainsKey(id));
    }

    private static void NativeSidebarTheme(NativePlatform platform)
    {
        var native = new NativeModel();
        var theme = new State<ThemeData>(ThemeData.Light);
        var appearance = new State<ThemeMode?>(null);
        var dispatcher = new Dispatcher();
        using var host = new RenderHost(
            new Factory(() =>
                new Theme(
                    theme.Value,
                    new ContextScope<PresentationTarget>(
                        new(platform),
                        new SideDock(new Text("body"), [new("Home", () => { })], true, _ => { })
                        {
                            Appearance = appearance.Value,
                        }
                    )
                )
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        uint id = native.Nodes.Single(p => p.Value.Kind == 15).Key;
        void Expect(ThemeMode expected)
        {
            dispatcher.Drain();
            Check(native.Nodes.ContainsKey(id));
            Check(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    native.Nodes[id].DesktopNavigation.AsSpan(16)
                ) == (uint)expected
            );
        }
        Expect(ThemeMode.Light);
        theme.Value = ThemeData.Dark;
        Expect(ThemeMode.Dark);
        appearance.Value = ThemeMode.System;
        Expect(ThemeMode.System);
        theme.Value = ThemeData.Light;
        Expect(ThemeMode.System);
        appearance.Value = ThemeMode.Dark;
        Expect(ThemeMode.Dark);
        appearance.Value = null;
        Expect(ThemeMode.Light);
    }

    private static void SystemThemeContrast()
    {
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() =>
                new Theme(
                    ThemeData.Dark with
                    {
                        Mode = ThemeMode.System,
                    },
                    new SideDock(new Text("body"), [new("Home", () => { })], true, _ => { })
                    {
                        CurrentIndex = 0,
                        Presentation = DesktopNavigationPresentation.Composed,
                    }
                )
            ),
            new Dispatcher(),
            native
        );
        host.RenderNow();
        var rail = native.Nodes[native.Root].Children[0];
        // System mode keeps resolved dark colors; it must not choose a light surface.
        Check(native.Nodes[rail].Style[4] < 64);
        var selected = native.Nodes[native.Find("Home")];
        Check(selected.Style[4] > native.Nodes[rail].Style[4]);
        Check(selected.Style[12] > selected.Style[4]);
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("Dock interaction/transition contract failed");
    }
}
