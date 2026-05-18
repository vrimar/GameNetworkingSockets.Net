using System.Runtime.InteropServices;
using System.Threading;

namespace Valve.Sockets;

/// <summary>
/// Managed wrapper around <c>ISteamNetworkingUtils</c>. Owns the global
/// status/debug callback registrations and configuration setters.
/// </summary>
public sealed partial class NetworkingUtils : IDisposable
{
    private const string LibraryName = Library.LibraryName;

    private nint _nativeUtils;

    // Hold managed delegate references so the function pointers passed to
    // native code do not get collected. Replaced atomically on each Set*.
    private StatusCallback? _statusCallback;
    private DebugCallback? _debugCallback;
    private AuthenticationStatusCallback? _authCallback;
    private RelayNetworkStatusCallback? _relayCallback;

    public NetworkingUtils()
    {
        _nativeUtils = SteamAPI_SteamNetworkingUtils_v003();

        if (_nativeUtils == IntPtr.Zero)
            throw new InvalidOperationException("SteamAPI_SteamNetworkingUtils_v003 returned null. Did Library.Initialize() succeed?");
    }

    /// <summary>Raw native pointer for advanced interop. Do not free.</summary>
    public nint NativeHandle => _nativeUtils;

    public long Time => SteamAPI_ISteamNetworkingUtils_GetLocalTimestamp(_nativeUtils);

    public bool SetStatusCallback(StatusCallback? callback)
    {
        _statusCallback = callback;
        return SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(_nativeUtils, callback);
    }

    public void SetDebugCallback(DebugType detailLevel, DebugCallback? callback)
    {
        _debugCallback = callback;
        SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(_nativeUtils, detailLevel, callback);
    }

    public bool SetAuthenticationStatusCallback(AuthenticationStatusCallback? callback)
    {
        _authCallback = callback;
        return SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetAuthenticationStatusChanged(_nativeUtils, callback);
    }

    public bool SetRelayNetworkStatusCallback(RelayNetworkStatusCallback? callback)
    {
        _relayCallback = callback;
        return SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamRelayNetworkStatusChanged(_nativeUtils, callback);
    }

    // -----------------------------------------------------------------
    // Typed configuration value setters (convenience over the generic
    // SetConfigurationValue). Mirrors the per-type setters in the C API.
    // -----------------------------------------------------------------

    public bool SetGlobalConfigValueInt32(ConfigurationValue value, int v) =>
        SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(_nativeUtils, value, v);

    public bool SetGlobalConfigValueFloat(ConfigurationValue value, float v) =>
        SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueFloat(_nativeUtils, value, v);

    public bool SetGlobalConfigValueString(ConfigurationValue value, string v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueString(_nativeUtils, value, v);
    }

    public bool SetGlobalConfigValuePtr(ConfigurationValue value, nint v) =>
        SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValuePtr(_nativeUtils, value, v);

    public bool SetConnectionConfigValueInt32(uint connection, ConfigurationValue value, int v) =>
        SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueInt32(_nativeUtils, connection, value, v);

    public bool SetConnectionConfigValueFloat(uint connection, ConfigurationValue value, float v) =>
        SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueFloat(_nativeUtils, connection, value, v);

    public bool SetConnectionConfigValueString(uint connection, ConfigurationValue value, string v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueString(_nativeUtils, connection, value, v);
    }

    // -----------------------------------------------------------------
    // Steam Datagram Relay (SDR). Only meaningful when running against a
    // Steam-connected backend; in the open-source standalone build these
    // APIs are no-ops or return Failed.
    // -----------------------------------------------------------------

    /// <summary>Kicks off ping-measurement and relay-config fetch. Wait for <see cref="GetRelayNetworkStatus"/> to report <c>Current</c>.</summary>
    public void InitRelayNetworkAccess() =>
        SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(_nativeUtils);

    public Availability GetRelayNetworkStatus(ref RelayNetworkStatus details) =>
        SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(_nativeUtils, ref details);

    /// <summary>
    /// Allocates an outgoing message owned by the native side. The returned
    /// pointer must be either submitted via <see cref="NetworkingSockets.SendMessages"/>
    /// (which transfers ownership) or released via
    /// <see cref="NetworkingMessage.Release"/>.
    /// </summary>
    public nint AllocateMessage(int bufferSize) =>
        SteamAPI_ISteamNetworkingUtils_AllocateMessage(_nativeUtils, bufferSize);

    public bool SetConfigurationValue(
        ConfigurationValue configurationValue,
        ConfigurationScope configurationScope,
        nint scopeObject,
        ConfigurationDataType dataType,
        nint value) =>
        SteamAPI_ISteamNetworkingUtils_SetConfigValue(_nativeUtils, configurationValue, configurationScope, scopeObject, dataType, value);

    public bool SetConfigurationValue(
        in Configuration configuration,
        ConfigurationScope configurationScope,
        nint scopeObject) =>
        SteamAPI_ISteamNetworkingUtils_SetConfigValueStruct(_nativeUtils, in configuration, configurationScope, scopeObject);

    public ConfigurationValueResult GetConfigurationValue(
        ConfigurationValue configurationValue,
        ConfigurationScope configurationScope,
        nint scopeObject,
        ref ConfigurationDataType dataType,
        nint result,
        ref nuint resultLength) =>
        SteamAPI_ISteamNetworkingUtils_GetConfigValue(_nativeUtils, configurationValue, configurationScope, scopeObject, ref dataType, result, ref resultLength);

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref _nativeUtils, IntPtr.Zero);
        if (h != IntPtr.Zero)
        {
            // Detach our callbacks so the native side can't fire them after
            // disposal (with the managed delegates due for collection).
            SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(h, null);
            SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(h, DebugType.None, null);
            SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetAuthenticationStatusChanged(h, null);
            SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamRelayNetworkStatusChanged(h, null);
        }
        _statusCallback = null;
        _debugCallback = null;
        _authCallback = null;
        _relayCallback = null;
        GC.SuppressFinalize(this);
    }

    ~NetworkingUtils() => Dispose();

    // -----------------------------------------------------------------
    // Native (steamnetworkingsockets_flat.h)
    // -----------------------------------------------------------------

    [LibraryImport(LibraryName)]
    internal static partial nint SteamAPI_SteamNetworkingUtils_v003();

    [LibraryImport(LibraryName)]
    internal static partial long SteamAPI_ISteamNetworkingUtils_GetLocalTimestamp(nint utils);

    // Delegate marshalling is not supported by [LibraryImport] (it requires
    // [UnmanagedFunctionPointer]). Keep these two on [DllImport].

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(nint utils, StatusCallback? callback);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(nint utils, DebugType detailLevel, DebugCallback? callback);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetAuthenticationStatusChanged(nint utils, AuthenticationStatusCallback? callback);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamRelayNetworkStatusChanged(nint utils, RelayNetworkStatusCallback? callback);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetConfigValue(nint utils, ConfigurationValue value, ConfigurationScope scope, nint scopeObject, ConfigurationDataType dataType, nint pArg);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetConfigValueStruct(nint utils, in Configuration configuration, ConfigurationScope scope, nint scopeObject);

    [LibraryImport(LibraryName)]
    internal static partial ConfigurationValueResult SteamAPI_ISteamNetworkingUtils_GetConfigValue(nint utils, ConfigurationValue value, ConfigurationScope scope, nint scopeObject, ref ConfigurationDataType dataType, nint result, ref nuint resultLength);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(nint utils, ConfigurationValue value, int val);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueFloat(nint utils, ConfigurationValue value, float val);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueString(nint utils, ConfigurationValue value, string val);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValuePtr(nint utils, ConfigurationValue value, nint val);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueInt32(nint utils, uint connection, ConfigurationValue value, int val);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueFloat(nint utils, uint connection, ConfigurationValue value, float val);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueString(nint utils, uint connection, ConfigurationValue value, string val);

    [LibraryImport(LibraryName)]
    internal static partial void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(nint utils);

    [LibraryImport(LibraryName)]
    internal static partial Availability SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(nint utils, ref RelayNetworkStatus details);

    [LibraryImport(LibraryName)]
    internal static partial nint SteamAPI_ISteamNetworkingUtils_AllocateMessage(nint utils, int cbAllocateBuffer);
}
