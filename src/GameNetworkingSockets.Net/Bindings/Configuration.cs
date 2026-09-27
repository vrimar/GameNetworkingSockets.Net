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

    /// <summary>An <see cref="ConfigurationDataType.Int32"/> option.</summary>
    public static Configuration Int32(ConfigurationValue value, int data) =>
        new() { value = value, dataType = ConfigurationDataType.Int32, data = new() { Int32 = data } };

    /// <summary>An <see cref="ConfigurationDataType.Int64"/> option.</summary>
    public static Configuration Int64(ConfigurationValue value, long data) =>
        new() { value = value, dataType = ConfigurationDataType.Int64, data = new() { Int64 = data } };

    /// <summary>A <see cref="ConfigurationDataType.Float"/> option.</summary>
    public static Configuration Float(ConfigurationValue value, float data) =>
        new() { value = value, dataType = ConfigurationDataType.Float, data = new() { Float = data } };

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
