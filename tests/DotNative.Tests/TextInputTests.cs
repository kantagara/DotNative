using System.Buffers.Binary;
using System.Text;
using DotNative;

internal static class TextInputTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        string? submitted = null;
        var state = new State<TextField>(
            new TextField("Živeli 🙂", _ => { })
            {
                Input = new()
                {
                    IsPassword = true,
                    Action = TextInputAction.Done,
                    Autofill = TextAutofill.Password,
                    Autocorrect = false,
                },
                OnSubmitted = value => submitted = value,
            }
        );
        using var host = new RenderHost(
            new Factory(() => state.Value.WithKey("editor")),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Nodes.Single().Key;
        Check(native.Nodes[id].TextInput.SequenceEqual(new byte[] { 0, 1, 0, 2, 1 }));
        Submit(host, id, "Živeli 🙂");
        Check(submitted == "Živeli 🙂");
        state.Value = state.Value with
        {
            Input = TextInputOptions.Default,
            OnSubmitted = value => submitted = "fresh:" + value,
        };
        dispatcher.Drain();
        Check(native.Nodes.Single().Key == id);
        Check(native.Nodes[id].TextInput.SequenceEqual(new byte[] { 0, 0, 0, 0, 4 }));
        Submit(host, id, "new");
        Check(submitted == "fresh:new");
        state.Value = state.Value with
        {
            Input = new() { PlaceholderColor = new Color(197, 192, 204, 255) },
        };
        dispatcher.Drain();
        Check(native.Nodes.Single().Key == id);
        Check(
            native
                .Nodes[id]
                .TextInput.SequenceEqual(new byte[] { 0, 0, 0, 0, 4, 197, 192, 204, 255 })
        );
        state.Value = state.Value with { Input = TextInputOptions.Default };
        dispatcher.Drain();
        Check(native.Nodes[id].TextInput.SequenceEqual(new byte[] { 0, 0, 0, 0, 4 }));
        state.Value = state.Value.Enabled(false);
        dispatcher.Drain();
        Submit(host, id, "disabled");
        Check(submitted == "fresh:new");
        state.Value = (
            state.Value with
            {
                Input = new() { IsMultiline = true, Action = TextInputAction.Send },
            }
        ).Enabled(true);
        dispatcher.Drain();
        var multilineId = native.Nodes.Single().Key;
        Check(multilineId != id && !native.Nodes.ContainsKey(id));
        Submit(host, id, "late");
        Check(submitted == "fresh:new");
        Submit(host, multilineId, "line1\nline2");
        Check(submitted == "fresh:line1\nline2");
        state.Value = state.Value with { Input = TextInputOptions.Default };
        dispatcher.Drain();
        Check(native.Nodes.Single().Key != multilineId);
        foreach (
            var invalid in new TextInputOptions[]
            {
                new() { Keyboard = (TextKeyboard)99 },
                new() { Action = (TextInputAction)99 },
                new() { Capitalization = (TextCapitalization)99 },
                new() { Autofill = (TextAutofill)99 },
                new() { IsPassword = true, IsMultiline = true },
                new() { IsMultiline = true, Keyboard = TextKeyboard.Number },
            }
        )
        {
            var buffer = new CommandBuffer();
            try
            {
                buffer.SetTextInput(1, invalid);
                throw new Exception("Invalid input accepted.");
            }
            catch (ArgumentException)
            {
                Check(buffer.IsEmpty);
            }
        }
        host.Dispose();
        try
        {
            Submit(host, id, "disposed");
            throw new Exception("Disposed host accepted input.");
        }
        catch (ObjectDisposedException) { }
        Check(submitted == "fresh:line1\nline2");
    }

    private static void Submit(RenderHost host, uint id, string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var data = new byte[16 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 9);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)text.Length);
        text.CopyTo(data, 16);
        NativeTransport.DispatchEvent(data, host);
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Text input contract failed.");
    }
}
