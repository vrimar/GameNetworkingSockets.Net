using System.Net;
using System.Net.Sockets;
using System.Text;
using Valve.Sockets;

return args.Length == 0
    ? Usage()
    : args[0].ToLowerInvariant() switch
    {
        "server" => RunServer(args.Length > 1 ? ushort.Parse(args[1]) : (ushort)27015, CertDir(args, 2)),
        "client" => RunClient(args.Length > 1 ? args[1] : "127.0.0.1:27015", CertDir(args, 2)),
        _ => Usage(),
    };

static int Usage()
{
    Console.Error.WriteLine("Usage: testapp server [port] [certs-dir]");
    Console.Error.WriteLine("       testapp client [host:port] [certs-dir]");
    return 1;
}

static string CertDir(string[] args, int index) =>
    args.Length > index ? args[index] : Path.Combine(AppContext.BaseDirectory, "certs");

static int RunServer(ushort port, string certDir)
{
    var (root, certificate) = MintServerCertificate(certDir);

    if (!Library.Initialize(out var initErr))
    {
        Console.Error.WriteLine($"Library.Initialize failed: {initErr}");
        return 1;
    }
    try
    {
        var sockets = new NetworkingSockets();
        using var utils = new NetworkingUtils();
        utils.SetDebugCallback(DebugType.Important, (t, m) => Console.WriteLine($"[gns:{t}] {m}"));

        // Before SetCertificate, which cannot read a certificate whose root it does not trust.
        if (!sockets.AddTrustedRootCA(root, out var caErr))
        {
            Console.Error.WriteLine($"AddTrustedRootCA failed: {caErr}");
            return 1;
        }

        if (!sockets.SetCertificate(certificate, out var certErr))
        {
            Console.Error.WriteLine($"SetCertificate failed: {certErr}");
            return 1;
        }

        var liveConnections = new HashSet<uint>();

        utils.SetStatusCallback((ref StatusInfo info) =>
        {
            var state = info.connectionInfo.state;
            Console.WriteLine($"[server] conn={info.connection} {info.oldState} -> {state} ({info.connectionInfo.EndDebug})");

            switch (state)
            {
                case ConnectionState.Connecting:
                    var r = sockets.AcceptConnection(info.connection);
                    if (r != Result.OK)
                    {
                        Console.Error.WriteLine($"[server] AcceptConnection failed: {r}");
                        sockets.CloseConnection(info.connection, 0, "accept failed", false);
                    }
                    else
                    {
                        liveConnections.Add(info.connection);
                    }
                    break;

                case ConnectionState.ClosedByPeer:
                case ConnectionState.ProblemDetectedLocally:
                    sockets.CloseConnection(info.connection, 0, "peer closed", false);
                    liveConnections.Remove(info.connection);
                    break;
            }
        });

        var listen = Listen(sockets, port);
        if (listen == 0)
        {
            Console.Error.WriteLine("[server] CreateListenSocket failed.");
            return 1;
        }

        var bound = default(Address);
        sockets.GetListenSocketAddress(listen, ref bound);
        Console.WriteLine($"[server] listening on port {bound.port}. Press Ctrl-C to stop.");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        MessageCallback onMsg = (in NetworkingMessage msg) =>
        {
            var text = Encoding.UTF8.GetString(msg.AsSpan());
            Console.WriteLine($"[server] <- conn={msg.connection} {text}");
            var reply = Encoding.UTF8.GetBytes($"echo: {text}");
            sockets.SendMessageToConnection(msg.connection, reply, SendFlags.Reliable);
        };

        while (!cts.IsCancellationRequested)
        {
            sockets.RunCallbacks();
            foreach (var c in liveConnections)
                sockets.ReceiveMessagesOnConnection(c, onMsg, 32);
            Thread.Sleep(10);
        }

        Console.WriteLine("[server] shutting down…");
        foreach (var c in liveConnections.ToArray())
            sockets.CloseConnection(c, 0, "server shutting down", enableLinger: true);
        sockets.CloseListenSocket(listen);
    }
    finally
    {
        Library.Deinitialize();
    }
    return 0;
}

static int RunClient(string endpoint, string certDir)
{
    var rootPath = Path.Combine(certDir, "root.txt");
    if (!File.Exists(rootPath))
    {
        Console.Error.WriteLine($"Missing root certificate at {rootPath}. Start the server first to mint it.");
        return 1;
    }
    var root = File.ReadAllText(rootPath).Trim();

    var (host, port) = ParseEndpoint(endpoint);

    if (!Library.Initialize(out var initErr))
    {
        Console.Error.WriteLine($"Library.Initialize failed: {initErr}");
        return 1;
    }
    try
    {
        var sockets = new NetworkingSockets();
        using var utils = new NetworkingUtils();
        utils.SetDebugCallback(DebugType.Important, (t, m) => Console.WriteLine($"[gns:{t}] {m}"));

        if (!sockets.AddTrustedRootCA(root, out var caErr))
        {
            Console.Error.WriteLine($"AddTrustedRootCA failed: {caErr}");
            return 1;
        }

        var state = ConnectionState.None;
        utils.SetStatusCallback((ref StatusInfo info) =>
        {
            state = info.connectionInfo.state;
            Console.WriteLine($"[client] conn={info.connection} {info.oldState} -> {state} ({info.connectionInfo.EndDebug})");
        });

        var addr = default(Address);
        addr.SetAddress(host, port);

        var conn = sockets.Connect(ref addr,
        [
            Configuration.Int32(ConfigurationValue.IPAllowWithoutAuth, 0),
            Configuration.Int32(ConfigurationValue.IPLocalHostAllowWithoutAuth, 0),
        ]);
        if (conn == 0)
        {
            Console.Error.WriteLine("[client] Connect failed.");
            return 1;
        }

        // Wait for the handshake (incl. cert verification) to finish.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (state is not ConnectionState.Connected and not ConnectionState.ProblemDetectedLocally and not ConnectionState.ClosedByPeer
               && DateTime.UtcNow < deadline)
        {
            sockets.RunCallbacks();
            Thread.Sleep(10);
        }
        if (state != ConnectionState.Connected)
        {
            Console.Error.WriteLine($"[client] failed to reach Connected (state={state}).");
            return 1;
        }

        // Send a handful of reliable messages; collect their echoes.
        const int messageCount = 5;
        int echoes = 0;
        MessageCallback onMsg = (in NetworkingMessage msg) =>
        {
            Console.WriteLine($"[client] <- {Encoding.UTF8.GetString(msg.AsSpan())}");
            echoes++;
        };

        for (int i = 0; i < messageCount; i++)
        {
            var payload = Encoding.UTF8.GetBytes($"hello {i} @ {DateTime.UtcNow:HH:mm:ss.fff}");
            var r = sockets.SendMessageToConnection(conn, payload, SendFlags.Reliable);
            if (r != Result.OK)
            {
                Console.Error.WriteLine($"[client] SendMessageToConnection #{i} failed: {r}");
                break;
            }
            Console.WriteLine($"[client] -> hello {i}");
        }

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (echoes < messageCount && DateTime.UtcNow < until && state == ConnectionState.Connected)
        {
            sockets.RunCallbacks();
            sockets.ReceiveMessagesOnConnection(conn, onMsg, 32);
            Thread.Sleep(10);
        }

        Console.WriteLine($"[client] received {echoes}/{messageCount} echoes; closing.");
        sockets.CloseConnection(conn, 0, "client done", enableLinger: true);

        // Drain so the FIN reaches the server before Deinitialize().
        var drainUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        while (DateTime.UtcNow < drainUntil)
        {
            sockets.RunCallbacks();
            Thread.Sleep(10);
        }
    }
    finally
    {
        Library.Deinitialize();
    }
    return 0;
}

static (string host, ushort port) ParseEndpoint(string ep)
{
    var idx = ep.LastIndexOf(':');
    if (idx < 0 || idx == ep.Length - 1)
        throw new FormatException($"Expected host:port, got '{ep}'.");
    return (ep[..idx], ushort.Parse(ep[(idx + 1)..]));
}

static uint Listen(NetworkingSockets sockets, ushort port)
{
    for (var attempt = 0; attempt < 5; attempt++)
    {
        var bind = default(Address);
        bind.port = port == 0 ? FreeUdpPort() : port;
        var listen = sockets.CreateListenSocket(ref bind);
        if (listen != 0 || port != 0)
            return listen;
    }

    return 0;
}

// GameNetworkingSockets refuses to listen on port 0, so ask the OS for a free one.
static ushort FreeUdpPort()
{
    using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    probe.Bind(new IPEndPoint(IPAddress.Any, 0));
    return (ushort)((IPEndPoint)probe.LocalEndPoint!).Port;
}

static (string Root, byte[] Certificate) MintServerCertificate(string dir)
{
    Directory.CreateDirectory(dir);
    var authorityPath = Path.Combine(dir, "authority.bin");
    var now = DateTimeOffset.UtcNow;

    var minted = !File.Exists(authorityPath);
    using var authority = minted
        ? NetworkingCertificateAuthority.Create(now, TimeSpan.FromDays(3650))
        : NetworkingCertificateAuthority.Import(File.ReadAllBytes(authorityPath));

    if (minted)
    {
        File.WriteAllBytes(authorityPath, authority.Export());
        Console.WriteLine($"[server] minted a new authority under {dir}; authority.bin is its secret key");
    }

    File.WriteAllText(Path.Combine(dir, "root.txt"), authority.RootCertificate);
    return (authority.RootCertificate, authority.Issue("str:testapp-server", now, TimeSpan.FromDays(1)));
}
