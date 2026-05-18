using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// Managed wrapper around <c>ISteamNetworkingSockets</c>. Construct a single
/// instance after <see cref="Library.Initialize()"/> has succeeded.
/// </summary>
public sealed partial class NetworkingSockets
{
    private const string LibraryName = Library.LibraryName;

    private readonly nint _nativeSockets;

    public NetworkingSockets()
    {
        _nativeSockets = SteamAPI_SteamNetworkingSockets_v009();

        if (_nativeSockets == IntPtr.Zero)
            throw new InvalidOperationException("SteamAPI_SteamNetworkingSockets_v009 returned null. Did Library.Initialize() succeed?");
    }

    /// <summary>Raw native pointer for advanced interop. Do not free.</summary>
    public nint NativeHandle => _nativeSockets;

    // -----------------------------------------------------------------
    // Listen sockets / connections
    // -----------------------------------------------------------------

    public uint CreateListenSocket(ref Address address) =>
        SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(_nativeSockets, ref address, 0, nint.Zero);

    public unsafe uint CreateListenSocket(ref Address address, Configuration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);

        fixed (Configuration* p = configurations)
        {
            return SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(_nativeSockets, ref address, configurations.Length, (nint)p);
        }
    }

    public uint Connect(ref Address address) =>
        SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(_nativeSockets, ref address, 0, nint.Zero);

    public unsafe uint Connect(ref Address address, Configuration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);

        fixed (Configuration* p = configurations)
        {
            return SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(_nativeSockets, ref address, configurations.Length, (nint)p);
        }
    }

    // -----------------------------------------------------------------
    // P2P (requires either the signaling service or relay infrastructure
    // to actually negotiate the connection — see InitAuthentication)
    // -----------------------------------------------------------------

    public uint CreateListenSocketP2P(int localVirtualPort) =>
        SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(_nativeSockets, localVirtualPort, 0, nint.Zero);

    public unsafe uint CreateListenSocketP2P(int localVirtualPort, Configuration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        fixed (Configuration* p = configurations)
            return SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(_nativeSockets, localVirtualPort, configurations.Length, (nint)p);
    }

    public uint ConnectP2P(ref NetworkingIdentity identityRemote, int remoteVirtualPort) =>
        SteamAPI_ISteamNetworkingSockets_ConnectP2P(_nativeSockets, ref identityRemote, remoteVirtualPort, 0, nint.Zero);

    public unsafe uint ConnectP2P(ref NetworkingIdentity identityRemote, int remoteVirtualPort, Configuration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        fixed (Configuration* p = configurations)
            return SteamAPI_ISteamNetworkingSockets_ConnectP2P(_nativeSockets, ref identityRemote, remoteVirtualPort, configurations.Length, (nint)p);
    }

    public Result AcceptConnection(uint connection) =>
        SteamAPI_ISteamNetworkingSockets_AcceptConnection(_nativeSockets, connection);

    public bool CloseConnection(uint connection) => CloseConnection(connection, 0, string.Empty, enableLinger: false);

    public bool CloseConnection(uint connection, int reason, string debug, bool enableLinger)
    {
        ArgumentNullException.ThrowIfNull(debug);
        if (debug.Length > Library.MaxCloseMessageLength)
            throw new ArgumentOutOfRangeException(nameof(debug), $"Length must be <= {Library.MaxCloseMessageLength}.");

        return SteamAPI_ISteamNetworkingSockets_CloseConnection(_nativeSockets, connection, reason, debug, enableLinger);
    }

    public bool CloseListenSocket(uint socket) =>
        SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_nativeSockets, socket);

    public bool SetConnectionUserData(uint peer, long userData) =>
        SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(_nativeSockets, peer, userData);

    public long GetConnectionUserData(uint peer) =>
        SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(_nativeSockets, peer);

    public void SetConnectionName(uint peer, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        SteamAPI_ISteamNetworkingSockets_SetConnectionName(_nativeSockets, peer, name);
    }

    public unsafe bool GetConnectionName(uint peer, out string name, int maxLength = 256)
    {
        if (maxLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
        byte* buf = stackalloc byte[maxLength];
        bool ok = SteamAPI_ISteamNetworkingSockets_GetConnectionName(_nativeSockets, peer, buf, maxLength);
        name = ok ? Marshal.PtrToStringUTF8((nint)buf) ?? string.Empty : string.Empty;
        return ok;
    }

    // -----------------------------------------------------------------
    // Sending
    // -----------------------------------------------------------------

    public Result SendMessageToConnection(uint connection, nint data, uint length) =>
        SendMessageToConnection(connection, data, length, SendFlags.Unreliable);

    public Result SendMessageToConnection(uint connection, nint data, uint length, SendFlags flags) =>
        SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(_nativeSockets, connection, data, length, (int)flags, nint.Zero);

    public Result SendMessageToConnection(uint connection, nint data, int length, SendFlags flags) =>
        SendMessageToConnection(connection, data, (uint)length, flags);

    public Result SendMessageToConnection(uint connection, ReadOnlySpan<byte> data) =>
        SendMessageToConnection(connection, data, SendFlags.Unreliable);

    public unsafe Result SendMessageToConnection(uint connection, ReadOnlySpan<byte> data, SendFlags flags)
    {
        fixed (byte* p = data)
        {
            return SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(_nativeSockets, connection, (nint)p, (uint)data.Length, (int)flags, nint.Zero);
        }
    }

    public Result FlushMessagesOnConnection(uint connection) =>
        SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(_nativeSockets, connection);

    /// <summary>
    /// Batched send. Each <paramref name="messagePointers"/> entry must point
    /// at a <c>SteamNetworkingMessage_t</c> allocated via
    /// <see cref="NetworkingUtils.AllocateMessage"/>; the native side takes
    /// ownership and frees them.
    /// </summary>
    /// <param name="messagePointers">Array of native message pointers.</param>
    /// <param name="results">Output array (same length); each slot receives a
    /// positive message number on success or a negative <see cref="Result"/> code on failure.</param>
    public unsafe void SendMessages(ReadOnlySpan<nint> messagePointers, Span<long> results)
    {
        if (results.Length < messagePointers.Length)
            throw new ArgumentException($"results buffer too small ({results.Length} < {messagePointers.Length}).", nameof(results));

        fixed (nint* msgs = messagePointers)
        fixed (long* outs = results)
        {
            SteamAPI_ISteamNetworkingSockets_SendMessages(_nativeSockets, messagePointers.Length, (nint)msgs, (nint)outs);
        }
    }

    /// <summary>
    /// Configures per-lane priorities and weights for a connection. Must be
    /// called before sending any messages on lanes > 0.
    /// </summary>
    public unsafe Result ConfigureConnectionLanes(uint connection, ReadOnlySpan<int> lanePriorities, ReadOnlySpan<ushort> laneWeights)
    {
        if (laneWeights.Length != 0 && laneWeights.Length != lanePriorities.Length)
            throw new ArgumentException("laneWeights must be empty or the same length as lanePriorities.", nameof(laneWeights));

        fixed (int* prios = lanePriorities)
        fixed (ushort* weights = laneWeights)
        {
            return SteamAPI_ISteamNetworkingSockets_ConfigureConnectionLanes(
                _nativeSockets, connection, lanePriorities.Length,
                (nint)prios,
                laneWeights.IsEmpty ? nint.Zero : (nint)weights);
        }
    }

    // -----------------------------------------------------------------
    // Authentication (relay / SDR identity bootstrap)
    // -----------------------------------------------------------------

    /// <summary>Triggers asynchronous initialization of the local authentication identity (cert exchange with the auth service).</summary>
    public Availability InitAuthentication() =>
        SteamAPI_ISteamNetworkingSockets_InitAuthentication(_nativeSockets);

    /// <summary>
    /// Returns the current authentication availability. When
    /// <paramref name="details"/> is non-default, it receives the full status
    /// payload (including diagnostic message).
    /// </summary>
    public Availability GetAuthenticationStatus(ref AuthenticationStatus details) =>
        SteamAPI_ISteamNetworkingSockets_GetAuthenticationStatus(_nativeSockets, ref details);

    // -----------------------------------------------------------------
    // Status
    // -----------------------------------------------------------------

    public bool GetConnectionInfo(uint connection, ref ConnectionInfo info) =>
        SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(_nativeSockets, connection, ref info);

    public Result GetConnectionStatus(uint connection, ref ConnectionStatus status) =>
        SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(_nativeSockets, connection, ref status, 0, nint.Zero);

    public unsafe Result GetConnectionStatus(uint connection, ref ConnectionStatus status, Span<ConnectionLaneStatus> lanes)
    {
        fixed (ConnectionLaneStatus* p = lanes)
        {
            return SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(_nativeSockets, connection, ref status, lanes.Length, (nint)p);
        }
    }

    /// <summary>
    /// Returns a multi-line, human-readable diagnostic status report for the
    /// connection. Native return contract:
    /// <list type="bullet">
    /// <item><c>0</c> — success; <paramref name="status"/> is populated.</item>
    /// <item><c>-1</c> — bad connection handle; <paramref name="status"/> is empty.</item>
    /// <item><c>&gt;0</c> — buffer too small; <paramref name="status"/> is empty and the
    /// return value is the required buffer size. Retry with <paramref name="bufferLength"/>
    /// at least that large.</item>
    /// </list>
    /// </summary>
    public unsafe int GetDetailedConnectionStatus(uint connection, out string status, int bufferLength = 4096)
    {
        if (bufferLength <= 0) throw new ArgumentOutOfRangeException(nameof(bufferLength));
        byte* buf = stackalloc byte[bufferLength];
        int rc = SteamAPI_ISteamNetworkingSockets_GetDetailedConnectionStatus(_nativeSockets, connection, buf, bufferLength);
        status = rc == 0 ? Marshal.PtrToStringUTF8((nint)buf) ?? string.Empty : string.Empty;
        return rc;
    }

    public bool GetListenSocketAddress(uint socket, ref Address address) =>
        SteamAPI_ISteamNetworkingSockets_GetListenSocketAddress(_nativeSockets, socket, ref address);

    public bool CreateSocketPair(out uint connectionLeft, out uint connectionRight, bool useNetworkLoopback, ref NetworkingIdentity identityLeft, ref NetworkingIdentity identityRight) =>
        SteamAPI_ISteamNetworkingSockets_CreateSocketPair(_nativeSockets, out connectionLeft, out connectionRight, useNetworkLoopback, ref identityLeft, ref identityRight);

    public bool GetIdentity(ref NetworkingIdentity identity) =>
        SteamAPI_ISteamNetworkingSockets_GetIdentity(_nativeSockets, ref identity);

    // -----------------------------------------------------------------
    // Poll groups
    // -----------------------------------------------------------------

    public uint CreatePollGroup() => SteamAPI_ISteamNetworkingSockets_CreatePollGroup(_nativeSockets);

    public bool DestroyPollGroup(uint pollGroup) =>
        SteamAPI_ISteamNetworkingSockets_DestroyPollGroup(_nativeSockets, pollGroup);

    public bool SetConnectionPollGroup(uint connection, uint pollGroup) =>
        SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup(_nativeSockets, connection, pollGroup);

    // -----------------------------------------------------------------
    // Certificates
    // -----------------------------------------------------------------

    /// <summary>
    /// Installs a certificate + private-key pair that was minted offline (e.g. by the
    /// bundled <c>steamnetworkingsockets_certtool</c>). Both blobs are expected in
    /// PEM-encoded form. The <paramref name="privateKey"/> buffer is wiped after
    /// loading — pass a copy if you need to retain the bytes afterwards.
    /// </summary>
    public unsafe bool SetCertificateAndPrivateKey(ReadOnlySpan<byte> certificate, Span<byte> privateKey, out string errorMessage)
    {
        byte* errBuf = stackalloc byte[Library.MaxErrorMessageLength];
        bool ok;
        fixed (byte* cert = certificate)
        fixed (byte* key = privateKey)
        {
            ok = SteamAPI_ISteamNetworkingSockets_SetCertificateAndPrivateKey(_nativeSockets, (nint)cert, certificate.Length, (nint)key, privateKey.Length, errBuf);
        }
        errorMessage = ok ? string.Empty : Marshal.PtrToStringUTF8((nint)errBuf) ?? string.Empty;
        return ok;
    }

    /// <summary>
    /// Registers an additional trusted root CA at runtime. <paramref name="base64Cert"/>
    /// is the body of a PEM-like blob (just the base64-encoded protobuf — no
    /// <c>-----BEGIN-----</c>/<c>-----END-----</c> wrappers). Combine with a native
    /// build that defines <c>STEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS</c>
    /// so your CA replaces Valve's as the trust anchor for non-Steam deployments.
    /// </summary>
    public unsafe bool AddTrustedRootCA(string base64Cert, out string errorMessage)
    {
        byte* errBuf = stackalloc byte[Library.MaxErrorMessageLength];
        bool ok = SteamAPI_ISteamNetworkingSockets_AddTrustedRootCA(_nativeSockets, base64Cert, errBuf);
        errorMessage = ok ? string.Empty : Marshal.PtrToStringUTF8((nint)errBuf) ?? string.Empty;
        return ok;
    }

    /// <summary>
    /// Builds a certificate signing request. On success, <paramref name="blob"/>
    /// is populated and <paramref name="errorMessage"/> is empty. Pass a
    /// <paramref name="blob"/> length of at least 2048 to be safe.
    /// </summary>
    public unsafe bool GetCertificateRequest(Span<byte> blob, out int blobLength, out string errorMessage)
    {
        blobLength = blob.Length;
        byte* errBuf = stackalloc byte[Library.MaxErrorMessageLength];
        bool ok;
        fixed (byte* p = blob)
        {
            ok = SteamAPI_ISteamNetworkingSockets_GetCertificateRequest(_nativeSockets, ref blobLength, (nint)p, errBuf);
        }
        errorMessage = ok ? string.Empty : Marshal.PtrToStringUTF8((nint)errBuf) ?? string.Empty;
        return ok;
    }

    /// <summary>Installs a certificate previously obtained by signing the request from <see cref="GetCertificateRequest"/>.</summary>
    public unsafe bool SetCertificate(ReadOnlySpan<byte> certificate, out string errorMessage)
    {
        byte* errBuf = stackalloc byte[Library.MaxErrorMessageLength];
        bool ok;
        fixed (byte* p = certificate)
        {
            ok = SteamAPI_ISteamNetworkingSockets_SetCertificate(_nativeSockets, (nint)p, certificate.Length, errBuf);
        }
        errorMessage = ok ? string.Empty : Marshal.PtrToStringUTF8((nint)errBuf) ?? string.Empty;
        return ok;
    }

    public void RunCallbacks() => SteamAPI_ISteamNetworkingSockets_RunCallbacks(_nativeSockets);

    // -----------------------------------------------------------------
    // Receive — Span-based callback model. Each message is released
    // automatically after the callback returns; the span is only valid
    // for the duration of that call.
    // -----------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReceiveMessagesOnConnection(uint connection, MessageCallback callback, int maxMessages)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (maxMessages > Library.MaxMessagesPerBatch)
            throw new ArgumentOutOfRangeException(nameof(maxMessages), $"Must be <= {Library.MaxMessagesPerBatch}.");

        var pointers = MessageBuffer.Rent();
        int count;
        unsafe
        {
            fixed (nint* p = pointers)
            {
                count = SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(_nativeSockets, connection, (nint)p, maxMessages);
            }
        }
        DispatchAndRelease(pointers, count, callback);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReceiveMessagesOnPollGroup(uint pollGroup, MessageCallback callback, int maxMessages)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (maxMessages > Library.MaxMessagesPerBatch)
            throw new ArgumentOutOfRangeException(nameof(maxMessages), $"Must be <= {Library.MaxMessagesPerBatch}.");

        var pointers = MessageBuffer.Rent();
        int count;
        unsafe
        {
            fixed (nint* p = pointers)
            {
                count = SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(_nativeSockets, pollGroup, (nint)p, maxMessages);
            }
        }
        DispatchAndRelease(pointers, count, callback);
    }

    private static unsafe void DispatchAndRelease(nint[] pointers, int count, MessageCallback callback)
    {
        for (int i = 0; i < count; i++)
        {
            var msg = (NetworkingMessage*)pointers[i];
            callback(in *msg);
            NetworkingMessage.Release(pointers[i]);
        }
    }

    private static class MessageBuffer
    {
        [ThreadStatic]
        private static nint[]? _buffer;

        public static nint[] Rent() => _buffer ??= new nint[Library.MaxMessagesPerBatch];
    }

    // -----------------------------------------------------------------
    // Native (steamnetworkingsockets_flat.h)
    // -----------------------------------------------------------------

    [LibraryImport(LibraryName)]
    internal static partial nint SteamAPI_SteamNetworkingSockets_v009();

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(nint sockets, ref Address address, int configurationsCount, nint configurations);

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(nint sockets, ref Address address, int configurationsCount, nint configurations);

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(nint sockets, int localVirtualPort, int configurationsCount, nint configurations);

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_ISteamNetworkingSockets_ConnectP2P(nint sockets, ref NetworkingIdentity identityRemote, int remoteVirtualPort, int configurationsCount, nint configurations);

    [LibraryImport(LibraryName)]
    internal static partial Result SteamAPI_ISteamNetworkingSockets_AcceptConnection(nint sockets, uint connection);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_CloseConnection(nint sockets, uint peer, int reason, string debug, [MarshalAs(UnmanagedType.U1)] bool enableLinger);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_CloseListenSocket(nint sockets, uint socket);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(nint sockets, uint peer, long userData);

    [LibraryImport(LibraryName)]
    internal static partial long SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(nint sockets, uint peer);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void SteamAPI_ISteamNetworkingSockets_SetConnectionName(nint sockets, uint peer, string name);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool SteamAPI_ISteamNetworkingSockets_GetConnectionName(nint sockets, uint peer, byte* name, int maxLength);

    [LibraryImport(LibraryName)]
    internal static partial Result SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(nint sockets, uint connection, nint data, uint length, int flags, nint outMessageNumber);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_ISteamNetworkingSockets_SendMessages(nint sockets, int nMessages, nint pMessages, nint pOutMessageNumberOrResult);

    [LibraryImport(LibraryName)]
    internal static partial Result SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(nint sockets, uint connection);

    [LibraryImport(LibraryName)]
    internal static partial Result SteamAPI_ISteamNetworkingSockets_ConfigureConnectionLanes(nint sockets, uint connection, int nNumLanes, nint pLanePriorities, nint pLaneWeights);

    [LibraryImport(LibraryName)]
    internal static partial Availability SteamAPI_ISteamNetworkingSockets_InitAuthentication(nint sockets);

    [LibraryImport(LibraryName)]
    internal static partial Availability SteamAPI_ISteamNetworkingSockets_GetAuthenticationStatus(nint sockets, ref AuthenticationStatus details);

    [LibraryImport(LibraryName)]
    internal static partial int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(nint sockets, uint connection, nint messages, int maxMessages);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(nint sockets, uint connection, ref ConnectionInfo info);

    [LibraryImport(LibraryName)]
    internal static partial Result SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(nint sockets, uint connection, ref ConnectionStatus status, int nLanes, nint pLanes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial int SteamAPI_ISteamNetworkingSockets_GetDetailedConnectionStatus(nint sockets, uint connection, byte* status, int statusLength);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_GetListenSocketAddress(nint sockets, uint socket, ref Address address);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool SteamAPI_ISteamNetworkingSockets_SetCertificateAndPrivateKey(nint sockets, nint certBytes, int certLength, nint privateKeyBytes, int privateKeyLength, byte* errMsg);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool SteamAPI_ISteamNetworkingSockets_AddTrustedRootCA(nint sockets, string pszBase64Cert, byte* errMsg);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool SteamAPI_ISteamNetworkingSockets_GetCertificateRequest(nint sockets, ref int pcbBlob, nint pBlob, byte* errMsg);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool SteamAPI_ISteamNetworkingSockets_SetCertificate(nint sockets, nint pCertificate, int cbCertificate, byte* errMsg);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_ISteamNetworkingSockets_RunCallbacks(nint sockets);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_CreateSocketPair(nint sockets, out uint connectionLeft, out uint connectionRight, [MarshalAs(UnmanagedType.U1)] bool useNetworkLoopback, ref NetworkingIdentity identityLeft, ref NetworkingIdentity identityRight);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_GetIdentity(nint sockets, ref NetworkingIdentity identity);

    [LibraryImport(LibraryName)]
    internal static partial uint SteamAPI_ISteamNetworkingSockets_CreatePollGroup(nint sockets);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_DestroyPollGroup(nint sockets, uint pollGroup);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup(nint sockets, uint connection, uint pollGroup);

    [LibraryImport(LibraryName)]
    internal static partial int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(nint sockets, uint pollGroup, nint messages, int maxMessages);
}
