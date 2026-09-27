# GameNetworkingSockets.Net

Cross-platform .NET bindings for Valve's [GameNetworkingSockets](https://github.com/ValveSoftware/GameNetworkingSockets) (the open-source standalone variant of Steam's networking transport), with native binaries bundled for `win-x64`, `linux-x64`, `osx-arm64`, and Android (`android-arm64`, `android-x64`).

The managed surface is derived from Stanislav Denisov's [ValveSockets-CSharp](https://github.com/nxrighthere/ValveSockets-CSharp) wrapper, modernized for [`LibraryImport`](https://learn.microsoft.com/dotnet/standard/native-interop/source-generated-marshalling) (source-generated P/Invoke, AOT-compatible) and updated against the current `steamnetworkingsockets_flat.h` API.

## Packages

| Package | Contents |
|---|---|
| `GameNetworkingSockets.Net` | Managed bindings (`net8.0`/`net10.0`) plus `GameNetworkingSockets.dll` / `libGameNetworkingSockets.so` / `libGameNetworkingSockets.dylib` under `runtimes/{rid}/native/` (desktop + Android). AOT-compatible. |
| `GameNetworkingSockets.Net.Certificates` | Managed minting and reading of certificates: a root authority, the identity-bound certificates it signs, and the public root peers trust (`net10.0`). Needs no native GameNetworkingSockets binary; Ed25519 comes from NSec. |

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

`GameNetworkingSockets.Net` builds GameNetworkingSockets with `STEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS`, so Valve's hardcoded root CA is not installed: outside Steam you bring your own root.

Out of the box the open-source build authenticates nothing. `IPAllowWithoutAuth` and `IPLocalHostAllowWithoutAuth` both default to 2, so a client accepts whatever server answers. To require a server signed by your root:

1. Mint a root authority once, offline, with `GameNetworkingSockets.Net.Certificates`, and issue each server a certificate from it. No native binary is involved.

   ```csharp
   using Valve.Sockets;

   var now = DateTimeOffset.UtcNow;
   using var authority = NetworkingCertificateAuthority.Create(now, TimeSpan.FromDays(3650));
   File.WriteAllBytes("authority.bin", authority.Export()); // secret, keep offline
   string root = authority.RootCertificate; // public, ship it in the client
   byte[] server = authority.Issue("str:game-1", now, TimeSpan.FromDays(365)); // secret, deploy to that server
   ```

   Later, `NetworkingCertificateAuthority.Import(File.ReadAllBytes("authority.bin"))` loads it to issue more. An identity is `str:` and 1 to 31 bytes, or `gen:` and 2 to 64 hex digits.

2. On the server, trust the root and take the certificate before opening listen sockets:

   ```csharp
   sockets.AddTrustedRootCA(root, out var error);
   sockets.SetCertificate(server, out error);
   ```

3. On the client, trust the root and dial with authentication required:

   ```csharp
   sockets.AddTrustedRootCA(root, out var error);
   var connection = sockets.Connect(ref address, [Configuration.Int32(ConfigurationValue.IPAllowWithoutAuth, 0)]);
   ```

What GameNetworkingSockets checks, and what it lets through:

- A root carries no identity. Identity-bound certificates are kept out of the trust store, which is why an authority names none and `Import` refuses a certificate that does.
- A root carries no private key either. `AddTrustedRootCA` accepts the authority itself and ignores its key, so the binding refuses any blob that carries one rather than let the key ship in a client.
- An identity-bound certificate must list the app id its peers run as. The open-source build runs as app 0, the default of `Issue`.
- A process must trust the root of its own certificate, or `SetCertificate` cannot read the certificate and fails.
- `SetCertificate` installs an expired certificate and only peers refuse it, so check `NetworkingCertificate.Read(certificate).Expiry` at startup.
- Expiries end at 2038-01-19 03:14:07 UTC: GameNetworkingSockets on Windows reads a later one as already passed, so the minter refuses it.
- Loopback stays exempt: a peer on the same machine connects unauthenticated unless the client also sets `IPLocalHostAllowWithoutAuth` to 0.
- A process holds one certificate, so every socket in it presents the same identity.

The package also ships Valve's `steamnetworkingsockets_certtool` under `tools/{rid}/`, whose PEM output `NetworkingSockets.SetCertificateAndPrivateKey` takes. It cannot bind a certificate to an identity, so it does not cover server authentication.

## Runtime dependencies

The native libraries inside the NuGet package have these external runtime dependencies:

| RID | Bundled in package | Required on host |
|---|---|---|
| `win-x64` | OpenSSL, protobuf, abseil (DLLs ship next to `GameNetworkingSockets.dll`) | UCRT (universal on Win10+) |
| `linux-x64` | OpenSSL and protobuf (statically linked) | Standard C/C++ runtime libraries |
| `osx-arm64` | OpenSSL and protobuf (statically linked) | macOS system libraries |
| `android-arm64`, `android-x64` | OpenSSL, protobuf, and libc++ (statically linked) | Android system libraries only (`liblog`, `libc`, `libm`, `libdl`); min API 21 |

The Unix and Android builds download protobuf 21.12 and OpenSSL 3.5.7 from their
official release archives, verify their SHA-256 checksums, and build both from
source as position-independent static libraries. This avoids coupling package
consumers to distribution or Homebrew native ABIs. The Android library links
libc++ statically as well, so each `.so` carries no extra runtime dependency.

.NET-for-Android consumers get the per-ABI `.so` placed into the APK
(`lib/<abi>/`) automatically via the bundled `buildTransitive` targets.

## Building from source

Prerequisites:

- .NET SDK 10 (see [`global.json`](global.json))
- CMake 3.15+, curl, and Perl
- A C++ toolchain (MSVC on Windows, gcc/clang on Linux, Apple clang on macOS)
- For Android: the Android NDK (r27 tested), located via `ANDROID_NDK_ROOT` or `ANDROID_NDK_LATEST_HOME`
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
pwsh build/build-native-win.ps1                  # Windows
bash build/build-native-unix.sh linux-x64        # Linux
bash build/build-native-unix.sh osx-arm64        # macOS (Apple Silicon)
bash build/build-native-android.sh arm64-v8a     # Android arm64-v8a (-> android-arm64)
bash build/build-native-android.sh x86_64        # Android x86_64   (-> android-x64)

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
src/GameNetworkingSockets.Net.Certificates/  # managed certificate minting (net10.0)
src/Shared/                        # source compiled into both packages
tests/                             # xunit smoke tests + AOT sample
artifacts/native/                  # staging for native binaries before packing
.github/workflows/                 # ci-pr, build-native, package
```

## License

[MIT](LICENSE) — matches the upstream `ValveSockets-CSharp` wrapper. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the GameNetworkingSockets (BSD 3-Clause), OpenSSL/libsodium, and Protocol Buffers attributions.
