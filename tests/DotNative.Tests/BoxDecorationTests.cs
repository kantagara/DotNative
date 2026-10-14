using DotNative;

internal static class BoxDecorationTests
{
    public static void Run()
    {
        var plain = new VStack();
        var styled = plain
            .CornerRadius(24, 0, 12, 4)
            .Border(3, new Color(255, 0, 0), BorderLineStyle.Dashed)
            .BorderLeft(BorderSide.None);
        Check(plain.Decoration == default);
        Check(styled.Decoration.Left == BorderSide.None);
        Check(styled.Border(1, new Color(0, 0, 0)).Decoration.Top is null);
        Check(styled.CornerRadius(5).Decoration.Corners is null);
        var state = new State<VStack>(styled);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(new Factory(() => state.Value), dispatcher, native);
        host.RenderNow();
        var id = native.Root;
        var bytes = native.Nodes[id].Decoration;
        Check(bytes.Length == 53 && bytes[0] == 31);
        Check(BitConverter.ToSingle(bytes, 1) == 24 && BitConverter.ToSingle(bytes, 5) == 0);
        Check(BitConverter.ToSingle(bytes, 17) == 0 && bytes[34] == 1);
        state.Value = plain;
        dispatcher.Drain();
        Check(native.Root == id && native.Nodes[id].Decoration.All(b => b == 0));
        foreach (
            var bad in new[]
            {
                new BoxDecoration(Corners: new(float.NaN, 0, 0, 0)),
                new BoxDecoration(Left: new(-1, default)),
                new BoxDecoration(Top: new(float.PositiveInfinity, default)),
                new BoxDecoration(Right: new(1, default, (BorderLineStyle)255)),
            }
        )
        {
            var buffer = new CommandBuffer();
            try
            {
                buffer.SetBoxDecoration(1, bad);
                throw new Exception("Invalid decoration accepted");
            }
            catch (ArgumentOutOfRangeException)
            {
                Check(buffer.IsEmpty);
            }
        }
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Box decoration contract failed");
    }
}
