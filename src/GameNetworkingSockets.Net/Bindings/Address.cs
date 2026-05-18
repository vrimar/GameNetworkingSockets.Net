using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// IPv4/IPv6 endpoint accepted by <see cref="NetworkingSockets.CreateListenSocket(ref Address)"/>
/// and friends. Maps 1:1 to <c>SteamNetworkingIPAddr</c>.
/// </summary>
/// <remarks>
/// <para>
/// The underlying C++ struct is 18 bytes (16-byte IPv6 union + 2-byte port)
/// with <c>#pragma pack(1)</c>, so it has 1-byte alignment and <em>no</em>
/// trailing padding. <see cref="Pack"/> = 1 is essential — otherwise the
/// runtime would 2-byte-align <c>port</c> and grow the struct, shifting every
/// field after an embedded <see cref="Address"/> in surrounding structs (most
/// painfully <see cref="ConnectionInfo"/>).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = Size)]
public partial struct Address : IEquatable<Address>
{
    internal const int Size = 18;

    private const string LibraryName = Library.LibraryName;

    /// <summary>The 16-byte IPv6 address (IPv4 is stored as a v4-mapped v6 address).</summary>
    public IpBytes ip;

    /// <summary>Port in host byte order.</summary>
    public ushort port;

    public bool IsLocalHost
    {
        get
        {
            Address self = this;
            return SteamAPI_SteamNetworkingIPAddr_IsLocalHost(ref self);
        }
    }

    public bool IsIPv4
    {
        get
        {
            Address self = this;
            return SteamAPI_SteamNetworkingIPAddr_IsIPv4(ref self);
        }
    }

    public void Clear() => SteamAPI_SteamNetworkingIPAddr_Clear(ref this);

    public void SetLocalHost(ushort port) =>
        SteamAPI_SteamNetworkingIPAddr_SetIPv6LocalHost(ref this, port);

    public void SetIPv4(uint ipv4, ushort port) =>
        SteamAPI_SteamNetworkingIPAddr_SetIPv4(ref this, ipv4, port);

    public unsafe void SetIPv6(ReadOnlySpan<byte> ipv6, ushort port)
    {
        if (ipv6.Length != 16)
            throw new ArgumentException("IPv6 address must be exactly 16 bytes.", nameof(ipv6));

        fixed (byte* p = ipv6)
        {
            SteamAPI_SteamNetworkingIPAddr_SetIPv6(ref this, p, port);
        }
    }

    /// <summary>
    /// Parses an IPv4 or IPv6 endpoint string. Throws when <paramref name="ip"/>
    /// is malformed.
    /// </summary>
    public void SetAddress(string ip, ushort port)
    {
        ArgumentNullException.ThrowIfNull(ip);

        if (!ip.Contains(':'))
            SetIPv4(ParseIPv4(ip), port);
        else
            SetIPv6(ParseIPv6(ip), port);
    }

    public uint GetIPv4()
    {
        Address self = this;
        return SteamAPI_SteamNetworkingIPAddr_GetIPv4(ref self);
    }

    /// <summary>Returns the canonical IP string (no port).</summary>
    public string GetIP() => ToString(withPort: false);

    /// <summary>Formats this endpoint via the native <c>ToString</c> helper.</summary>
    public unsafe string ToString(bool withPort)
    {
        const int bufSize = 64;
        byte* buf = stackalloc byte[bufSize];
        Address self = this;
        SteamAPI_SteamNetworkingIPAddr_ToString(&self, buf, bufSize, withPort);
        return Marshal.PtrToStringUTF8((nint)buf) ?? string.Empty;
    }

    public override string ToString() => ToString(withPort: true);

    public bool Equals(Address other) =>
        SteamAPI_SteamNetworkingIPAddr_IsEqualTo(ref this, ref other);

    public override bool Equals(object? obj) => obj is Address other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < 16; i++) hash.Add(ip[i]);
        hash.Add(port);
        return hash.ToHashCode();
    }

    public static bool operator ==(Address left, Address right) => left.Equals(right);
    public static bool operator !=(Address left, Address right) => !left.Equals(right);

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static uint ParseIPv4(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new FormatException("Incorrect format of an IPv4 address.");

        byte[] bytes = address.GetAddressBytes();
        Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static byte[] ParseIPv6(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new FormatException("Incorrect format of an IPv6 address.");

        return address.GetAddressBytes();
    }

    // -----------------------------------------------------------------
    // Native (steamnetworkingsockets_flat.h)
    // -----------------------------------------------------------------

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIPAddr_Clear(ref Address self);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIPAddr_IsIPv6AllZeros(ref Address self);

    [LibraryImport(LibraryName)]
    internal static unsafe partial void SteamAPI_SteamNetworkingIPAddr_SetIPv6(ref Address self, byte* ipv6, ushort port);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIPAddr_SetIPv4(ref Address self, uint ipv4, ushort port);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIPAddr_IsIPv4(ref Address self);

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_SteamNetworkingIPAddr_GetIPv4(ref Address self);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIPAddr_SetIPv6LocalHost(ref Address self, ushort port);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIPAddr_IsLocalHost(ref Address self);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIPAddr_IsEqualTo(ref Address self, ref Address other);

    [LibraryImport(LibraryName)]
    internal static unsafe partial void SteamAPI_SteamNetworkingIPAddr_ToString(Address* self, byte* buf, nuint cbBuf, [MarshalAs(UnmanagedType.U1)] bool withPort);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIPAddr_ParseString(ref Address self, string str);
}

/// <summary>
/// Inline 16-byte buffer used by <see cref="Address.ip"/>. Blittable and
/// AOT-friendly. Index it directly (<c>ip[0]</c> .. <c>ip[15]</c>) or
/// pass it where a <see cref="Span{T}"/> is expected — the compiler
/// implicitly converts it.
/// </summary>
[InlineArray(16)]
public struct IpBytes
{
    private byte _element0;
}
