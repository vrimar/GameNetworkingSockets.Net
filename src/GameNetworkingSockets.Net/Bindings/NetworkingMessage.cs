using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// Received message envelope. Maps 1:1 to <c>SteamNetworkingMessage_t</c>.
/// Pointers to this struct are returned by
/// <see cref="NetworkingSockets.ReceiveMessagesOnConnection"/> and
/// <see cref="NetworkingSockets.ReceiveMessagesOnPollGroup"/>; the caller is
/// responsible for releasing each one via <see cref="Release"/>.
/// </summary>
/// <remarks>
/// Field order matches the C++ struct exactly. <c>m_idxLane</c> and
/// <c>_pad1__</c> were added in the multi-lane API; both must be present to
/// keep the size and layout in sync with the native struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public partial struct NetworkingMessage
{
    /// <summary>Pointer to the message payload (length <see cref="length"/> bytes).</summary>
    public nint data;
    public int length;
    public uint connection;
    public NetworkingIdentity identity;
    public long connectionUserData;
    public long timeReceived;
    public long messageNumber;
    internal nint freeData;
    internal nint release;
    public int channel;
    public int flags;
    public long userData;
    public ushort idxLane;
    private ushort _pad1__;

    /// <summary>Copies the payload into <paramref name="destination"/>. Does not release the message.</summary>
    public readonly void CopyTo(Span<byte> destination)
    {
        if (destination.Length < length)
            throw new ArgumentException($"Destination too small ({destination.Length} < {length}).", nameof(destination));

        unsafe
        {
            new ReadOnlySpan<byte>((void*)data, length).CopyTo(destination);
        }
    }

    /// <summary>Returns a view over the payload. Lifetime is bounded by the native message; copy before releasing.</summary>
    public readonly unsafe ReadOnlySpan<byte> AsSpan() => new((void*)data, length);

    /// <summary>
    /// Releases the native message. Call exactly once per pointer returned by
    /// the native <c>ReceiveMessagesOn*</c> APIs. The
    /// <see cref="NetworkingSockets.ReceiveMessagesOnConnection"/> overloads
    /// in this binding already release messages internally after invoking the
    /// callback.
    /// </summary>
    public static void Release(nint nativeMessage)
    {
        if (nativeMessage != 0)
            SteamAPI_SteamNetworkingMessage_t_Release(nativeMessage);
    }

    [LibraryImport(Library.LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingMessage_t_Release(nint nativeMessage);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void StatusCallback(ref StatusInfo info);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DebugCallback(DebugType type, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void AuthenticationStatusCallback(ref AuthenticationStatus status);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void RelayNetworkStatusCallback(ref RelayNetworkStatus status);

public delegate void MessageCallback(in NetworkingMessage message);
