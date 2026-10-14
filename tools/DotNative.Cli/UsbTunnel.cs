using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

// macOS usbmuxd control messages use plist; application traffic remains the
// unchanged binary DotNative protocol. The selected device must be USB-connected.
internal static class UsbTunnel
{
    public static async Task Relay(string udid, int port, CancellationToken cancellation)
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(45));
        NetworkStream? usb = null;
        while (usb is null)
        {
            connectTimeout.Token.ThrowIfCancellationRequested();
            try
            {
                usb = await Connect(udid, port, connectTimeout.Token);
            }
            catch (IOException)
            {
                await Task.Delay(300, connectTimeout.Token);
            }
        }
        using (usb)
        using (var host = new TcpClient())
        {
            await host.ConnectAsync("127.0.0.1", port, cancellation);
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var outgoing = host.GetStream().CopyToAsync(usb, transfer.Token);
            var incoming = usb.CopyToAsync(host.GetStream(), transfer.Token);
            Console.WriteLine("USB preview connected.");
            await Task.WhenAny(outgoing, incoming);
            transfer.Cancel();
            host.Close();
            usb.Close();
            try
            {
                await Task.WhenAll(outgoing, incoming);
            }
            catch (Exception error)
                when (error is OperationCanceledException or IOException or ObjectDisposedException)
            { }
        }
    }

    private static async Task<NetworkStream> Open(CancellationToken cancellation)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint("/var/run/usbmuxd"),
                cancellation
            );
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static XElement Value(XElement dict, string key) =>
        dict.Elements("key").Single(x => x.Value == key).ElementsAfterSelf().First();

    private static async Task<NetworkStream> Connect(
        string udid,
        int port,
        CancellationToken cancellation
    )
    {
        int deviceId;
        using (var discovery = await Open(cancellation))
        {
            var list = await Request(discovery, "ListDevices", [], cancellation);
            var device = Value(list, "DeviceList")
                .Elements("dict")
                .FirstOrDefault(d =>
                {
                    var properties = Value(d, "Properties");
                    return Value(properties, "SerialNumber").Value == udid
                        && Value(properties, "ConnectionType").Value == "USB";
                });
            if (device is null)
                throw new IOException("Selected iPhone is not connected over USB.");
            deviceId = int.Parse(Value(device, "DeviceID").Value);
        }
        var stream = await Open(cancellation);
        try
        {
            var response = await Request(
                stream,
                "Connect",
                [
                    new XElement("key", "DeviceID"),
                    new XElement("integer", deviceId),
                    new XElement("key", "PortNumber"),
                    new XElement(
                        "integer",
                        (ushort)System.Net.IPAddress.HostToNetworkOrder((short)port)
                    ),
                ],
                cancellation
            );
            if (Value(response, "Number").Value != "0")
                throw new IOException("iPhone preview port is not ready.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static async Task<XElement> Request(
        Stream stream,
        string type,
        XElement[] values,
        CancellationToken cancellation
    )
    {
        var dict = new XElement(
            "dict",
            new XElement("key", "MessageType"),
            new XElement("string", type),
            new XElement("key", "ClientVersionString"),
            new XElement("string", "DotNative"),
            new XElement("key", "ProgName"),
            new XElement("string", "dotnative"),
            values
        );
        var body = Encoding.UTF8.GetBytes(
            new XDocument(new XElement("plist", new XAttribute("version", "1.0"), dict)).ToString()
        );
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(body, cancellation);
        await stream.ReadExactlyAsync(header, cancellation);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (
            length < 16
            || length > 4 * 1024 * 1024
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != 8
        )
            throw new IOException("Invalid usbmuxd reply.");
        var reply = new byte[length - 16];
        await stream.ReadExactlyAsync(reply, cancellation);
        return XDocument.Parse(Encoding.UTF8.GetString(reply)).Root!.Element("dict")!;
    }
}
