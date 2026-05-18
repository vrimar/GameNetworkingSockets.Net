using System.Threading;
using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

/// <summary>
/// Custom-allocator tests live in their own collection because they install
/// a process-global, one-way setting. The shared collection serializes them
/// with each other, but they can still run before/after the other smoke tests
/// in the same process — that's fine: the test installs an allocator that's
/// a thin passthrough to <c>NativeMemory</c>, which is byte-for-byte
/// compatible with what GameNetworkingSockets uses by default.
/// </summary>
public class CustomMemoryAllocatorTests
{
    // Static so the delegates can be installed exactly once per process. The
    // delegate field references must outlive the library (the native side
    // keeps the raw function pointers, not the managed delegates).
    private static long s_mallocCount;
    private static long s_freeCount;
    private static long s_reallocCount;
    private static readonly MallocCallback s_malloc = static size =>
    {
        Interlocked.Increment(ref s_mallocCount);
        unsafe { return (nint)System.Runtime.InteropServices.NativeMemory.Alloc(size); }
    };
    private static readonly FreeCallback s_free = static ptr =>
    {
        if (ptr == 0) return;
        Interlocked.Increment(ref s_freeCount);
        unsafe { System.Runtime.InteropServices.NativeMemory.Free((void*)ptr); }
    };
    private static readonly ReallocCallback s_realloc = static (ptr, size) =>
    {
        Interlocked.Increment(ref s_reallocCount);
        unsafe { return (nint)System.Runtime.InteropServices.NativeMemory.Realloc((void*)ptr, size); }
    };

    [SkippableFact]
    public void SetCustomMemoryAllocator_StoresFunctionPointers()
    {
        Skip.IfNot(NativeLibraryPresent(), "GameNetworkingSockets native library not deployed.");

        // The actual contract validated by this test:
        //  1. The DllImport signature accepts our delegate types without throwing.
        //  2. The managed delegate references stored inside Library survive a
        //     GC pass — otherwise the next call from native code would crash.
        Library.SetCustomMemoryAllocator(s_malloc, s_free, s_realloc);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // We can't easily prove "the native side held the pointers" without
        // calling Library.Initialize and observing the counter — which we do
        // below, but only if no prior test in this process has already
        // initialized the library (after which switching allocators is UB
        // per upstream docs).
        Assert.True(Library.Initialize(out var error), $"Initialize failed: {error}");
        try
        {
            // Per upstream docs, "MOST but not all" allocations route through
            // the custom allocator — Library.Initialize alone may not trigger
            // any. Force a per-connection allocation by creating a poll group.
            var preMalloc = s_mallocCount;
            var preRealloc = s_reallocCount;

            var sockets = new NetworkingSockets();
            var pollGroup = sockets.CreatePollGroup();
            Assert.NotEqual(0u, pollGroup);
            sockets.DestroyPollGroup(pollGroup);

            Assert.True((s_mallocCount + s_reallocCount) > (preMalloc + preRealloc),
                $"Expected custom allocator to be invoked by poll-group create/destroy; got Δmalloc={s_mallocCount - preMalloc}, Δrealloc={s_reallocCount - preRealloc}.");
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
