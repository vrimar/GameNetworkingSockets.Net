using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

/// <summary>
/// Holds what a certificate minted by <see cref="NetworkingCertificateAuthority"/> is worth to the real library.
/// A process has one trust store and one certificate, so a client that holds no certificate, or trusts only a
/// different root, dials a TestApp server running in a second process.
/// </summary>
public partial class CertificateHandshakeTests
{
    private const int RemoteBadCert = 4003;
    private const int MiscTimeout = 5003;

    private static readonly TimeSpan s_settleWithin = TimeSpan.FromSeconds(5);

    [SkippableFact]
    public void StrictClientRefusesUnsignedServer()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, utils) =>
            Assert.Equal(
                new DialOutcome(Connected: false, ServerAnswered: true, EndReason: MiscTimeout),
                Dial(sockets, utils, StrictClient)));
    }

    [SkippableFact]
    public void StrictClientAcceptsServerSignedByTrustedRoot()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, utils) =>
        {
            var now = DateTimeOffset.UtcNow;
            using var authority = NetworkingCertificateAuthority.Create(now, TimeSpan.FromDays(2));

            Assert.True(sockets.AddTrustedRootCA(authority.RootCertificate, out var trustError), trustError);
            var certificate = authority.Issue("str:test-server", now, TimeSpan.FromDays(1));
            Assert.True(sockets.SetCertificate(certificate, out var certError), certError);

            Assert.True(Dial(sockets, utils, StrictClient).Connected);
        });
    }

    [SkippableFact]
    public void ClientWithoutCertificateAcceptsOnlyServerSignedByTheRootItTrusts()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        using var server = TestAppServer.Start();

        WithLibrary((sockets, utils) =>
        {
            using var stranger = NetworkingCertificateAuthority.Create(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
            Assert.True(sockets.AddTrustedRootCA(stranger.RootCertificate, out var strangerError), strangerError);
            Assert.Equal(
                new DialOutcome(Connected: false, ServerAnswered: false, EndReason: RemoteBadCert),
                Dial(sockets, utils, StrictClient, server.Port));

            Assert.True(sockets.AddTrustedRootCA(server.RootCertificate, out var trustError), trustError);
            Assert.True(Dial(sockets, utils, StrictClient, server.Port).Connected);
        });
    }

    [SkippableFact]
    public void AuthorityIsRefusedAsATrustedRoot()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, _) =>
        {
            using var authority = NetworkingCertificateAuthority.Create(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));

            Assert.False(sockets.AddTrustedRootCA(Convert.ToBase64String(authority.Export()), out var error));
            Assert.False(string.IsNullOrEmpty(error));
            Assert.True(sockets.AddTrustedRootCA(authority.RootCertificate, out var rootError), rootError);
        });
    }

    [SkippableFact]
    public void CertificateFromUntrustedRootIsRefused()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            using var authority = NetworkingCertificateAuthority.Create(now, TimeSpan.FromDays(2));
            var certificate = authority.Issue("str:test-server", now, TimeSpan.FromDays(1));

            Assert.False(sockets.SetCertificate(certificate, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        });
    }

    [SkippableFact]
    public void ExpiredCertificateIsInstalledWithoutComplaint()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, _) =>
        {
            var minted = DateTimeOffset.UtcNow.AddDays(-3);
            using var authority = NetworkingCertificateAuthority.Create(minted, TimeSpan.FromDays(10));
            var expired = authority.Issue("str:test-server", minted, TimeSpan.FromDays(1));

            Assert.True(sockets.AddTrustedRootCA(authority.RootCertificate, out var trustError), trustError);
            Assert.True(sockets.SetCertificate(expired, out var certError), certError);
        });
    }

    [SkippableFact]
    public void LoopbackSkipsAuthenticationUnlessLockedDownToo()
    {
        Skip.IfNot(Native.Present(), "GameNetworkingSockets native library not deployed.");

        WithLibrary((sockets, utils) =>
            Assert.True(Dial(sockets, utils, [Configuration.Int32(ConfigurationValue.IPAllowWithoutAuth, 0)]).Connected));
    }

    private static Configuration[] StrictClient =>
    [
        Configuration.Int32(ConfigurationValue.IPAllowWithoutAuth, 0),
        Configuration.Int32(ConfigurationValue.IPLocalHostAllowWithoutAuth, 0),
        Configuration.Int32(ConfigurationValue.TimeoutInitial, 2000),
    ];

    private readonly record struct DialOutcome(bool Connected, bool ServerAnswered, int EndReason);

    private static void WithLibrary(Action<NetworkingSockets, NetworkingUtils> test)
    {
        Assert.True(Library.Initialize(out var error), $"Initialize failed: {error}");
        try
        {
            using var utils = new NetworkingUtils();
            test(new NetworkingSockets(), utils);
        }
        finally
        {
            Library.Deinitialize();
        }
    }

    private static DialOutcome Dial(NetworkingSockets sockets, NetworkingUtils utils, Configuration[] client, ushort? remotePort = null)
    {
        uint listenSocket = 0, serverConnection = 0, clientConnection = 0;
        bool? connected = null;
        var serverAnswered = false;
        var endReason = 0;

        utils.SetStatusCallback((ref StatusInfo info) =>
        {
            if (listenSocket != 0 && info.connectionInfo.listenSocket == listenSocket)
            {
                if (info.connectionInfo.state == ConnectionState.Connecting)
                {
                    serverConnection = info.connection;
                    sockets.AcceptConnection(info.connection);
                }
                else if (info.connectionInfo.state == ConnectionState.Connected)
                {
                    serverAnswered = true;
                }

                return;
            }

            if (info.connection != clientConnection)
                return;

            if (info.connectionInfo.state == ConnectionState.Connected)
            {
                connected = true;
            }
            else if (info.connectionInfo.state is ConnectionState.ClosedByPeer or ConnectionState.ProblemDetectedLocally)
            {
                connected = false;
                endReason = info.connectionInfo.endReason;
            }
        });

        try
        {
            var port = remotePort ?? Listen(sockets, out listenSocket);

            var dial = default(Address);
            dial.SetAddress("127.0.0.1", port);
            clientConnection = sockets.Connect(ref dial, client);
            Assert.NotEqual(0u, clientConnection);

            var deadline = Stopwatch.GetTimestamp() + (long)(s_settleWithin.TotalSeconds * Stopwatch.Frequency);
            while (connected is null && Stopwatch.GetTimestamp() < deadline)
            {
                sockets.RunCallbacks();
                Thread.Sleep(2);
            }

            Assert.True(connected.HasValue, "The dial never settled.");
            return new(connected.Value, serverAnswered, endReason);
        }
        finally
        {
            sockets.CloseConnection(clientConnection);
            sockets.CloseConnection(serverConnection);
            sockets.CloseListenSocket(listenSocket);
            utils.SetStatusCallback(null);
        }
    }

    private static ushort Listen(NetworkingSockets sockets, out uint listenSocket)
    {
        for (var attempt = 1; ; attempt++)
        {
            var listen = default(Address);
            listen.SetIPv4(0, FreeUdpPort());
            listenSocket = sockets.CreateListenSocket(ref listen);
            if (listenSocket != 0)
                return listen.port;

            Assert.True(attempt < 5, "No free UDP port could be listened on.");
        }
    }

    private static ushort FreeUdpPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Any, 0));
        return (ushort)((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    private sealed partial class TestAppServer : IDisposable
    {
        private static readonly TimeSpan s_startWithin = TimeSpan.FromSeconds(20);

        private readonly Process _process;
        private readonly DirectoryInfo _certificates;

        private TestAppServer(Process process, DirectoryInfo certificates, ushort port)
        {
            _process = process;
            _certificates = certificates;
            Port = port;
            RootCertificate = File.ReadAllText(Path.Combine(certificates.FullName, "root.txt")).Trim();
        }

        public ushort Port { get; }

        public string RootCertificate { get; }

        public static TestAppServer Start()
        {
            var certificates = Directory.CreateTempSubdirectory("gns-testapp-");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "GameNetworkingSockets.Net.TestApp.dll"));
            start.ArgumentList.Add("server");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add(certificates.FullName);

            var listening = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new StringBuilder();
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line && ListeningOn().Match(line) is { Success: true } match)
                    listening.TrySetResult(ushort.Parse(match.Groups[1].ValueSpan));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                lock (errors)
                    errors.AppendLine(e.Data);
            };
            process.Exited += (_, _) => listening.TrySetException(new InvalidOperationException("The TestApp server exited."));

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                if (!listening.Task.Wait(s_startWithin))
                    throw new TimeoutException("The TestApp server never started listening.");
                return new(process, certificates, listening.Task.Result);
            }
            catch (Exception e)
            {
                Stop(process, certificates);
                lock (errors)
                    Assert.Fail($"{e.GetBaseException().Message} Its stderr:\n{errors}");
                throw;
            }
        }

        public void Dispose() => Stop(_process, _certificates);

        private static void Stop(Process process, DirectoryInfo certificates)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
            process.Dispose();
            certificates.Delete(recursive: true);
        }

        [GeneratedRegex(@"listening on port (\d+)")]
        private static partial Regex ListeningOn();
    }
}
