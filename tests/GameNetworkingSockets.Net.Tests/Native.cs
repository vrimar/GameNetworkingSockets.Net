using Xunit;

// GNS state is process-global: one class's Deinitialize would tear down another's sockets mid-test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GameNetworkingSockets.Net.Tests;

internal static class Native
{
    public static bool Present()
    {
        var dir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(dir, "GameNetworkingSockets.dll"))
            || File.Exists(Path.Combine(dir, "libGameNetworkingSockets.so"))
            || File.Exists(Path.Combine(dir, "libGameNetworkingSockets.dylib"));
    }
}
