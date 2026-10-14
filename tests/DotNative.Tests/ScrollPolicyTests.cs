using DotNative;

internal static class ScrollPolicyTests
{
    public static void Run()
    {
        var mode = new State<NestedScrollMode>(NestedScrollMode.Contain);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() =>
                new ListView<int>([1, 2], i => new Text(i.ToString()), i => i.ToString())
                {
                    NestedScrolling = mode.Value,
                }
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Nodes.Single(x => x.Value.Kind == 4).Key;
        if (native.Nodes[id].ScrollMode != 1)
            throw new Exception("Contain must be the default.");
        mode.Value = NestedScrollMode.Chain;
        dispatcher.Drain();
        if (native.Nodes[id].ScrollMode != 0)
            throw new Exception("Policy update lost identity.");
        if (new ScrollView(new Text("x")).NestedScrolling != NestedScrollMode.Contain)
            throw new Exception("Unexpected default.");
        try
        {
            new CommandBuffer().SetScroll(1, (NestedScrollMode)99);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new Exception("Invalid scroll mode accepted.");
    }
}
