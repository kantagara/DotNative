using System.Buffers.Binary;
using DotNative;
using DotNative.Plugins;

internal static class PluginTests
{
    public static void Run()
    {
        Check(
            NamedChannels.GetId("dotnative.fixture/service").Value
                == System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("dotnative.fixture/service/v1")
                    )
                )
        );
        Check(NamedChannels.GetId("channel", 1) != NamedChannels.GetId("channel", 2));
        foreach (
            var length in new[] { 1, 51, 52, 53, 55, 56, 60, 61, 63, 64, 65, 127, 128, 129, 1024 }
        )
        foreach (var version in new[] { 1u, uint.MaxValue })
        {
            var name = new string('x', length) + "č😀";
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{name}/v{version}")
                )
            );
            Check(NamedChannels.GetId(name, version).Value == expected);
        }

        Check(
            PluginCodec.Encode(0.125).SequenceEqual(new byte[] { 8, 0, 0, 0, 0, 0, 0, 0xc0, 0x3f })
        );
        Check(Equals(PluginCodec.Decode(PluginCodec.Encode(0.125f)), 0.125));
        Check(double.IsNaN((double)PluginCodec.Decode(PluginCodec.Encode(double.NaN))!));
        Reject(() => PluginCodec.Decode([8, 0]));
        var values = new Dictionary<string, object?>
        {
            ["name"] = "Dokument čćž 😄",
            ["bytes"] = new byte[] { 0, 255 },
            ["values"] = new object?[] { null, true, false, long.MinValue, long.MaxValue },
        };
        var compact = PluginArgs.Create(
            ("name", values["name"]), ("bytes", values["bytes"]), ("values", values["values"]));
        Check(PluginCodec.Encode(compact).SequenceEqual(PluginCodec.Encode(values)));
        Check(PluginCodec.Encode(PluginArgs.Create()).SequenceEqual(
            PluginCodec.Encode(new Dictionary<string, object?>())));
        var duplicateRejected = false;
        try { PluginArgs.Create(("key", "a"), ("key", "b")); }
        catch (ArgumentException) { duplicateRejected = true; }
        Check(duplicateRejected);
        var decoded = (Dictionary<string, object?>)PluginCodec.Decode(PluginCodec.Encode(values))!;
        Check(Equals(decoded["name"], values["name"]));
        Check(((byte[])decoded["bytes"]!).SequenceEqual((byte[])values["bytes"]!));
        Reject(() => PluginCodec.Decode([0, 0]));
        Reject(() => PluginCodec.Decode([4, 1, 0, 0, 0, 255]));
        Reject(() => PluginCodec.Decode([5, 255, 255, 255, 127]));
        Reject(() => PluginCodec.Encode(new byte[PluginCodec.MaximumBytes]));
        Reject(() => PluginCodec.Encode(new string('x', PluginCodec.MaximumBytes)));
        object? nested = null;
        for (int i = 0; i < 18; i++)
            nested = new object?[] { nested };
        Reject(() => PluginCodec.Encode(nested));

        var dispatcher = new Dispatcher();
        var transport = new Transport();
        using var channels = new PlatformChannels(dispatcher, transport);
        var channel = channels.Get(new(42));
        // A native click must establish a UI synchronization context before await.
        var state = new State<string>("before");
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async void Click()
        {
            try
            {
                state.Value = (string)(await channel.InvokeAsync("pick"))!;
                finished.SetResult();
            }
            catch (Exception error)
            {
                finished.SetException(error);
            }
        }
        var model = new NativeModel();
        using (
            var host = new RenderHost(
                new Factory(() => new Button(state.Value, Click)),
                dispatcher,
                model
            )
        )
        {
            host.RenderNow();
            host.DispatchClick(model.Nodes.Single().Key);
            dispatcher.Drain();
            channels.AcceptReply(Reply(transport.Packets.Last(), 0, "after"));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!finished.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                dispatcher.Drain();
                Thread.Sleep(1);
            }
            finished.Task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            Check(state.Value == "after");
        }
        transport.Packets.Clear();
        var first = channel.InvokeAsync("pick");

        Check(transport.Packets.Count == 0); // Native dispatch must use the UI dispatcher.
        dispatcher.Drain();
        var request = transport.Packets.Last();
        var call = (Dictionary<string, object?>)PluginCodec.Decode(request.AsSpan(16))!;
        Check(Equals(call["method"], "pick"));
        channels.AcceptReply(Reply(request, 0, "selected"));
        Check(Equals(Await(first), "selected"));
        channels.AcceptReply(Reply(request, 0, "late duplicate"));

        var failed = channel.InvokeAsync("unknown");
        dispatcher.Drain();
        channels.AcceptReply(
            Reply(
                transport.Packets.Last(),
                1,
                new Dictionary<string, object?>
                {
                    ["code"] = "not_implemented",
                    ["message"] = "Unknown method",
                }
            )
        );
        try
        {
            Await(failed);
            throw new Exception("Expected plugin error");
        }
        catch (PluginException error)
        {
            Check(error.Code == "not_implemented");
        }

        using var cancellation = new CancellationTokenSource();
        var cancelled = channel.InvokeAsync("pick", cancellationToken: cancellation.Token);
        dispatcher.Drain();
        var cancelledRequest = transport.Packets.Last();
        cancellation.Cancel();
        dispatcher.Drain();
        Check(BinaryPrimitives.ReadUInt32LittleEndian(transport.Packets.Last().AsSpan(12)) == 2);
        try
        {
            Await(cancelled);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        channels.AcceptReply(Reply(cancelledRequest, 0, "late reply"));

        var waiting = Enumerable
            .Range(0, 256)
            .Select(_ => channel.InvokeAsync("pending"))
            .ToArray();
        try
        {
            Await(channel.InvokeAsync("over limit"));
            throw new Exception("Expected backpressure");
        }
        catch (PluginException error)
        {
            Check(error.Code == "busy");
        }
        channels.Dispose();
        foreach (var task in waiting)
        {
            try
            {
                Await(task);
                throw new Exception("Expected disposal");
            }
            catch (ObjectDisposedException) { }
        }
        dispatcher.Drain();
        using var unavailable = new PlatformChannels(dispatcher, null);
        try
        {
            Await(unavailable.Get(new(42)).InvokeAsync("pick"));
            throw new Exception("Expected unavailable");
        }
        catch (PluginException error)
        {
            Check(error.Code == "unavailable");
        }
    }

    private static object? Await(Task<object?> task) =>
        task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

    private static byte[] Reply(byte[] request, uint status, object? value)
    {
        var payload = PluginCodec.Encode(value);
        var result = new byte[12 + payload.Length];
        request.AsSpan(0, 8).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), status);
        payload.CopyTo(result, 12);
        return result;
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
            when (e
                    is InvalidDataException
                        or System.Text.DecoderFallbackException
                        or EndOfStreamException
            )
        {
            return;
        }
        throw new Exception("Invalid plugin payload accepted");
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("Plugin contract failed");
    }

    private sealed class Transport : IPluginTransport
    {
        public List<byte[]> Packets { get; } = [];

        public void SendPlugin(ReadOnlySpan<byte> message) => Packets.Add(message.ToArray());
    }
}
