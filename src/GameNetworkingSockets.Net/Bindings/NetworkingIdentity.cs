using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// Tagged-union identity used by the P2P APIs. Maps 1:1 to
/// <c>SteamNetworkingIdentity</c>. The C++ struct is 136 bytes:
/// 4-byte type tag, 4-byte alignment padding, then a 128-byte payload union.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public partial struct NetworkingIdentity : IEquatable<NetworkingIdentity>
{
    internal const int Size = 136;
    internal const int MaxStringLength = 128;
    internal const int MaxGenericStringLength = 32;
    internal const int MaxGenericBytesLength = 32;
    internal const int MaxXboxPairwiseIDLength = 33;

    private const string LibraryName = Library.LibraryName;

    [FieldOffset(0)]
    public IdentityType type;

    /// <summary>Length in bytes of whichever payload field is currently populated.</summary>
    [FieldOffset(4)]
    public int sizeInBytes;

    public bool IsInvalid
    {
        get
        {
            NetworkingIdentity self = this;
            return SteamAPI_SteamNetworkingIdentity_IsInvalid(ref self);
        }
    }

    public bool IsLocalHost
    {
        get
        {
            NetworkingIdentity self = this;
            return SteamAPI_SteamNetworkingIdentity_IsLocalHost(ref self);
        }
    }

    public void Clear() => SteamAPI_SteamNetworkingIdentity_Clear(ref this);

    public void SetLocalHost() => SteamAPI_SteamNetworkingIdentity_SetLocalHost(ref this);

    public ulong GetSteamID()
    {
        NetworkingIdentity self = this;
        return SteamAPI_SteamNetworkingIdentity_GetSteamID64(ref self);
    }

    public void SetSteamID(ulong steamID) => SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref this, steamID);

    public string GetGenericString()
    {
        NetworkingIdentity self = this;
        var ptr = SteamAPI_SteamNetworkingIdentity_GetGenericString(ref self);
        return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    public bool SetGenericString(string genericString)
    {
        ArgumentNullException.ThrowIfNull(genericString);
        return SteamAPI_SteamNetworkingIdentity_SetGenericString(ref this, genericString);
    }

    public unsafe bool SetGenericBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxGenericBytesLength)
            throw new ArgumentOutOfRangeException(nameof(data), $"Length must be <= {MaxGenericBytesLength}.");

        fixed (byte* p = data)
        {
            return SteamAPI_SteamNetworkingIdentity_SetGenericBytes(ref this, (nint)p, (uint)data.Length);
        }
    }

    public unsafe ReadOnlySpan<byte> GetGenericBytes()
    {
        NetworkingIdentity self = this;
        int len = 0;
        var ptr = SteamAPI_SteamNetworkingIdentity_GetGenericBytes(ref self, ref len);
        return ptr == null ? default : new ReadOnlySpan<byte>(ptr, len);
    }

    public bool SetXboxPairwiseID(string xboxPairwiseID)
    {
        ArgumentNullException.ThrowIfNull(xboxPairwiseID);
        return SteamAPI_SteamNetworkingIdentity_SetXboxPairwiseID(ref this, xboxPairwiseID);
    }

    public string GetXboxPairwiseID()
    {
        NetworkingIdentity self = this;
        var ptr = SteamAPI_SteamNetworkingIdentity_GetXboxPairwiseID(ref self);
        return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    public void SetIPAddr(ref Address address) =>
        SteamAPI_SteamNetworkingIdentity_SetIPAddr(ref this, ref address);

    public unsafe bool TryGetIPAddr(out Address address)
    {
        NetworkingIdentity self = this;
        var ptr = SteamAPI_SteamNetworkingIdentity_GetIPAddr(ref self);
        if (ptr == null)
        {
            address = default;
            return false;
        }
        address = *ptr;
        return true;
    }

    /// <summary>Canonical string form (e.g., <c>"steamid:76561...".</c>).</summary>
    public unsafe override string ToString()
    {
        const int bufSize = MaxStringLength;
        byte* buf = stackalloc byte[bufSize];
        NetworkingIdentity self = this;
        SteamAPI_SteamNetworkingIdentity_ToString(&self, buf, bufSize);
        return Marshal.PtrToStringUTF8((nint)buf) ?? string.Empty;
    }

    /// <summary>
    /// Parses the canonical string form. Returns true on success; the
    /// identity is left unchanged on failure.
    /// </summary>
    public unsafe bool ParseString(string str)
    {
        ArgumentNullException.ThrowIfNull(str);
        NetworkingIdentity tmp = default;
        bool ok = SteamAPI_SteamNetworkingIdentity_ParseString(ref tmp, (nuint)Size, str);
        if (ok)
            this = tmp;
        return ok;
    }

    public bool Equals(NetworkingIdentity other) =>
        SteamAPI_SteamNetworkingIdentity_IsEqualTo(ref this, ref other);

    public override bool Equals(object? obj) => obj is NetworkingIdentity other && Equals(other);

    public override int GetHashCode()
    {
        // Hash the entire 136-byte payload to honor Equals semantics.
        var hash = new HashCode();
        unsafe
        {
            fixed (NetworkingIdentity* self = &this)
            {
                hash.AddBytes(new ReadOnlySpan<byte>(self, Size));
            }
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(NetworkingIdentity left, NetworkingIdentity right) => left.Equals(right);
    public static bool operator !=(NetworkingIdentity left, NetworkingIdentity right) => !left.Equals(right);

    // -----------------------------------------------------------------
    // Native (steamnetworkingsockets_flat.h)
    // -----------------------------------------------------------------

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIdentity_Clear(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_IsInvalid(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref NetworkingIdentity self, ulong steamID);

    [LibraryImport(LibraryName)]
    internal static partial ulong SteamAPI_SteamNetworkingIdentity_GetSteamID64(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIdentity_SetIPAddr(ref NetworkingIdentity self, ref Address addr);

    [LibraryImport(LibraryName)]
    internal static unsafe partial Address* SteamAPI_SteamNetworkingIdentity_GetIPAddr(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_SteamNetworkingIdentity_SetLocalHost(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_IsLocalHost(ref NetworkingIdentity self);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_SetGenericString(ref NetworkingIdentity self, string value);

    [LibraryImport(LibraryName)]
    internal static partial nint SteamAPI_SteamNetworkingIdentity_GetGenericString(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_IsEqualTo(ref NetworkingIdentity self, ref NetworkingIdentity other);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_SetGenericBytes(ref NetworkingIdentity self, nint data, uint length);

    [LibraryImport(LibraryName)]
    internal static unsafe partial byte* SteamAPI_SteamNetworkingIdentity_GetGenericBytes(ref NetworkingIdentity self, ref int length);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_SetXboxPairwiseID(ref NetworkingIdentity self, string xboxPairwiseID);

    [LibraryImport(LibraryName)]
    internal static partial nint SteamAPI_SteamNetworkingIdentity_GetXboxPairwiseID(ref NetworkingIdentity self);

    [LibraryImport(LibraryName)]
    internal static unsafe partial void SteamAPI_SteamNetworkingIdentity_ToString(NetworkingIdentity* self, byte* buf, nuint cbBuf);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_SteamNetworkingIdentity_ParseString(ref NetworkingIdentity self, nuint sizeofIdentity, string str);
}
