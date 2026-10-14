using System.Buffers.Binary;
using DotNative;

internal static class WheelPickerTests
{
    public static void Run()
    {
        foreach (var direction in new[] { -1, 1 })
        foreach (var scale in new[] { 1f, 2f })
            RoundedNativeRepliesDoNotRestartAnimationFrames(direction, scale);
        SettlingReachesTheCenterWithoutAFinalJump();
    }

    private static void RoundedNativeRepliesDoNotRestartAnimationFrames(int direction, float scale)
    {
        var dispatcher = new Dispatcher();
        var clock = new AnimationTests.ManualClock();
        var native = new NativeModel();
        var wheel = new WheelPicker(
            Enumerable.Range(1, 31).Select(day => day.ToString()).ToArray(),
            12,
            _ => { },
            dispatcher,
            clock
        );
        using var host = new RenderHost(wheel, dispatcher, native);
        host.SetViewport(100, 160);
        dispatcher.Drain();
        host.RenderNow();
        var id = native.Nodes.Single(pair => pair.Value.Kind == (byte)NodeKind.ScrollView).Key;
        uint Revision() => BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[id].ScrollPosition);
        float Offset() =>
            BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].ScrollPosition.AsSpan(4));
        void Report(float offset) =>
            host.DispatchInput(id, NativeEventKind.ScrollChanged, offset, $"{Revision()}:124");

        host.DispatchInput(id, NativeEventKind.ScrollDraggingChanged, 1, "");
        Report(Offset() + direction * 32.5f);
        dispatcher.Drain();
        host.DispatchInput(id, NativeEventKind.ScrollDraggingChanged, 0, "");
        host.DispatchInput(id, NativeEventKind.ScrollFling, 0, "");
        dispatcher.Drain();

        var roundedFrames = 0;
        for (var frame = 0; frame < 32; frame++)
        {
            clock.Advance(0.016);
            dispatcher.Drain();
            var revision = Revision();
            var requested = Offset();
            var rounded = MathF.Round(requested * scale) / scale;
            if (requested != rounded)
                roundedFrames++;
            Report(rounded);
            dispatcher.Drain();
            Check(
                Revision() == revision,
                "A rounded native reply must not send the same animation position again"
            );
        }
        Check(roundedFrames > 0, "The test must exercise fractional native requests");
    }

    private static void SettlingReachesTheCenterWithoutAFinalJump()
    {
        foreach (var direction in new[] { -1, 1 })
        {
            var motion = WheelScrollSimulation.Create(558468 + direction * 3.5f, 0, 36, 1_115_964);
            var penultimate = motion.Position(motion.Duration - 1.0 / 60);
            Check(
                Math.Abs(penultimate - motion.Target) < 0.001,
                "The last frame must approach the row center before committing the exact target"
            );
            Check(motion.At(1) == motion.Target, "Settling must finish exactly on a row");
            var nearEnd = motion.At(0.9f);
            Check(
                Math.Abs(nearEnd - (float)nearEnd) > 0.00001,
                "Looping offsets must retain subpixel precision until converted to local coordinates"
            );
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
