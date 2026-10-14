using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNative;

public interface ICommandTransport
{
    PresentationTarget Target => PresentationTarget.Local;

    // Synchronous borrowed span. Implementations must consume it before returning.
    void Flush(ReadOnlySpan<byte> commands);
}

public sealed class NativeTransport : ICommandTransport, Plugins.IPluginTransport
{
    public void ResetPlugins() => NativeMethods.ResetPlugins();

    public unsafe void SendPlugin(ReadOnlySpan<byte> message)
    {
        fixed (byte* bytes = message)
            if (NativeMethods.SendPlugin(bytes, (nuint)message.Length) != 0)
                throw new Plugins.PluginException(
                    "transport",
                    "Native plugin request was rejected."
                );
    }

    public unsafe void DispatchPluginReplies(Plugins.PlatformChannels channels)
    {
        for (int i = 0; i < 256; i++)
        {
            nuint size;
            fixed (byte* bytes = _events)
                size = NativeMethods.PollPlugin(bytes, (nuint)_events.Length);
            if (size == 0)
                return;
            if (size > Plugins.PluginCodec.MaximumBytes + 12)
                throw new InvalidDataException("Invalid plugin reply size.");
            if (size > (nuint)_events.Length)
            {
                Array.Resize(ref _events, (int)size);
                i--;
                continue;
            }
            channels.AcceptReply(_events.AsSpan(0, (int)size));
        }
    }

    private byte[] _events = new byte[4096];

    public unsafe void DispatchPendingEvents(RenderHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        for (int i = 0; i < 1024; ++i)
        {
            nuint required;
            fixed (byte* pointer = _events)
                required = NativeMethods.PollEvent(pointer, (nuint)_events.Length);
            if (required == 0)
                return;
            if (required > CommandBuffer.MaximumBytes || required < 16)
                throw new InvalidOperationException("Invalid native event length.");
            if (required > (nuint)_events.Length)
            {
                Array.Resize(ref _events, (int)required);
                --i;
                continue;
            }
            DispatchEvent(_events.AsSpan(0, checked((int)required)), host);
        }
    }

    /// <summary>Decode one native event for an alternate transport on its UI dispatcher.</summary>
    public static void DispatchEvent(ReadOnlySpan<byte> data, RenderHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (data.Length < 16 || data.Length > CommandBuffer.MaximumBytes)
            throw new InvalidOperationException("Invalid native event length.");
        uint id = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
        var kind = (NativeEventKind)
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        float value = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data[8..]);
        uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (length != data.Length - 16 || !float.IsFinite(value) || !Enum.IsDefined(kind))
            throw new InvalidOperationException("Invalid native event.");
        if (
            kind
                is NativeEventKind.FocusChanged
                    or NativeEventKind.PressedChanged
                    or NativeEventKind.HoverChanged
            && value is not (0 or 1)
        )
            throw new InvalidOperationException("Invalid native interaction state.");
        host.DispatchInput(
            id,
            kind,
            value,
            new System.Text.UTF8Encoding(false, true).GetString(data[16..])
        );
    }

    public unsafe void Flush(ReadOnlySpan<byte> commands)
    {
        if (commands.IsEmpty)
            return;
        // Rust borrows exactly commands.Length bytes during this call. Rust must
        // copy strings/properties into owned storage before returning, and must
        // never retain this pointer or let a panic unwind through this boundary.
        fixed (byte* pointer = commands)
            NativeMethods.FlushCommands(pointer, (nuint)commands.Length);
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("core", EntryPoint = "dotnative_plugins_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ResetPlugins();

    [LibraryImport("core", EntryPoint = "dotnative_send_plugin")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int SendPlugin(byte* bytes, nuint length);

    [LibraryImport("core", EntryPoint = "dotnative_poll_plugin")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial nuint PollPlugin(byte* bytes, nuint length);

    [LibraryImport("core", EntryPoint = "dotnative_poll_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial nuint PollEvent(byte* buffer, nuint capacity);

    [LibraryImport("core", EntryPoint = "flush_commands")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial void FlushCommands(byte* buffer, nuint len);
    // Deliberately no SuppressGCTransition: layout/native control work may be long.
    // NativeAOT static hosts must configure direct linking for this import.
}
