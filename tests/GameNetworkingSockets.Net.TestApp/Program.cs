using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Valve.Sockets;

// Minimal client/server demo over GameNetworkingSockets with a runtime-minted
// trust chain (CA -> server cert). Run the server first; it mints the certs
// into ./certs/, then the client reads ca.cert.txt from the same directory.
//
//   testapp server [port]            # default 27015
//   testapp client [host:port]       # default 127.0.0.1:27015
//
// Note on cert *rejection*: this demo only exercises the happy path. Making
// the open-source build reject a peer with an untrusted cert is harder than
// it looks:
//   * Server-side strict auth (IP_AllowWithoutAuth=0) is gated behind the
//     STEAMNETWORKINGSOCKETS_CAN_REQUEST_CERT macro (undefined in our build),
//     so CreateListenSocket errors with "No cert authority, must set
//     IP_AllowWithoutAuth".
//   * Client-side strict auth refuses any ConnectOK whose cert has no
//     identity_string (steamnetworkingsockets_udp.cpp ~L1620, "Unauthenticated
//     connections not allowed"), and the bundled certtool's `create_cert`
//     doesn't expose --identity. So even a perfectly CA-signed cert is
//     rejected.
// Demonstrating cert rejection needs either a certtool extension or a
// native helper that mints identity-bound certs.

return args.Length == 0
    ? Usage()
    : args[0].ToLowerInvariant() switch
    {
        "server" => RunServer(args.Length > 1 ? ushort.Parse(args[1]) : (ushort)27015),
        "client" => RunClient(args.Length > 1 ? args[1] : "127.0.0.1:27015"),
        _ => Usage(),
    };

static int Usage()
{
    Console.Error.WriteLine("Usage: testapp server [port]");
    Console.Error.WriteLine("       testapp client [host:port]");
    return 1;
}

static string CertDir() => Path.Combine(AppContext.BaseDirectory, "certs");

static int RunServer(ushort port)
{
    var bundle = EnsureCerts();

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

        // Install our cert + private key so the server has a verifiable identity.
        // SetCertificateAndPrivateKey wipes the private-key buffer, so pass a copy.
        var serverCertBytes = Encoding.ASCII.GetBytes(bundle.ServerCertPem);
        var serverPrivBytes = Encoding.ASCII.GetBytes(bundle.ServerPrivPem);
        if (!sockets.SetCertificateAndPrivateKey(serverCertBytes, serverPrivBytes, out var certErr))
        {
            Console.Error.WriteLine($"SetCertificateAndPrivateKey failed: {certErr}");
            return 1;
        }

        // Trust our own CA so the server can verify its own cert chain at startup.
        if (!sockets.AddTrustedRootCA(bundle.CaCertBase64, out var caErr))
        {
            Console.Error.WriteLine($"AddTrustedRootCA failed: {caErr}");
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

        var bind = default(Address);
        bind.port = port;

        var listen = sockets.CreateListenSocket(ref bind);
        if (listen == 0)
        {
            Console.Error.WriteLine("[server] CreateListenSocket failed.");
            return 1;
        }

        Console.WriteLine($"[server] listening on port {port}. Press Ctrl-C to stop.");
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

static int RunClient(string endpoint)
{
    var caPath = Path.Combine(CertDir(), "ca.cert.txt");
    if (!File.Exists(caPath))
    {
        Console.Error.WriteLine($"Missing CA cert at {caPath}. Start the server first to mint it.");
        return 1;
    }
    var caCertBase64 = File.ReadAllText(caPath).Trim();

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

        if (!sockets.AddTrustedRootCA(caCertBase64, out var caErr))
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

        var conn = sockets.Connect(ref addr);
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

// -----------------------------------------------------------------
// Cert minting via the bundled steamnetworkingsockets_certtool.
// -----------------------------------------------------------------

static CertBundle EnsureCerts()
{
    var dir = CertDir();
    Directory.CreateDirectory(dir);

    var caCertPath = Path.Combine(dir, "ca.cert.txt");
    var serverCertPath = Path.Combine(dir, "server.cert.pem");
    var serverPrivPath = Path.Combine(dir, "server.priv.pem");

    if (File.Exists(caCertPath) && File.Exists(serverCertPath) && File.Exists(serverPrivPath))
    {
        Console.WriteLine($"[server] reusing existing certs under {dir}");
        return new CertBundle(
            File.ReadAllText(caCertPath).Trim(),
            File.ReadAllText(serverCertPath),
            File.ReadAllText(serverPrivPath));
    }

    Console.WriteLine($"[server] minting fresh CA + server cert under {dir}");
    var certtool = LocateCerttool();

    var ca = RunCerttool(certtool, "gen_keypair");
    var caPub = ca.GetProperty("public_key").GetString()!;
    var caPriv = ca.GetProperty("private_key").GetString()!;

    var caCert = RunCerttool(certtool, "--ca-priv-key", Whitespace(caPriv), "--pub-key", caPub, "create_cert");
    var caCertBase64 = caCert.GetProperty("cert").GetString()!;

    var srv = RunCerttool(certtool, "gen_keypair");
    var srvPub = srv.GetProperty("public_key").GetString()!;
    var srvPriv = srv.GetProperty("private_key").GetString()!;

    var srvCert = RunCerttool(certtool, "--ca-priv-key", Whitespace(caPriv), "--pub-key", srvPub, "create_cert");
    var srvCertBase64 = srvCert.GetProperty("cert").GetString()!;

    // SetCertificateAndPrivateKey parses STEAMDATAGRAM-CERT-wrapped PEM;
    // AddTrustedRootCA takes the bare base64 body.
    var serverCertPem = $"-----BEGIN STEAMDATAGRAM CERT-----\n{srvCertBase64}\n-----END STEAMDATAGRAM CERT-----\n";

    File.WriteAllText(caCertPath, caCertBase64);
    File.WriteAllText(serverCertPath, serverCertPem);
    File.WriteAllText(serverPrivPath, srvPriv);

    // Also drop the CA priv key beside the rest in case the user wants to mint
    // additional server certs later. Treat the file as a secret.
    File.WriteAllText(Path.Combine(dir, "ca.priv.pem"), caPriv);

    return new CertBundle(caCertBase64, serverCertPem, srvPriv);
}

static string Whitespace(string s) => Regex.Replace(s, @"\s+", " ");

static JsonElement RunCerttool(string certtool, params string[] args)
{
    var psi = new ProcessStartInfo(certtool)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    psi.ArgumentList.Add("--output-json");
    foreach (var a in args) psi.ArgumentList.Add(a);

    using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {certtool}.");
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0)
        throw new InvalidOperationException($"certtool exited {p.ExitCode}: {stderr}");
    return JsonDocument.Parse(stdout).RootElement.Clone();
}

static string LocateCerttool()
{
    var exe = OperatingSystem.IsWindows()
        ? "steamnetworkingsockets_certtool.exe"
        : "steamnetworkingsockets_certtool";

    var beside = Path.Combine(AppContext.BaseDirectory, exe);
    if (File.Exists(beside)) return beside;

    // Dev fallback: walk up to the repo root and look in artifacts/native/<rid>/.
    var rid = RuntimeInformation.RuntimeIdentifier;
    var rids = new[] { rid, "win-x64", "linux-x64", "osx-x64" };
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        foreach (var r in rids)
        {
            var candidate = Path.Combine(dir.FullName, "artifacts", "native", r, exe);
            if (File.Exists(candidate)) return candidate;
        }
        dir = dir.Parent;
    }

    throw new FileNotFoundException(
        $"Could not locate {exe}. Build the natives (build/build-native-{(OperatingSystem.IsWindows() ? "win.ps1" : "unix.sh")}) first.");
}

record CertBundle(string CaCertBase64, string ServerCertPem, string ServerPrivPem);
