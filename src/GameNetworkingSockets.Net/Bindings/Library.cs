using System.Runtime.InteropServices;
using System.Threading;

namespace Valve.Sockets;

/// <summary>
/// Native <c>malloc</c> replacement. Called from arbitrary internal threads.
/// Must return a pointer aligned for any standard type, or <c>0</c> on failure.
/// Use <see cref="Library.SetCustomMemoryAllocator"/> to install.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate nint MallocCallback(nuint size);

/// <summary>
/// Native <c>free</c> replacement. Must accept <c>0</c> as a no-op (matching
/// the C <c>free(NULL)</c> contract).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void FreeCallback(nint ptr);

/// <summary>
/// Native <c>realloc</c> replacement. When <paramref name="ptr"/> is <c>0</c>
/// behaves like malloc; when <paramref name="size"/> is <c>0</c> behaves like free.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate nint ReallocCallback(nint ptr, nuint size);

/// <summary>
/// Top-level entry point. <see cref="Initialize()"/> must succeed before any
/// <see cref="NetworkingSockets"/> or <see cref="NetworkingUtils"/> instance
/// is constructed, and <see cref="Deinitialize"/> must be called at exit.
/// </summary>
public static partial class Library
{
    /// <summary>
    /// Native library name passed to <c>[LibraryImport]</c>. .NET maps this
    /// to <c>GameNetworkingSockets.dll</c> / <c>libGameNetworkingSockets.so</c>
    /// / <c>libGameNetworkingSockets.dylib</c> as appropriate.
    /// </summary>
    internal const string LibraryName = "GameNetworkingSockets";

    public const int MaxCloseMessageLength = 128;
    public const int MaxConnectionDescriptionLength = 128;
    public const int MaxErrorMessageLength = 1024;
    public const int MaxMessagesPerBatch = 256;
    public const int MaxMessageSize = 512 * 1024;
    public const int SocketsCallbacks = 1220;

    /// <summary>Initializes the library. Returns true on success.</summary>
    public static bool Initialize() => Initialize(out _);

    /// <summary>
    /// Initializes the library, capturing any failure message into
    /// <paramref name="errorMessage"/>. <paramref name="errorMessage"/> is
    /// non-null only when the call returns false.
    /// </summary>
    public static unsafe bool Initialize(out string? errorMessage)
    {
        byte* buf = stackalloc byte[MaxErrorMessageLength];
        bool ok = GameNetworkingSockets_Init(null, buf);
        errorMessage = ok ? null : Marshal.PtrToStringUTF8((nint)buf);
        return ok;
    }

    /// <summary>
    /// Initializes the library bound to the supplied identity, capturing any
    /// failure message into <paramref name="errorMessage"/>.
    /// </summary>
    public static unsafe bool Initialize(ref NetworkingIdentity identity, out string? errorMessage)
    {
        byte* buf = stackalloc byte[MaxErrorMessageLength];
        bool ok;
        fixed (NetworkingIdentity* p = &identity)
        {
            ok = GameNetworkingSockets_Init(p, buf);
        }
        errorMessage = ok ? null : Marshal.PtrToStringUTF8((nint)buf);
        return ok;
    }

    public static void Deinitialize() => GameNetworkingSockets_Kill();

    // Strong managed references for the installed allocator callbacks. The
    // native side stores raw function pointers obtained via the runtime's
    // delegate→cdecl thunk, so the managed delegate objects must outlive the
    // library — otherwise the GC will collect them and the next allocation
    // will crash.
    private static MallocCallback? s_customMalloc;
    private static FreeCallback? s_customFree;
    private static ReallocCallback? s_customRealloc;

    /// <summary>
    /// Installs custom allocator callbacks used by GameNetworkingSockets
    /// internals. Must be called <b>before</b> <see cref="Initialize()"/>;
    /// the native library cannot mix allocators at runtime because pointers
    /// previously handed out by the default allocator cannot be safely freed
    /// by a different one.
    /// </summary>
    /// <remarks>
    /// <para>All three callbacks are required. They will be invoked from
    /// arbitrary internal threads, including high-frequency code paths, so
    /// keep them allocation-free and re-entrant.</para>
    /// <para>This is a process-global, one-way setting. There is no API to
    /// reset to the default allocator; the binding intentionally exposes no
    /// such method.</para>
    /// </remarks>
    public static void SetCustomMemoryAllocator(MallocCallback malloc, FreeCallback free, ReallocCallback realloc)
    {
        ArgumentNullException.ThrowIfNull(malloc);
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(realloc);

        // Publish to the static fields first so the GC roots them before we
        // hand the function pointers to the native side.
        Volatile.Write(ref s_customMalloc, malloc);
        Volatile.Write(ref s_customFree, free);
        Volatile.Write(ref s_customRealloc, realloc);

        SteamNetworkingSockets_SetCustomMemoryAllocator(malloc, free, realloc);
    }

    // -----------------------------------------------------------------
    // Native (steamnetworkingsockets.h)
    // -----------------------------------------------------------------

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool GameNetworkingSockets_Init(NetworkingIdentity* identity, byte* errorMessage);

    [LibraryImport(LibraryName)]
    private static partial void GameNetworkingSockets_Kill();

    // Delegate parameters are not supported by [LibraryImport] — they need
    // [UnmanagedFunctionPointer] cdecl thunks which the source generator
    // doesn't emit. Use classic [DllImport]; it's still AOT-compatible.
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamNetworkingSockets_SetCustomMemoryAllocator(
        MallocCallback pfnMalloc,
        FreeCallback pfnFree,
        ReallocCallback pfnRealloc);
}
