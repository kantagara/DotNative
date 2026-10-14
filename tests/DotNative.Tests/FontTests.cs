using System.Buffers.Binary;
using DotNative;

internal static class FontTests
{
    public static void Run()
    {
        var bytes = new byte[12];
        var font = FontSource.FromBytes("Test", bytes);
        bytes[0] = 255;
        Check(font.Data[0] == 0);
        using var stream = new MemoryStream(bytes);
        var streamed = FontSource.FromStreamAsync("Test", stream).GetAwaiter().GetResult();
        Check(stream.CanRead && streamed.Data[0] == 255);
        var input = new[] { new FontAxis("wght", 650), new FontAxis("wdth", 125) };
        var axes = new FontVariationSettings(input);
        input[0] = new("wght", 100);
        Check(axes.Axes[0].Tag == "wdth" && axes.Axes[1].Value == 650);
        Check(axes.Equals(new FontVariationSettings(new("wdth", 125), new("wght", 650))));
        Reject(() => new FontVariationSettings(new FontAxis("wght", float.NaN)));
        Reject(() => new FontVariationSettings(new FontAxis("bad", 1)));
        Reject(() => new FontVariationSettings(new("wght", 1), new("wght", 2)));
        Reject(() => FontSource.FromBytes("Test", new byte[3]));
        var state = new State<Text>(new Text("font").FontSource(font).FontVariations(axes));
        var native = new NativeModel();
        var dispatcher = new Dispatcher();
        using var host = new RenderHost(new Factory(() => state.Value), dispatcher, native);
        host.RenderNow();
        var id = native.Find("font");
        Check(native.Nodes[id].FontAxes[0] == 2);
        Check(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].FontAxes.AsSpan(5)) == 125);
        Check(native.Nodes[id].FontAsset.Length == 24);
        Check(native.Fonts.Count == 1);
        Check(native.Nodes[id].FontAsset.AsSpan(4, 4).SequenceEqual("Test"u8));
        Check(native.Nodes[id].FontAsset.AsSpan(12).SequenceEqual(font.Data));
        // A new FontSource with identical content must reuse the registration.
        state.Value = state.Value.FontSource(FontSource.FromBytes("Test", font.Data));
        dispatcher.Drain();
        Check(native.Fonts.Count == 1 && native.Nodes[id].FontAsset.Length == 24);
        state.Value = state.Value.FontSource(null).FontVariations(null);
        dispatcher.Drain();
        Check(native.Find("font") == id && native.Nodes[id].FontAxes[0] == 0);
        Check(native.Nodes[id].FontAsset.SequenceEqual(new byte[8]));
        state.Value = state.Value.FontSource(font);
        dispatcher.Drain();
        Check(native.Fonts.Count == 1 && native.Nodes[id].FontAsset.Length == 24);
        Check(new Text("x").FontSource(font).FontFamily("Other").FontAsset is null);
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
        throw new Exception("Invalid font input accepted");
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Font contract failed");
    }
}
