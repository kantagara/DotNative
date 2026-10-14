using System.Buffers.Binary;
using DotNative;

internal static class RadioTests
{
    public static void Run()
    {
        var state = new State<string>("system");
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        int changes = 0;
        using var host = new RenderHost(
            new Factory(() =>
                new RadioGroup<string>(
                    state.Value,
                    value =>
                    {
                        changes++;
                        state.Value = value;
                    },
                    new("system", "System"),
                    new("light", "Light"),
                    new("dark", "Dark")
                )
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var darkId = native.Find("Dark");
        var systemId = native.Find("System");
        Require(native.Nodes.Values.Count(node => node.Kind == 12) == 3);
        host.DispatchInput(darkId, NativeEventKind.ValueChanged, 1, "");
        dispatcher.Drain();
        Require(state.Value == "dark" && changes == 1);
        Require(native.Find("Dark") == darkId);
        Require(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[darkId].Control) == 1);
        Require(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[systemId].Control) == 0);
        host.DispatchInput(darkId, NativeEventKind.ValueChanged, 1, "");
        host.DispatchInput(darkId, NativeEventKind.ValueChanged, 0, "");
        dispatcher.Drain();
        Require(changes == 1 && state.Value == "dark");
        Reject(() => new RadioGroup<int>(1, _ => { }, new(1, "one"), new(1, "duplicate")));
        Reject(() => new RadioGroup<int>(2, _ => { }, new RadioOption<int>(1, "one")));
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Radio assertion failed.");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new Exception("Invalid radio group accepted.");
    }
}
