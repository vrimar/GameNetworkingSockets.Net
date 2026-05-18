using System.Reflection;
using System.Runtime.InteropServices;
using Valve.Sockets;

// Minimal AOT-friendly entry point.
//
// Goal: validate that GameNetworkingSockets.Net's bindings compile under
// PublishAot without trim/AOT warnings. Library.Initialize is not invoked
// here because the native library may not be deployed alongside the AOT
// executable in all CI lanes; the test is purely compile-time + boot-time.

var revision = typeof(NetworkingSockets).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .FirstOrDefault(a => a.Key == "GameNetworkingSocketsRevision")?.Value;

Console.WriteLine($"GameNetworkingSockets.Net AOT sample. Pinned upstream revision: {revision ?? "(unknown)"}.");

// Exercise a handful of value-type APIs to keep them rooted under trimming.
var addressSize = Marshal.SizeOf<Address>();
var identitySize = Marshal.SizeOf<NetworkingIdentity>();
var sendFlags = SendFlags.Reliable | SendFlags.NoNagle;

Console.WriteLine($"sizeof(Address)={addressSize}, sizeof(NetworkingIdentity)={identitySize}, send flags = {sendFlags} ({(int)sendFlags}).");

return 0;
