using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// Payload of the <c>SteamNetConnectionStatusChanged</c> callback. Maps to
/// <c>SteamNetConnectionStatusChangedCallback_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct StatusInfo
{
    public uint connection;
    public ConnectionInfo connectionInfo;
    public ConnectionState oldState;
}

/// <summary>
/// Connection metadata. Maps 1:1 to <c>SteamNetConnectionInfo_t</c>.
/// </summary>
/// <remarks>
/// <c>m_nFlags</c> was added after the original ValveSockets-CSharp wrapper
/// was written; including it here keeps subsequent struct offsets correct.
/// The trailing reserved array is <c>uint32 reserved[63]</c> upstream. The
/// layout works out because <see cref="Address"/> is declared <c>Pack = 1</c>
/// to mirror the native <c>#pragma pack(1)</c> on <c>SteamNetworkingIPAddr</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ConnectionInfo
{
    public NetworkingIdentity identity;
    public long userData;
    public uint listenSocket;
    public Address address;
    private readonly ushort _pad1;
    private readonly uint _popRemote;
    private readonly uint _popRelay;
    public ConnectionState state;
    public int endReason;

    public EndDebugBuffer endDebug;
    public ConnectionDescriptionBuffer connectionDescription;

    public int flags;
    private ReservedBuffer _reserved;

    public readonly string EndDebug => InlineUtf8.ToString(endDebug);
    public readonly string ConnectionDescription => InlineUtf8.ToString(connectionDescription);

    [InlineArray(Library.MaxCloseMessageLength)]
    public struct EndDebugBuffer { private byte _element0; }

    [InlineArray(Library.MaxConnectionDescriptionLength)]
    public struct ConnectionDescriptionBuffer { private byte _element0; }

    [InlineArray(63)]
    internal struct ReservedBuffer { private uint _element0; }
}

/// <summary>Real-time connection telemetry. Maps to <c>SteamNetConnectionRealTimeStatus_t</c>.</summary>
/// <remarks>
/// <c>m_usecMaxJitter</c> was added in the multi-lane API. The reserved
/// array dropped from <c>[16]</c> to <c>[15]</c> uint32s to keep the total
/// struct size unchanged.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ConnectionStatus
{
    public ConnectionState state;
    public int ping;
    public float connectionQualityLocal;
    public float connectionQualityRemote;
    public float outPacketsPerSecond;
    public float outBytesPerSecond;
    public float inPacketsPerSecond;
    public float inBytesPerSecond;
    public int sendRateBytesPerSecond;
    public int pendingUnreliable;
    public int pendingReliable;
    public int sentUnackedReliable;
    public long queueTime;
    public int maxJitter;
    private ReservedBuffer _reserved;

    [InlineArray(15)]
    internal struct ReservedBuffer { private uint _element0; }
}

/// <summary>Per-lane real-time telemetry. Maps to <c>SteamNetConnectionRealTimeLaneStatus_t</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ConnectionLaneStatus
{
    public int pendingUnreliable;
    public int pendingReliable;
    public int sentUnackedReliable;
    private readonly int _reserved;
    public long queueTime;
    private ReservedBuffer _reservedTail;

    [InlineArray(10)]
    internal struct ReservedBuffer { private uint _element0; }
}

/// <summary>
/// Payload of the global authentication-status-changed callback. Maps to
/// <c>SteamNetAuthenticationStatus_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AuthenticationStatus
{
    public Availability availability;
    public DebugMsgBuffer debugMsg;

    public readonly string DebugMessage => InlineUtf8.ToString(debugMsg);

    [InlineArray(256)]
    public struct DebugMsgBuffer { private byte _element0; }
}

/// <summary>
/// Payload of the global relay-network-status-changed callback. Maps to
/// <c>SteamRelayNetworkStatus_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RelayNetworkStatus
{
    public Availability availability;
    public int pingMeasurementInProgress;
    public Availability availabilityNetworkConfig;
    public Availability availabilityAnyRelay;
    public DebugMsgBuffer debugMsg;

    public readonly string DebugMessage => InlineUtf8.ToString(debugMsg);

    [InlineArray(256)]
    public struct DebugMsgBuffer { private byte _element0; }
}

internal static class InlineUtf8
{
    /// <summary>Decode a NUL-terminated UTF-8 inline buffer (e.g., <c>char m_szEndDebug[128]</c>) into a managed string.</summary>
    public static string ToString<TBuffer>(TBuffer buffer) where TBuffer : struct
    {
        unsafe
        {
            int size = Unsafe.SizeOf<TBuffer>();
            ReadOnlySpan<byte> span = new(Unsafe.AsPointer(ref buffer), size);
            int nul = span.IndexOf((byte)0);
            if (nul >= 0)
                span = span[..nul];
            return System.Text.Encoding.UTF8.GetString(span);
        }
    }
}
