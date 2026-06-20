# GameNetworkingSockets.Net

Cross-platform .NET bindings for Valve's [GameNetworkingSockets](https://github.com/ValveSoftware/GameNetworkingSockets) (the open-source standalone variant of Steam's networking transport), with native binaries bundled for `win-x64`, `linux-x64`, and `osx-arm64`.

The managed surface is derived from Stanislav Denisov's [ValveSockets-CSharp](https://github.com/nxrighthere/ValveSockets-CSharp) wrapper, modernized for [`LibraryImport`](https://learn.microsoft.com/dotnet/standard/native-interop/source-generated-marshalling) (source-generated P/Invoke, AOT-compatible) and updated against the current `steamnetworkingsockets_flat.h` API.

## Packages

| Package | Contents |
|---|---|
| `GameNetworkingSockets.Net` | Managed bindings (`net8.0`/`net10.0`) plus `GameNetworkingSockets.dll` / `libGameNetworkingSockets.so` / `libGameNetworkingSockets.dylib` under `runtimes/{rid}/native/`. AOT-compatible. |

## Quick start

```csharp
using Valve.Sockets;

if (!Library.Initialize(out var error))
    throw new Exception(error);

var sockets = new NetworkingSockets();
using var utils = new NetworkingUtils();

utils.SetDebugCallback(DebugType.Important, (type, msg) => Console.WriteLine($"[{type}] {msg}"));

var address = default(Address);
address.SetLocalHost(7777);

var listenSocket = sockets.CreateListenSocket(ref address);

while (!Console.KeyAvailable)
{
    sockets.RunCallbacks();
    Thread.Sleep(1);
}

sockets.CloseListenSocket(listenSocket);
Library.Deinitialize();
```

## Identity & authentication

`GameNetworkingSockets.Net` ships with the **dynamic self-signed cert** build flag (`STEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS`) enabled, so Valve's hardcoded root CA is not installed. Consumers running outside the Steam ecosystem provide their own trust anchor.

Two API entry points support this:

- `NetworkingSockets.SetCertificateAndPrivateKey(cert, key, out err)` — installs a long-lived keypair (e.g. one minted by the certtool below). Server processes call this to take on a durable cryptographic identity.
- `NetworkingSockets.AddTrustedRootCA(base64Cert, out err)` — registers a CA whose-signed certs will be accepted from peers during the handshake. Clients call this at startup so they can verify the server's identity.

### Offline cert tooling

The package ships `steamnetworkingsockets_certtool` under `tools/{rid}/` for build-time use. Resolve it from a `PackageReference`:

```xml
<PackageReference Include="GameNetworkingSockets.Net" Version="X.Y.Z" GeneratePathProperty="true" />
```

Then invoke it during your build:

```
$(PkgGameNetworkingSockets_Net)/tools/win-x64/steamnetworkingsockets_certtool.exe gen_keypair
```

Typical mint flow (run once, commit `ca.pub` to source control; keep `ca.priv` secret):

```bash
# Generate a CA keypair (writes the priv key to stdout).
$tool gen_keypair > ca.priv

# Mint a server cert signed by that CA.
$tool create_cert --ca-priv-key-file ca.priv --pub-key-file server.pub --expiry 730 > server.cert
```

Load the resulting blobs from your app at runtime — no Valve infrastructure required.

## Runtime dependencies

The native libraries inside the NuGet package have these external runtime dependencies:

| RID | Bundled in package | Required on host |
|---|---|---|
| `win-x64` | OpenSSL, protobuf, abseil (DLLs ship next to `GameNetworkingSockets.dll`) | UCRT (universal on Win10+) |
| `linux-x64` | OpenSSL and protobuf (statically linked) | Standard C/C++ runtime libraries |
| `osx-arm64` | OpenSSL and protobuf (statically linked) | macOS system libraries |

The Unix build downloads protobuf 21.12 and OpenSSL 3.5.7 from their official
release archives, verifies their SHA-256 checksums, and builds both from source
as position-independent static libraries. This avoids coupling package
consumers to distribution or Homebrew native ABIs.

## Building from source

Prerequisites:

- .NET SDK 10 (see [`global.json`](global.json))
- CMake 3.15+, curl, and Perl
- A C++ toolchain (MSVC on Windows, gcc/clang on Linux, Apple clang on macOS)
- PowerShell 7+ (for the Windows native build script)
  - Windows: installed automatically via [vcpkg](https://github.com/microsoft/vcpkg) manifest mode (`external/GameNetworkingSockets/vcpkg.json`); the build script bootstraps a local vcpkg under `build/vcpkg-local` on first run
  - Linux (Debian/Ubuntu): `sudo apt install build-essential cmake curl perl`
  - macOS (Homebrew): `brew install cmake`

Protobuf and OpenSSL are fetched and built by `build-native-unix.sh`; system
installations of either library are neither used nor required.

Steps:

```bash
# 1. Initialize the GameNetworkingSockets submodule and its dependencies.
pwsh build/bootstrap.ps1

# 2. Build the native shared library for your platform.
pwsh build/build-native-win.ps1              # Windows
bash build/build-native-unix.sh linux-x64    # Linux
bash build/build-native-unix.sh osx-arm64    # macOS (Apple Silicon)

# 3. Build the managed solution.
dotnet build GameNetworkingSockets.Net.sln
```

Output is staged into `artifacts/native/{rid}/` and packed into the NuGet package via the `runtimes/{rid}/native/` convention.

When working only on the managed side (no native binaries available), suppress the pack-time warning with:

```bash
dotnet build GameNetworkingSockets.Net.sln -p:SkipNativeWarning=true
```

## Layout

```
external/GameNetworkingSockets/    # git submodule, pinned to a specific upstream SHA
build/                             # CMakeLists.txt, vcpkg manifest, per-platform build scripts
src/GameNetworkingSockets.Net/     # managed wrapper library (multi-targets net8.0;net10.0)
tests/                             # xunit smoke tests + AOT sample
artifacts/native/                  # staging for native binaries before packing
.github/workflows/                 # ci-pr, build-native, package
```

## License

[MIT](LICENSE) — matches the upstream `ValveSockets-CSharp` wrapper. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the GameNetworkingSockets (BSD 3-Clause), OpenSSL/libsodium, and Protocol Buffers attributions.
