using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

public partial class UdpSocketBufferSizeTests
{
    private const int DefaultBytes = 256 * 1024;

    // Under Linux's stock net.core.rmem_max, so the kernel grants it as asked.
    private const int RequestedBytes = 200 * 1024;

    private const int SolSocket = 1;
    private const int SoRcvbuf = 8;

    [SkippableFact]
    public void A_listen_socket_opened_after_the_setter_gets_the_requested_receive_buffer()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");
        Skip.IfNot(OperatingSystem.IsLinux(), "Reads the socket's buffer through Linux's /proc and getsockopt.");

        Assert.True(Library.Initialize(out var error), $"Initialize failed: {error}");
        try
        {
            Library.SetUdpSocketBufferSize(RequestedBytes);

            var before = UdpSocketDescriptors();
            var sockets = new NetworkingSockets();
            var listen = default(Address);
            listen.SetIPv4(0, FreeUdpPort());
            var listenSocket = sockets.CreateListenSocket(ref listen);
            Assert.NotEqual(0u, listenSocket);

            var opened = UdpSocketDescriptors().Except(before).ToList();
            Assert.NotEmpty(opened);

            // Linux doubles a requested buffer to cover its own bookkeeping.
            foreach (var fd in opened)
                Assert.Equal(RequestedBytes * 2, ReceiveBuffer(fd));

            sockets.CloseListenSocket(listenSocket);
        }
        finally
        {
            Library.SetUdpSocketBufferSize(DefaultBytes);
            Library.Deinitialize();
        }
    }

    private static HashSet<int> UdpSocketDescriptors()
    {
        var udp = UdpInodes();
        var descriptors = new HashSet<int>();
        foreach (var entry in new DirectoryInfo("/proc/self/fd").EnumerateFileSystemInfos())
            if (
                entry.LinkTarget is { } target
                && target.StartsWith("socket:[", StringComparison.Ordinal)
                && udp.Contains(target["socket:[".Length..^1])
            )
                descriptors.Add(int.Parse(entry.Name));

        return descriptors;
    }

    private static HashSet<string> UdpInodes()
    {
        var inodes = new HashSet<string>();
        foreach (var table in new[] { "/proc/net/udp", "/proc/net/udp6" })
            foreach (var line in File.ReadLines(table).Skip(1))
                inodes.Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[9]);

        return inodes;
    }

    private static int ReceiveBuffer(int fd)
    {
        var size = sizeof(int);
        Assert.Equal(0, getsockopt(fd, SolSocket, SoRcvbuf, out var bytes, ref size));
        return bytes;
    }

    private static ushort FreeUdpPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getsockopt(int fd, int level, int name, out int value, ref int length);
}
