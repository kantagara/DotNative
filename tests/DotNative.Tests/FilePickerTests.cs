using System.Buffers.Binary;
using DotNative;
using DotNative.FilePicker;
using DotNative.Plugins;
using Microsoft.Extensions.DependencyInjection;

internal static class FilePickerTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var transport = new PickerTransport();
        using var channels = new PlatformChannels(dispatcher, transport);
        transport.Channels = channels;
        using var services = new ServiceCollection()
            .AddSingleton<IPlatformChannels>(channels)
            .AddFilePicker()
            .BuildServiceProvider();
        var picker = services.GetRequiredService<IFilePicker>();
        using var cancellation = new CancellationTokenSource();
        var pending = picker.PickAsync(cancellation.Token);
        dispatcher.Drain();
        // Native success was queued just before the cancellation message arrives.
        cancellation.Cancel();
        transport.CompletePick();
        try
        {
            Pump(pending, dispatcher);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Check(transport.Released == 1);

        var selection = picker.PickAsync();
        dispatcher.Drain();
        transport.CompletePick();
        var file = Pump(selection, dispatcher)!;
        Check(file.Name == "sample.txt" && file.Length == 3);
        var stream = Pump(file.OpenReadAsync(), dispatcher);
        var buffer = new byte[8];
        int count = Pump(stream.ReadAsync(buffer).AsTask(), dispatcher);
        Check(count == 3 && buffer.AsSpan(0, count).SequenceEqual(new byte[] { 1, 2, 3 }));
        Pump(stream.DisposeAsync().AsTask(), dispatcher);
        Pump(file.DisposeAsync().AsTask(), dispatcher);
        Check(transport.Closed == 1 && transport.Released == 2);
    }

    private static T Pump<T>(Task<T> task, Dispatcher dispatcher)
    {
        Pump((Task)task, dispatcher);
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(Task task, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            dispatcher.Drain();
            Thread.Sleep(1);
        }
        task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("File picker ownership contract failed");
    }

    private sealed class PickerTransport : IPluginTransport
    {
        public PlatformChannels Channels = null!;
        private byte[]? _pick;
        public int Released,
            Closed;

        public void SendPlugin(ReadOnlySpan<byte> message)
        {
            var packet = message.ToArray();
            var call = (Dictionary<string, object?>)PluginCodec.Decode(message[16..])!;
            switch (call["method"])
            {
                case "pick":
                    _pick = packet;
                    break;
                case "cancelPick":
                    Reply(packet, null);
                    break;
                case "openRead":
                    Reply(packet, 2L);
                    break;
                case "read":
                    Reply(packet, new byte[] { 1, 2, 3 });
                    break;
                case "closeStream":
                    Closed++;
                    Reply(packet, null);
                    break;
                case "releaseFile":
                    Released++;
                    Reply(packet, null);
                    break;
                default:
                    throw new Exception("Unexpected method");
            }
        }

        public void CompletePick() =>
            Reply(
                _pick!,
                new Dictionary<string, object?>
                {
                    ["handle"] = 1L,
                    ["name"] = "sample.txt",
                    ["length"] = 3L,
                }
            );

        private void Reply(byte[] request, object? value)
        {
            var payload = PluginCodec.Encode(value);
            var reply = new byte[12 + payload.Length];
            request.AsSpan(0, 8).CopyTo(reply);
            BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(8), 0);
            payload.CopyTo(reply, 12);
            Channels.AcceptReply(reply);
        }
    }
}
