using DotNative;

internal static class LayoutPolicyTests
{
    public static void Run()
    {
        var axis = new State<Axis>(Axis.Vertical);
        var shrink = new State<float>(0);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() =>
                new ScrollView(new Text("content").Shrink(shrink.Value)) { ScrollAxis = axis.Value }
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var scroll = native.Root;
        var text = native.Find("content");
        foreach (var value in new[] { Axis.Horizontal, Axis.Vertical, Axis.Horizontal })
        {
            axis.Value = value;
            shrink.Value = value == Axis.Horizontal ? 1 : 0;
            dispatcher.Drain();
            if (
                native.Root != scroll
                || native.Find("content") != text
                || native.Nodes[scroll].ScrollAxis != (byte)value
                || native.Nodes[text].FlexShrink != shrink.Value
            )
                throw new Exception("Axis/shrink update or reset lost state or identity.");
        }
        foreach (var value in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            Throws(() => new Text("x").Shrink(value));
            Throws(() => new CommandBuffer().SetLayout(1, new(FlexShrink: value)));
        }
        Throws(() => new CommandBuffer().SetScrollAxis(1, (Axis)99));
    }

    private static void Throws(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new Exception("Invalid layout input accepted.");
    }
}
