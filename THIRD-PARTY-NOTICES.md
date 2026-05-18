# Third-party notices

GameNetworkingSockets.Net redistributes the following third-party software as
part of its NuGet package or build process.

## GameNetworkingSockets

- Source: https://github.com/ValveSoftware/GameNetworkingSockets
- Author: Valve Corporation
- License: BSD 3-Clause

```
Copyright (c) 2016-present, Valve Corporation

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

## ValveSockets-CSharp (original managed wrapper)

- Source: https://github.com/nxrighthere/ValveSockets-CSharp
- Author: Stanislav Denisov
- License: MIT

The managed binding layer in `src/GameNetworkingSockets.Net/Bindings/` is
derived from this project (and from `FuseCore/Network/ValveSockets.cs`,
which itself derives from it). See the `LICENSE` file for the MIT notice.

## libsodium / OpenSSL (linked at native build time)

GameNetworkingSockets links against either OpenSSL or libsodium to provide
cryptographic primitives. Which library is linked depends on the native
build flags passed via `build/build-native-*.{ps1,sh}`. Refer to the
respective project licenses (OpenSSL: Apache-2.0; libsodium: ISC).

## Protocol Buffers (protobuf)

GameNetworkingSockets uses Google's Protocol Buffers for wire serialization.
Protobuf is licensed under BSD 3-Clause.
