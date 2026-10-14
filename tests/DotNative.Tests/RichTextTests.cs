using System.Buffers.Binary;
using DotNative;

internal static class RichTextTests
{
    public static void Run()
    {
        TextRun[] runs =
        [
            new("Živeli 🙂 ", Weight: FontWeight.Bold),
            new("svete", FontSize: 30, Foreground: Color.Red),
        ];
        var rich = Text.Rich(runs);
        runs[0] = new("mutated");
        if (rich.Value != "Živeli 🙂 svete")
            throw new Exception("Mutable runs");
        var state = new State<Text>(rich);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(new Factory(() => state.Value), dispatcher, native);
        host.RenderNow();
        var id = native.Root;
        if (BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[id].TextRuns) != 2)
            throw new Exception("Run count");
        state.Value = new Text(rich.Value);
        dispatcher.Drain();
        if (
            native.Root != id
            || BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[id].TextRuns) != 0
        )
            throw new Exception("Run reset");
        var buffer = new CommandBuffer();
        try
        {
            buffer.SetTextRuns(1, [new("bad", FontSize: float.NaN)]);
            throw new Exception("Invalid run");
        }
        catch (ArgumentOutOfRangeException)
        {
            if (!buffer.IsEmpty)
                throw new Exception("Partial write");
        }
    }
}
