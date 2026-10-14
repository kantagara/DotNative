using System.Buffers.Binary;
using DotNative;

internal static class HeaderTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var visible = new State<bool>(true);
        var opacity = new State<float>(1);
        var display = new State<NavigationTitleDisplay>(NavigationTitleDisplay.Inline);
        var label = new State<string>("Filter");
        int count = 0;
        int multiplier = 1;
        using var host = new RenderHost(
            new Factory(() =>
            {
                int captured = multiplier;
                return new NativeNavigationPage(
                    new Text("Content"),
                    new ScreenOptions
                    {
                        Title = "",
                        HeaderBackgroundOpacity = opacity.Value,
                        TitleDisplay = display.Value,
                        HeaderVisible = visible.Value,
                        Leading = [new ToolbarAction(label.Value, () => count += captured)],
                        Trailing = [new ToolbarAction("Clear", () => count = 0)],
                    }.Snapshot("Fallback")
                );
            }),
            dispatcher,
            native
        );
        host.RenderNow();
        var page = native.Nodes.Single(x => x.Value.Kind == 11).Key;
        Require(native.Nodes[page].Text == "");
        host.DispatchInput(page, NativeEventKind.ValueChanged, 0, "");
        Require(count == 1);
        multiplier = 10;
        int flushes = native.Flushes;
        host.RenderNow();
        Require(native.Flushes == flushes); // Fresh delegates without native churn.
        host.DispatchInput(page, NativeEventKind.ValueChanged, 0, "");
        Require(count == 11);
        foreach (float invalid in new[] { -1f, 0.5f, 2f, float.NaN })
            host.DispatchInput(page, NativeEventKind.ValueChanged, invalid, "");
        Require(count == 11);
        visible.Value = false;
        dispatcher.Drain();
        Require((BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[page].Header) & 1) == 0);
        host.DispatchInput(page, NativeEventKind.ValueChanged, 1, "");
        Require(count == 11);
        visible.Value = true;
        label.Value = "Different action";
        dispatcher.Drain();
        Require(native.Nodes.Single(x => x.Value.Kind == 11).Key == page);
        host.DispatchInput(page, NativeEventKind.ValueChanged, 1, "");
        Require(count == 0);
        Require(new ScreenOptions().Snapshot("Default").Title == "Default");
        opacity.Value = 0.5f;
        display.Value = NavigationTitleDisplay.Large;
        dispatcher.Drain();
        Require(native.Nodes[page].HeaderOpacity == 0.5f);
        Require((BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[page].Header) & 8) != 0);
        foreach (float invalid in new[] { -1f, 1.1f, float.NaN })
        {
            try
            {
                new ScreenOptions { HeaderBackgroundOpacity = invalid }.Snapshot("x");
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }
            throw new Exception("Invalid opacity accepted.");
        }
        foreach (
            var transition in new NavigationTransition[]
            {
                new() { Kind = (NavigationTransitionKind)99 },
                new() { Easing = (TransitionEasing)99 },
                new() { Duration = TimeSpan.Zero },
                new() { Duration = TimeSpan.FromSeconds(11) },
            }
        )
        {
            try
            {
                new ScreenOptions { Transition = transition }.Snapshot("x");
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }
            throw new Exception("Invalid navigation transition accepted.");
        }
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Header assertion failed.");
    }
}
