using System.Buffers.Binary;
using DotNative;

internal static class AccessibilityTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var clock = new AnimationTests.ManualClock();
        var native = new NativeModel();
        var state = new State<Text>(
            new Text("scaled").FontSize(20).Transition(new(TimeSpan.FromSeconds(1)))
        );
        using var host = new RenderHost(
            new Factory(() =>
                new VStack(state.Value, new Text("fixed").FontSize(12).FontScaling(false))
            ),
            dispatcher,
            native,
            clock
        );
        host.RenderNow();
        var id = native.Find("scaled");
        var fixedId = native.Find("fixed");
        host.SetSystemAccessibility(new(true, 2, true));
        dispatcher.Drain();
        Check(Font(id) == 40 && Font(fixedId) == 12 && host.ReducedMotion);
        host.ReducedMotion = false;
        Check(host.ReducedMotion);
        state.Value = state.Value.Opacity(.25f);
        dispatcher.Drain();
        Check(
            BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle) == .25f
                && clock.Active == 0
        );
        host.SetSystemAccessibility(AccessibilityPreferences.Default);
        dispatcher.Drain();
        Check(Font(id) == 20 && !host.ReducedMotion);
        state.Value = state.Value.Opacity(1);
        dispatcher.Drain();
        clock.Advance(.5);
        dispatcher.Drain();
        host.SetSystemAccessibility(new(true, 1, false));
        Check(
            clock.Active == 0
                && BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle) == 1
        );
        try
        {
            host.SetSystemAccessibility(new(false, float.NaN, false));
            throw new Exception("Invalid scale accepted");
        }
        catch (ArgumentOutOfRangeException) { }
        var source = new SystemAccessibility();
        int reads = 0;
        using var reader = new RenderHost(
            new Factory(() =>
            {
                reads++;
                return new Text(source.Preferences.HighContrast.ToString());
            }),
            dispatcher,
            new NativeModel()
        );
        reader.RenderNow();
        source.Update(new(false, 1, true));
        dispatcher.Drain();
        Check(reads == 2);
        var richNative = new NativeModel();
        var authored = Text.Rich(new TextRun("A", FontSize: 30)).FontSize(20);
        using var richHost = new RenderHost(new Factory(() => authored), dispatcher, richNative);
        richHost.SetSystemAccessibility(new(false, 2, false));
        richHost.RenderNow();
        Check(RunSize() == 60);
        richHost.SetSystemAccessibility(new(false, 3, false));
        dispatcher.Drain();
        Check(RunSize() == 90);
        richHost.SetSystemAccessibility(AccessibilityPreferences.Default);
        dispatcher.Drain();
        Check(RunSize() == 30 && authored.Runs[0].FontSize == 30);
        float RunSize() =>
            BinaryPrimitives.ReadSingleLittleEndian(
                richNative.Nodes[richNative.Root].TextRuns.AsSpan(10)
            );
        float Font(uint node) =>
            BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[node].Style.AsSpan(16));
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Accessibility preference contract failed");
    }
}
