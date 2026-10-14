using DotNative;

internal static class ImageAndLayerTests
{
    public static void Run()
    {
        var source = ImageSource.FromBytes([1, 2, 3]); // This transport tests bytes, not native decoding.
        var plain = new Image(source);
        var state = new State<Image>(
            plain.Fit(ImageFit.Cover).TintColor(new Color(10, 20, 30, 128)).ZIndex(-10)
        );
        var background = new State<bool>(false);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() =>
                new VStack(state.Value).BackgroundImage(background.Value ? source : null)
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Nodes[native.Root].Children.Single();
        Check(native.Nodes[id].ImageFit == 1 && native.Nodes[id].ZIndex == -10);
        Check(native.Nodes[id].ImageTint.SequenceEqual(new byte[] { 1, 10, 20, 30, 128 }));
        Check(plain.ImageFit == ImageFit.Contain && plain.ImageTint is null && plain.ZIndex == 0);
        state.Value = plain;
        dispatcher.Drain();
        Check(native.Nodes[native.Root].Children.Single() == id);
        Check(
            native.Nodes[id].ImageFit == 0
                && native.Nodes[id].ImageTint[0] == 0
                && native.Nodes[id].ZIndex == 0
        );
        foreach (var fit in Enum.GetValues<ImageFit>())
        {
            state.Value = plain.Fit(fit).ZIndex(int.MaxValue);
            dispatcher.Drain();
            Check(
                native.Nodes[id].ImageFit == (byte)fit && native.Nodes[id].ZIndex == int.MaxValue
            );
        }
        background.Value = true;
        dispatcher.Drain();
        Check(
            native
                .Nodes[native.Root]
                .BackgroundImage.SequenceEqual(new byte[] { 1, 3, 0, 0, 0, 1, 2, 3 })
        );
        Check(native.Nodes[native.Root].Children.Single() == id);
        background.Value = false;
        dispatcher.Drain();
        Check(
            native.Nodes[native.Root].BackgroundImage.Length == 5
                && native.Nodes[native.Root].BackgroundImage[1] == 0
        );
        var buffer = new CommandBuffer();
        try
        {
            buffer.SetImageFit(1, (ImageFit)255);
            throw new Exception("Invalid fit accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
            Check(buffer.IsEmpty);
        }
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Image/layer style contract failed.");
    }
}
