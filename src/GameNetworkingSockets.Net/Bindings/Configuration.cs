using System.Runtime.InteropServices;

namespace Valve.Sockets;

/// <summary>
/// Single configuration option payload accepted by
/// <see cref="NetworkingSockets.CreateListenSocket(ref Address, Configuration[])"/>
/// and <see cref="NetworkingUtils.SetConfigurationValue(Configuration, ConfigurationScope, IntPtr)"/>.
/// Mirrors <c>SteamNetworkingConfigValue_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Configuration
{
    public ConfigurationValue value;
    public ConfigurationDataType dataType;
    public ConfigurationData data;

    [StructLayout(LayoutKind.Explicit)]
    public struct ConfigurationData
    {
        [FieldOffset(0)] public int Int32;
        [FieldOffset(0)] public long Int64;
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public nint String;
        [FieldOffset(0)] public nint FunctionPtr;
    }
}
