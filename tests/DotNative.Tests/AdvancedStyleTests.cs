using System.Buffers.Binary;
using DotNative;

internal static class AdvancedStyleTests
{
    public static void Run()
    {
        GridTrack[] tracks = [GridTrack.Points(40), GridTrack.Fraction(2)];
        var definition = new GridDefinition(tracks);
        tracks[0] = GridTrack.Auto;
        Check(definition.Columns[0] == GridTrack.Points(40));
        GradientStop[] stops = [new(0, Color.Red), new(1, Color.Black)];
        var gradient = Gradient.Linear(new(0, 0), new(1, 1), stops);
        stops[0] = new(0, Color.White);
        Check(gradient.Stops[0].Color == Color.Red);
        Check(
            gradient.Equals(
                Gradient.Linear(new(0, 0), new(1, 1), new(0, Color.Red), new(1, Color.Black))
            )
        );
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var plain = new Container(new Text("advanced"));
        var state = new State<Container>(
            plain
                .FlowDirection(FlowDirection.RightToLeft)
                .PaddingInline(12, 4)
                .MarginInline(-2, 6)
                .GridColumn(2, 2)
                .GridRow(3)
                .BackgroundGradient(gradient)
        );
        using var host = new RenderHost(
            new Factory(() => new Grid(definition, state.Value).RowGap(8).ColumnGap(12)),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Nodes[native.Root].Children[0];
        var wire = native.Nodes[id].AdvancedLayout;
        Check(wire[0] == 2 && wire[36] == 15);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(38)) == 2);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(40)) == 2);
        var paint = native.Nodes[id].Gradient;
        Check(paint.Length == 34 && paint[0] == 1 && paint[1] == 2);
        Check(BinaryPrimitives.ReadSingleLittleEndian(paint.AsSpan(26)) == 1);
        state.Value = plain;
        dispatcher.Drain();
        Check(native.Nodes[native.Root].Children[0] == id);
        Check(native.Nodes[id].AdvancedLayout[0] == 0 && native.Nodes[id].AdvancedLayout[36] == 0);
        Check(native.Nodes[id].Gradient[0] == 0 && native.Nodes[id].Gradient.Length == 18);
        Reject(() => new GridDefinition([new(GridTrackUnit.Fraction, 0)]));
        Reject(() => new GridDefinition(Enumerable.Repeat(GridTrack.Auto, 129)));
        Reject(() => plain.GridColumn(128, 2));
        Reject(() => plain.PaddingInline(-1, 0));
        Reject(() => new VStack().RowGap(float.NaN));
        Reject(() => Gradient.Linear(new(0, 0), new(0, 0), new(0, Color.Black), new(1, Color.Red)));
        Reject(() => Gradient.Radial(new(.5f, .5f), 0, new(0, Color.Black), new(1, Color.Red)));
        Reject(() => Gradient.Conic(new(.5f, .5f), new(0, Color.Black), new(.5f, Color.Red)));
        var buffer = new CommandBuffer();
        Reject(() =>
            buffer.SetAdvancedLayout(1, AdvancedLayoutStyle.Default with { ColumnSpan = 0 })
        );
        Check(buffer.IsEmpty);
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Advanced style contract failed.");
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
        throw new Exception("Invalid advanced style accepted.");
    }
}
