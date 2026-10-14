using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using DotNative.Hosting;

namespace DotNative.Development;

internal static class Program
{
    private const int Limit = 16 * 1024 * 1024;

    public static void Main(string[] args)
    {
        bool singleSession = args.Contains("--once");
        args = args.Where(arg => arg != "--once").ToArray();
        var platform =
            args.Length == 1 && Enum.TryParse<NativePlatform>(args[0], true, out var parsed)
                ? parsed
                : NativePlatform.IOS;
        if (platform is NativePlatform.Unknown || !Enum.IsDefined(platform))
            throw new ArgumentException("Expected IOS, Android, MacOS, Windows or Linux.");
        var port = 4970 + (int)platform;
        if (Environment.GetEnvironmentVariable("DOTNATIVE_PREVIEW_PORT") is { } configuredPort)
        {
            if (!ushort.TryParse(configuredPort, out var parsedPort) || parsedPort == 0)
                throw new ArgumentException("DOTNATIVE_PREVIEW_PORT must be between 1 and 65535.");
            port = parsedPort;
        }
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start(3);
        }
        catch (SocketException error)
            when (error.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.Error.WriteLine(
                $"DotNative preview port {port} is already in use. Stop the existing preview session or select another DOTNATIVE_PREVIEW_PORT."
            );
            Environment.ExitCode = 1;
            return;
        }
        if (
            Environment.GetEnvironmentVariable("DotNativePreviewReadyFile") is { Length: > 0 } ready
        )
            File.WriteAllText(ready, Environment.ProcessId.ToString());
        Console.WriteLine(
            $"DotNative UI preview: {platform}, {listener.LocalEndpoint}, PID {Environment.ProcessId}"
        );
        Console.WriteLine(
            $"Code updates enabled: {MetadataUpdater.IsSupported}. Channel plugins execute on the native device. Direct native imports are release-only."
        );
        try
        {
            while (true)
            {
                using var client = listener.AcceptTcpClient();
                client.NoDelay = true;
                bool sessionStarted = false;
                try
                {
                    Run(client, platform, out sessionStarted);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(
                        sessionStarted
                            ? $"Preview ended: {error}"
                            : $"Preview connection rejected before handshake: {error.Message}"
                    );
                }
                if (singleSession && sessionStarted)
                    break;
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void Run(TcpClient client, NativePlatform platform, out bool sessionStarted)
    {
        sessionStarted = false;
        using var stream = client.GetStream();
        // A cancelled connect or port probe must not consume --once, and a
        // client that never sends a hello must not block subsequent launches.
        stream.ReadTimeout = 5000;
        var (kind, hello) = Read(stream);
        if (
            kind != 1
            || hello.Length is not (20 or 32)
            || U32(hello, 0) != 1
            || U32(hello, 4) != (uint)platform
        )
            throw new InvalidDataException("Incompatible native preview handshake.");
        stream.ReadTimeout = Timeout.Infinite;
        sessionStarted = true;
        using var dispatcher = new PreviewDispatcher();
        var uiContext = new DispatcherSynchronizationContext(dispatcher);
        using var context = new CancellationTokenSource();
        var transport = new PreviewTransport(stream, platform);
        var session = ApplicationSession.Begin(
            PreviewEntry.Invoke,
            dispatcher,
            transport,
            Float(hello, 8),
            Float(hello, 12),
            (ThemeMode)U32(hello, 16),
            hello.Length == 32
                ? ReadAccessibility(hello.AsSpan(20).ToArray())
                : AccessibilityPreferences.Default
        );
        Exception? failure = null;
        bool connected = true;
        var reader = Task.Run(() =>
        {
            try
            {
                while (!context.IsCancellationRequested)
                {
                    var packet = Read(stream);
                    dispatcher.Post(() => Handle(session, transport, packet.Kind, packet.Body));
                }
            }
            catch (Exception error)
                when (error is IOException or SocketException or ObjectDisposedException)
            {
                dispatcher.Post(() =>
                {
                    failure = error;
                    connected = false;
                });
            }
        });
        Console.WriteLine(
            "Native preview connected. Application entry point is running. "
                + (
                    MetadataUpdater.IsSupported
                        ? "Supported code edits preserve this session."
                        : "Source edits rebuild this session."
                )
        );
        try
        {
            while (connected)
            {
                uiContext.Run(dispatcher.Pump);
                if (session.Startup.IsFaulted)
                    session.Startup.GetAwaiter().GetResult();
            }
        }
        finally
        {
            context.Cancel();
            transport.Disconnect();
            client.Close();
            // Keep pumping the UI context while scoped services perform async cleanup.
            var stopping = session.DisposeAsync().AsTask();
            while (!stopping.IsCompleted)
                uiContext.Run(dispatcher.Pump);
            stopping.GetAwaiter().GetResult();
            reader.GetAwaiter().GetResult();
        }
        if (failure is not null)
            Console.WriteLine("Native preview disconnected; application session disposed.");
    }

    private static void Handle(
        ApplicationSession session,
        PreviewTransport transport,
        uint kind,
        byte[] body
    )
    {
        switch (kind)
        {
            case 9 when body.Length == 0:
                session.MountedHost?.AdvanceAnimationFrame();
                transport.AcknowledgeFrame();
                break;
            case 8 when body.Length == 12:
                session.SetSystemAccessibility(ReadAccessibility(body));
                break;
            case 7:
                session.Channels.AcceptReply(body);
                break;
            case 6 when body.Length == 0:
                transport.ConfirmFrame();
                break;
            case 2 when body.Length == 8:
                session.Resize(Float(body, 0), Float(body, 4));
                break;
            case 3 when body.Length >= 16:
                if (session.MountedHost is { } host)
                    NativeTransport.DispatchEvent(body, host);
                break;
            case 4 when body.Length == 4:
                session.SetSystemTheme((ThemeMode)U32(body, 0));
                break;
            case 5 when body.Length == 4:
                session.SetActivity((ApplicationActivity)U32(body, 0));
                break;
            default:
                throw new InvalidDataException($"Invalid preview message {kind}.");
        }
    }

    private static AccessibilityPreferences ReadAccessibility(byte[] body)
    {
        uint motion = U32(body, 0),
            contrast = U32(body, 8);
        if (motion > 1 || contrast > 1)
            throw new InvalidDataException("Invalid accessibility flags.");
        return new(motion != 0, Float(body, 4), contrast != 0);
    }

    private static uint U32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static float Float(byte[] bytes, int offset)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
        return float.IsFinite(value)
            ? value
            : throw new InvalidDataException("Non-finite native value.");
    }

    private static (uint Kind, byte[] Body) Read(NetworkStream stream)
    {
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (length > Limit)
            throw new InvalidDataException("Preview message exceeds the size limit.");
        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return (BinaryPrimitives.ReadUInt32LittleEndian(header), bytes);
    }
}

internal sealed class PreviewTransport(NetworkStream stream, NativePlatform platform)
    : ICommandTransport,
        DotNative.Plugins.IPluginTransport
{
    private bool _connected = true;
    private readonly Queue<long> frames = new();
    private long displayedRevision = -1;

    public void ConfirmFrame()
    {
        if (!frames.TryDequeue(out var revision))
            throw new InvalidDataException("Native renderer acknowledged an unsent frame.");
        if (revision <= displayedRevision)
            return;
        displayedRevision = revision;
        Console.WriteLine(
            $"[preview] Displayed build {PreviewRevision.BuildId}, managed update {revision} (native frame committed)."
        );
    }

    public void Disconnect() => _connected = false;

    public PresentationTarget Target { get; } = new(platform);

    public void Flush(ReadOnlySpan<byte> commands)
    {
        if (!_connected)
            return;
        frames.Enqueue(PreviewRevision.Current);
        Send(100, commands);
    }

    public void AcknowledgeFrame() => Send(102, []);

    public void SendPlugin(ReadOnlySpan<byte> message)
    {
        if (!_connected)
            throw new DotNative.Plugins.PluginException(
                "disconnected",
                "The native preview disconnected."
            );
        Send(101, message);
    }

    private void Send(uint kind, ReadOnlySpan<byte> commands)
    {
        if (!_connected)
            return;
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, kind);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)commands.Length));
        stream.Write(header);
        stream.Write(commands);
    }
}

internal sealed class PreviewDispatcher : IUiDispatcher, IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly BlockingCollection<Action> _queue = new(1024);

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread;

    public void Post(Action action)
    {
        if (!_queue.TryAdd(action))
            throw new InvalidOperationException("Preview dispatcher queue is full.");
    }

    public void Pump()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("Wrong preview dispatcher thread.");
        if (_queue.TryTake(out var action, 16))
            action();
    }

    public void Dispose() => _queue.Dispose();
}
