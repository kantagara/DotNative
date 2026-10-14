using System.Buffers.Binary;
using DotNative;

internal static class TransitionTests
{
    public static void Run()
    {
        var clock = new AnimationTests.ManualClock();
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var transition = new StyleTransition(
            TimeSpan.FromSeconds(1),
            easing: TransitionEasing.Linear
        );
        var state = new State<Text>(new Text("transition").Transition(transition));
        using var host = new RenderHost(new Factory(() => state.Value), dispatcher, native, clock);
        host.RenderNow();
        var id = native.Root;
        state.Value = state.Value.Opacity(0).Translate(100, 0).CornerRadius(20, 0, 10, 0);
        dispatcher.Drain();
        clock.Advance(.5);
        dispatcher.Drain();
        Check(native.Root == id && Opacity(native, id) == .5f && Translation(native, id) == 50);
        state.Value = state.Value.Opacity(1).Translate(200, 0);
        dispatcher.Drain();
        Check(Translation(native, id) == 50);
        clock.Advance(.5);
        dispatcher.Drain();
        Check(Opacity(native, id) == .75f && Translation(native, id) == 125);
        host.ReducedMotion = true;
        Check(Translation(native, id) == 200 && clock.Active == 0);
        host.ReducedMotion = false;
        state.Value = state.Value.Opacity(.2f).Translate(0, 0);
        dispatcher.Drain();
        host.AdvanceAnimationFrame();
        Check(clock.Active == 0);
        clock.Advance(.5);
        host.AdvanceAnimationFrame();
        Check(Translation(native, id) == 100);
        state.Value = state.Value.Transition(null);
        dispatcher.Drain();
        Check(Translation(native, id) == 0 && Opacity(native, id) == .2f);
        state.Value = state.Value.Transition(transition).Opacity(1);
        dispatcher.Drain();
        host.Dispose();
        clock.Advance(1);
        dispatcher.Drain();
        Check(clock.Active == 0 && native.Root == 0);
    }

    private static float Opacity(NativeModel native, uint id) =>
        BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle);

    private static float Translation(NativeModel native, uint id) =>
        BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle.AsSpan(28));

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Transition contract failed");
    }
}
