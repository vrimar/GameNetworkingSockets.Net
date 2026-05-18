using System.Reflection;
using System.Runtime.InteropServices;
using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

public class SmokeTests
{
    [Fact]
    public void AssemblyEmbedsRevision()
    {
        var attr = typeof(NetworkingSockets).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GameNetworkingSocketsRevision");

        Assert.NotNull(attr);
        Assert.False(string.IsNullOrEmpty(attr!.Value), "GameNetworkingSocketsRevision metadata should be populated.");
    }

    [Fact]
    public void EnumValuesMatchNativeConstants()
    {
        // Spot-check values that must remain stable across upstream revisions
        // (these are baked into the wire protocol or the C API signatures).
        Assert.Equal(1, (int)Result.OK);
        Assert.Equal(2, (int)Result.Fail);
        Assert.Equal(3, (int)ConnectionState.Connected);
        Assert.Equal(8, (int)SendFlags.Reliable);
        Assert.Equal(16, (int)IdentityType.SteamID);
    }

    [Fact]
    public void AddressStructHasExpectedSize()
    {
        // 16-byte ip + 2-byte port. The native struct is wrapped in
        // #pragma pack(1) (steamnetworkingtypes.h), so there's no trailing
        // padding even though uint64 sits inside the IPv4-mapped union.
        Assert.Equal(Address.Size, Marshal.SizeOf<Address>());
        Assert.Equal(18, Marshal.SizeOf<Address>());
    }

    [Fact]
    public void NetworkingIdentityStructHasExpectedSize()
    {
        // 4-byte type tag + 4-byte size + 128-byte payload union.
        Assert.Equal(NetworkingIdentity.Size, Marshal.SizeOf<NetworkingIdentity>());
        Assert.Equal(136, Marshal.SizeOf<NetworkingIdentity>());
    }

    [Fact]
    public void ConnectionInfoMatchesNativeLayout()
    {
        // SteamNetworkingIPAddr is declared inside #pragma pack(1), so its
        // size is 18 (16+2) with alignment 1 — there's no 4-byte pad before
        // m_addrRemote and no 6-byte tail pad inside it. The managed Address
        // mirrors that with [Pack = 1, Size = 18]; without it every field
        // after `address` would shift 6 bytes off the native layout and the
        // status callback would read garbage.
        Assert.Equal(0, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.identity)));
        Assert.Equal(136, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.userData)));
        Assert.Equal(144, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.listenSocket)));
        Assert.Equal(148, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.address)));
        Assert.Equal(176, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.state)));
        Assert.Equal(180, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.endReason)));
        Assert.Equal(440, (int)Marshal.OffsetOf<ConnectionInfo>(nameof(ConnectionInfo.flags)));
        Assert.Equal(696, Marshal.SizeOf<ConnectionInfo>());
    }

    [SkippableFact]
    public void InitializeAndDeinitialize()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed; per-RID smoke job runs this with the lib staged.");

        Assert.True(Library.Initialize(out var error), $"Initialize failed: {error}");
        try
        {
            var sockets = new NetworkingSockets();
            Assert.NotEqual(IntPtr.Zero, sockets.NativeHandle);

            using var utils = new NetworkingUtils();
            Assert.NotEqual(IntPtr.Zero, utils.NativeHandle);
            Assert.True(utils.Time > 0);
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void AddressSetLocalHostRoundTrip()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var address = default(Address);
            address.SetLocalHost(7777);
            Assert.True(address.IsLocalHost);
            Assert.Equal(7777, address.port);
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void IdentitySteamIDRoundTrip()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var id = default(NetworkingIdentity);
            id.SetSteamID(76561198000000001ul);
            Assert.Equal(IdentityType.SteamID, id.type);
            Assert.Equal(76561198000000001ul, id.GetSteamID());
            Assert.False(id.IsInvalid);

            // ToString / ParseString round-trip.
            var str = id.ToString();
            Assert.StartsWith("steamid:", str);
            var parsed = default(NetworkingIdentity);
            Assert.True(parsed.ParseString(str));
            Assert.Equal(id, parsed);
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void IdentityGenericStringRoundTrip()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var id = default(NetworkingIdentity);
            Assert.True(id.SetGenericString("player-42"));
            Assert.Equal(IdentityType.GenericString, id.type);
            Assert.Equal("player-42", id.GetGenericString());
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void NetworkingUtilsTimestampMonotonic()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            using var utils = new NetworkingUtils();
            var t0 = utils.Time;
            Thread.Sleep(2);
            var t1 = utils.Time;
            Assert.True(t1 > t0, $"Expected monotonic time, got t0={t0} t1={t1}.");
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void SetCertificateAndPrivateKeyRejectsGarbageBlobs()
    {
        // Verifies the new binding reaches the native lib and surfaces an error
        // message on failure — i.e. that the SteamNetworkingErrMsg buffer is
        // wired through. Round-trip with a real keypair is a manual step
        // (see README: run the bundled certtool offline).
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var sockets = new NetworkingSockets();
            Span<byte> garbageKey = stackalloc byte[16];
            byte[] garbageCert = new byte[16];
            Assert.False(sockets.SetCertificateAndPrivateKey(garbageCert, garbageKey, out var err));
            Assert.False(string.IsNullOrEmpty(err), "Expected a non-empty error message from the native side.");
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void AddTrustedRootCARejectsEmptyBlob()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var sockets = new NetworkingSockets();
            Assert.False(sockets.AddTrustedRootCA(string.Empty, out var err));
            Assert.False(string.IsNullOrEmpty(err), "Expected a non-empty error message from the native side.");
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    [SkippableFact]
    public void P2PListenSocketCanBeCreated()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        Assert.True(Library.Initialize(out _));
        try
        {
            var sockets = new NetworkingSockets();
            // P2P listen sockets with no signaling configured can still be created; they'll just have no accepted connections.
            var listen = sockets.CreateListenSocketP2P(localVirtualPort: 0);
            Assert.NotEqual(0u, listen);
            Assert.True(sockets.CloseListenSocket(listen));
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    private static bool NativeLibraryPresent()
    {
        var dir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(dir, "GameNetworkingSockets.dll"))
            || File.Exists(Path.Combine(dir, "libGameNetworkingSockets.so"))
            || File.Exists(Path.Combine(dir, "libGameNetworkingSockets.dylib"));
    }
}
