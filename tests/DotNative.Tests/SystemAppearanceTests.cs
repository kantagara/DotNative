using DotNative;

internal static class SystemAppearanceTests
{
    public static void Run()
    {
        var system = new SystemAppearance(ThemeMode.Dark);
        var mode = new State<ThemeMode>(ThemeMode.System);
        var dispatcher = new Dispatcher();
        int builds = 0;
        ThemeMode rendered = ThemeMode.Light;
        using var host = new RenderHost(
            new Factory(() =>
            {
                builds++;
                rendered = mode.Value == ThemeMode.System ? system.Theme : mode.Value;
                return new Text(rendered.ToString());
            }),
            dispatcher,
            new NativeModel()
        );
        host.RenderNow();
        Require(rendered == ThemeMode.Dark);
        system.Update(ThemeMode.Light);
        dispatcher.Drain();
        Require(rendered == ThemeMode.Light && builds == 2);
        mode.Value = ThemeMode.Dark;
        dispatcher.Drain();
        int explicitBuilds = builds;
        system.Update(ThemeMode.Dark);
        system.Update(ThemeMode.Light);
        dispatcher.Drain();
        Require(builds == explicitBuilds && rendered == ThemeMode.Dark);
        mode.Value = ThemeMode.System;
        dispatcher.Drain();
        Require(rendered == ThemeMode.Light);
        RetainedAndRecycledThemeColors();
    }

    private static void RetainedAndRecycledThemeColors()
    {
        foreach (
            var platform in new[]
            {
                NativePlatform.Windows,
                NativePlatform.MacOS,
                NativePlatform.Linux,
                NativePlatform.IOS,
                NativePlatform.Android,
            }
        )
        {
            var dispatcher = new Dispatcher();
            var native = new NativeModel();
            var theme = new State<ThemeData>(ThemeData.Light);
            var button = new Button("retained", () => { }).OnState(
                InteractionState.Hovered,
                new(Opacity: .8f)
            );
            var list = new ListView<int>(
                Enumerable.Range(0, 1000).ToArray(),
                i => new Text($"row {i}"),
                i => i.ToString(),
                40
            )
            {
                Height = 400,
            };
            var branch = new VStack(
                button,
                new Text("explicit").TextColor(new Color(17, 90, 180)),
                list
            );
            using var host = new RenderHost(
                new Factory(() =>
                    new ContextScope<PresentationTarget>(
                        new(platform),
                        new Theme(theme.Value, branch)
                    )
                ),
                dispatcher,
                native
            );
            host.SetViewport(400, 600);
            host.RenderNow();
            uint retained = native.Find("retained");
            uint scroll = native.Nodes.Single(n => n.Value.Kind == 4).Key;
            for (int cycle = 0; cycle < 20; cycle++)
            {
                bool dark = cycle % 2 == 0;
                theme.Value = dark ? ThemeData.Dark : ThemeData.Light;
                host.DispatchInput(
                    retained,
                    NativeEventKind.HoverChanged,
                    cycle % 3 == 0 ? 1 : 0,
                    ""
                );
                host.DispatchInput(
                    scroll,
                    NativeEventKind.ScrollChanged,
                    cycle % 2 == 0 ? 12000 : 0,
                    "0:400"
                );
                dispatcher.Drain();
                Require(native.Find("retained") == retained);
                byte ink = dark ? (byte)255 : (byte)0;
                foreach (
                    var node in native.Nodes.Values.Where(n =>
                        n.Text == "retained" || n.Text.StartsWith("row ")
                    )
                )
                    Require(
                        node.Style.AsSpan(12, 4).SequenceEqual(new byte[] { ink, ink, ink, 255 })
                    );
                Require(
                    native
                        .Nodes[native.Find("explicit")]
                        .Style.AsSpan(12, 4)
                        .SequenceEqual(new byte[] { 17, 90, 180, 255 })
                );
            }
        }
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("System appearance assertion failed.");
    }
}
