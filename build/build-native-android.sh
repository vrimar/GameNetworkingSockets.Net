#!/usr/bin/env bash
# Cross-compiles libGameNetworkingSockets.so for Android using the NDK + CMake.
#
# Usage: build-native-android.sh <abi>
# Where <abi> is one of: arm64-v8a | x86_64
#
# Requires the Android NDK; resolved from ANDROID_NDK_ROOT or ANDROID_NDK_LATEST_HOME.
# Host tools: cmake, curl, perl, make (and a host C/C++ compiler for protoc).
#
# protoc cannot run cross-compiled, so a host protoc is built once and reused.
# protobuf (target) and OpenSSL are downloaded at pinned versions and built as
# PIC static libraries; libc++ is linked statically, so the output is a single
# self-contained .so with no extra runtime to bundle.
set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <abi>" >&2
    echo "  abi: arm64-v8a | x86_64" >&2
    exit 1
fi

ABI="$1"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
GNS="$REPO/external/GameNetworkingSockets"
ANDROID_API="21"
PROTOBUF_VERSION="21.12"
PROTOBUF_ARCHIVE="protobuf-all-$PROTOBUF_VERSION.tar.gz"
PROTOBUF_URL="https://github.com/protocolbuffers/protobuf/releases/download/v$PROTOBUF_VERSION/$PROTOBUF_ARCHIVE"
PROTOBUF_SHA256="2c6a36c7b5a55accae063667ef3c55f2642e67476d96d355ff0acb13dbb47f09"
OPENSSL_VERSION="3.5.7"
OPENSSL_ARCHIVE="openssl-$OPENSSL_VERSION.tar.gz"
OPENSSL_URL="https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/$OPENSSL_ARCHIVE"
OPENSSL_SHA256="a8c0d28a529ca480f9f36cf5792e2cd21984552a3c8e4aa11a24aa31aeac98e8"
OPENSSL_BUILD_REVISION="2"

case "$ABI" in
    arm64-v8a)
        RID="android-arm64"
        OPENSSL_TARGET="android-arm64"
        ;;
    x86_64)
        RID="android-x64"
        OPENSSL_TARGET="android-x86_64"
        ;;
    *)
        echo "Unsupported ABI: $ABI (expected arm64-v8a | x86_64)" >&2
        exit 1
        ;;
esac

# Resolve the NDK.
NDK="${ANDROID_NDK_ROOT:-${ANDROID_NDK_LATEST_HOME:-}}"
if [ -z "$NDK" ] || [ ! -f "$NDK/build/cmake/android.toolchain.cmake" ]; then
    echo "Android NDK not found. Set ANDROID_NDK_ROOT (or ANDROID_NDK_LATEST_HOME)" >&2
    echo "to an NDK containing build/cmake/android.toolchain.cmake." >&2
    exit 1
fi
TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake"

# Locate the NDK host LLVM toolchain (for OpenSSL's compiler-driver build and
# for the symbol-inspection tools below).
HOST_TAG=""
for tag in linux-x86_64 darwin-x86_64 darwin-arm64; do
    if [ -d "$NDK/toolchains/llvm/prebuilt/$tag" ]; then
        HOST_TAG="$tag"
        break
    fi
done
if [ -z "$HOST_TAG" ]; then
    echo "Could not locate NDK prebuilt LLVM toolchain under $NDK/toolchains/llvm/prebuilt" >&2
    exit 1
fi
LLVM_BIN="$NDK/toolchains/llvm/prebuilt/$HOST_TAG/bin"
NM="$LLVM_BIN/llvm-nm"
READELF="$LLVM_BIN/llvm-readelf"
STRIP="$LLVM_BIN/llvm-strip"
OBJCOPY="$LLVM_BIN/llvm-objcopy"

if [ ! -f "$GNS/include/steam/steamnetworkingsockets.h" ]; then
    echo "GameNetworkingSockets submodule missing at $GNS. Run bootstrap.ps1 first." >&2
    exit 1
fi

NPROC="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"
DEPS_DIR="$REPO/build/deps-$RID"
HOST_DEPS_DIR="$REPO/build/deps-android-host"
DOWNLOAD_DIR="$REPO/build/downloads"
# Host protoc (runs on the build machine to generate .pb.cc).
HOST_PROTOBUF_SOURCE_DIR="$HOST_DEPS_DIR/protobuf-$PROTOBUF_VERSION"
HOST_PROTOBUF_BUILD_DIR="$HOST_DEPS_DIR/protobuf-build"
HOST_PROTOBUF_PREFIX="$HOST_DEPS_DIR/protobuf-prefix"
# Target protobuf (static lib linked into the .so).
PROTOBUF_SOURCE_DIR="$DEPS_DIR/protobuf-$PROTOBUF_VERSION"
PROTOBUF_BUILD_DIR="$DEPS_DIR/protobuf-build"
PROTOBUF_PREFIX="$DEPS_DIR/protobuf-prefix"
OPENSSL_SOURCE_DIR="$DEPS_DIR/openssl-$OPENSSL_VERSION"
OPENSSL_PREFIX="$DEPS_DIR/openssl-prefix"
BUILD_DIR="$REPO/build/build-$RID"
mkdir -p "$DOWNLOAD_DIR"

verify_sha256() {
    local file="$1"
    local expected="$2"
    local actual

    if command -v sha256sum >/dev/null 2>&1; then
        actual="$(sha256sum "$file" | awk '{print $1}')"
    else
        actual="$(shasum -a 256 "$file" | awk '{print $1}')"
    fi

    if [ "$actual" != "$expected" ]; then
        echo "SHA-256 mismatch for $file" >&2
        echo "  expected: $expected" >&2
        echo "  actual:   $actual" >&2
        exit 1
    fi
}

download_protobuf() {
    local archive="$DOWNLOAD_DIR/$PROTOBUF_ARCHIVE"
    if [ ! -f "$archive" ]; then
        echo "[build-native-android] Downloading protobuf $PROTOBUF_VERSION."
        curl --fail --location --retry 3 --output "$archive" "$PROTOBUF_URL"
    fi
    verify_sha256 "$archive" "$PROTOBUF_SHA256"
}

# Host protoc: a native build whose only output we consume is bin/protoc.
build_host_protoc() {
    if [ -x "$HOST_PROTOBUF_PREFIX/bin/protoc" ]; then
        echo "[build-native-android] Reusing cached host protoc $PROTOBUF_VERSION."
        return
    fi

    download_protobuf
    rm -rf "$HOST_PROTOBUF_SOURCE_DIR" "$HOST_PROTOBUF_BUILD_DIR" "$HOST_PROTOBUF_PREFIX"
    mkdir -p "$HOST_DEPS_DIR"
    tar -xzf "$DOWNLOAD_DIR/$PROTOBUF_ARCHIVE" -C "$HOST_DEPS_DIR"

    echo "[build-native-android] Building host protoc $PROTOBUF_VERSION."
    cmake -S "$HOST_PROTOBUF_SOURCE_DIR/cmake" -B "$HOST_PROTOBUF_BUILD_DIR" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CXX_STANDARD=17 \
        -DCMAKE_CXX_STANDARD_REQUIRED=ON \
        -DCMAKE_INSTALL_PREFIX="$HOST_PROTOBUF_PREFIX" \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -Dprotobuf_BUILD_SHARED_LIBS=OFF \
        -Dprotobuf_BUILD_TESTS=OFF \
        -Dprotobuf_BUILD_EXAMPLES=OFF \
        -Dprotobuf_BUILD_PROTOC_BINARIES=ON \
        -Dprotobuf_WITH_ZLIB=OFF
    cmake --build "$HOST_PROTOBUF_BUILD_DIR" --parallel "$NPROC"
    cmake --install "$HOST_PROTOBUF_BUILD_DIR"

    test -x "$HOST_PROTOBUF_PREFIX/bin/protoc"
}

# Target protobuf: static PIC lib cross-compiled for the Android ABI.
build_target_protobuf() {
    if [ -f "$PROTOBUF_PREFIX/lib/libprotobuf.a" ]; then
        echo "[build-native-android] Reusing cached target protobuf $PROTOBUF_VERSION ($ABI)."
        return
    fi

    download_protobuf
    rm -rf "$PROTOBUF_SOURCE_DIR" "$PROTOBUF_BUILD_DIR" "$PROTOBUF_PREFIX"
    mkdir -p "$DEPS_DIR"
    tar -xzf "$DOWNLOAD_DIR/$PROTOBUF_ARCHIVE" -C "$DEPS_DIR"

    echo "[build-native-android] Building target protobuf $PROTOBUF_VERSION as static PIC ($ABI)."
    cmake -S "$PROTOBUF_SOURCE_DIR/cmake" -B "$PROTOBUF_BUILD_DIR" \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN_FILE" \
        -DANDROID_ABI="$ABI" \
        -DANDROID_PLATFORM="android-$ANDROID_API" \
        -DANDROID_STL=c++_static \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CXX_STANDARD=17 \
        -DCMAKE_CXX_STANDARD_REQUIRED=ON \
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
        -DCMAKE_INSTALL_PREFIX="$PROTOBUF_PREFIX" \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -Dprotobuf_BUILD_SHARED_LIBS=OFF \
        -Dprotobuf_BUILD_TESTS=OFF \
        -Dprotobuf_BUILD_EXAMPLES=OFF \
        -Dprotobuf_BUILD_PROTOC_BINARIES=OFF \
        -Dprotobuf_BUILD_LIBPROTOC=OFF \
        -Dprotobuf_WITH_ZLIB=OFF
    cmake --build "$PROTOBUF_BUILD_DIR" --parallel "$NPROC"
    cmake --install "$PROTOBUF_BUILD_DIR"

    test -f "$PROTOBUF_PREFIX/lib/libprotobuf.a"
}

build_target_openssl() {
    local archive="$DOWNLOAD_DIR/$OPENSSL_ARCHIVE"
    local marker="$OPENSSL_PREFIX/.gns-static-build-$OPENSSL_BUILD_REVISION"

    if [ -f "$marker" ] &&
        [ -f "$OPENSSL_PREFIX/lib/libcrypto.a" ] &&
        [ -f "$OPENSSL_PREFIX/lib/libssl.a" ]; then
        echo "[build-native-android] Reusing cached OpenSSL $OPENSSL_VERSION ($ABI)."
        return
    fi

    if [ ! -f "$archive" ]; then
        echo "[build-native-android] Downloading OpenSSL $OPENSSL_VERSION."
        curl --fail --location --retry 3 --output "$archive" "$OPENSSL_URL"
    fi
    verify_sha256 "$archive" "$OPENSSL_SHA256"

    rm -rf "$OPENSSL_SOURCE_DIR" "$OPENSSL_PREFIX"
    tar -xzf "$archive" -C "$DEPS_DIR"

    echo "[build-native-android] Building OpenSSL $OPENSSL_VERSION as static PIC ($ABI)."
    (
        cd "$OPENSSL_SOURCE_DIR"
        # OpenSSL's Android config reads ANDROID_NDK_ROOT and needs the NDK
        # clang wrappers on PATH.
        export ANDROID_NDK_ROOT="$NDK"
        export PATH="$LLVM_BIN:$PATH"
        ./Configure "$OPENSSL_TARGET" \
            "-D__ANDROID_API__=$ANDROID_API" \
            no-shared \
            no-tests \
            no-docs \
            no-apps \
            no-module \
            no-zlib \
            --prefix="$OPENSSL_PREFIX" \
            --libdir=lib \
            -fPIC \
            -fvisibility=hidden
        make -s -j"$NPROC"
        make -s install_sw
    )

    test -f "$OPENSSL_PREFIX/lib/libcrypto.a"
    test -f "$OPENSSL_PREFIX/lib/libssl.a"
    touch "$marker"
}

# The GNS Android portability fixes (CMake OS dispatch, IsLinux() on Android,
# INADDR_BROADCAST cast) live as commits on the pinned submodule fork branch.

build_host_protoc
build_target_protobuf
build_target_openssl

# Use module-mode FindProtobuf, not config mode: cross-compiling needs the host
# protoc (codegen) decoupled from the target static libprotobuf (linked into the
# .so). Config mode couples both to one build, and the target build has no protoc.
# CMAKE_FIND_ROOT_PATH_MODE_PACKAGE=ONLY (below) sandboxes the CONFIG probe to the
# NDK so a host protobuf-config.cmake can't be picked up instead.
PROTOBUF_CMAKE_ARGS=(
    "-DProtobuf_USE_STATIC_LIBS=ON"
    "-DProtobuf_INCLUDE_DIR=$PROTOBUF_PREFIX/include"
    "-DProtobuf_LIBRARY=$PROTOBUF_PREFIX/lib/libprotobuf.a"
    "-DProtobuf_PROTOC_EXECUTABLE=$HOST_PROTOBUF_PREFIX/bin/protoc"
)
OPENSSL_CMAKE_ARGS=(
    "-DCMAKE_DISABLE_FIND_PACKAGE_PkgConfig=TRUE"
    "-DOPENSSL_ROOT_DIR=$OPENSSL_PREFIX"
    "-DOPENSSL_CRYPTO_LIBRARY=$OPENSSL_PREFIX/lib/libcrypto.a"
    "-DOPENSSL_SSL_LIBRARY=$OPENSSL_PREFIX/lib/libssl.a"
    "-DOPENSSL_INCLUDE_DIR=$OPENSSL_PREFIX/include"
)

rm -rf "$BUILD_DIR"

echo "[build-native-android] cmake -S $GNS -B $BUILD_DIR ($ABI, android-$ANDROID_API)"
# Static-link OpenSSL and our pinned PIC protobuf build so the .so is
# self-contained; --exclude-libs,ALL hides their (and libc++'s) symbols.
cmake -S "$GNS" -B "$BUILD_DIR" \
    -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN_FILE" \
    -DANDROID_ABI="$ABI" \
    -DANDROID_PLATFORM="android-$ANDROID_API" \
    -DANDROID_STL=c++_static \
    -DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=ONLY \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_STANDARD=17 \
    -DCMAKE_CXX_STANDARD_REQUIRED=ON \
    "-DCMAKE_SHARED_LINKER_FLAGS=-Wl,--exclude-libs,ALL -llog" \
    -DBUILD_SHARED_LIB=ON \
    -DBUILD_STATIC_LIB=OFF \
    -DBUILD_TESTS=OFF \
    -DBUILD_EXAMPLES=OFF \
    -DBUILD_TOOLS=OFF \
    -DUSE_CRYPTO=OpenSSL \
    -DUSE_CRYPTO25519=OpenSSL \
    -DOPENSSL_USE_STATIC_LIBS=TRUE \
    -DENABLE_ICE=OFF \
    -DCMAKE_CXX_FLAGS="-DSTEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE -DSTEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS" \
    "${PROTOBUF_CMAKE_ARGS[@]}" \
    "${OPENSSL_CMAKE_ARGS[@]}"

echo "[build-native-android] cmake --build $BUILD_DIR --parallel $NPROC"
cmake --build "$BUILD_DIR" --parallel "$NPROC"

LIB="$(find "$BUILD_DIR" -maxdepth 4 -name "libGameNetworkingSockets.so" | head -n 1)"
if [ -z "$LIB" ] || [ ! -f "$LIB" ]; then
    echo "Expected output missing: libGameNetworkingSockets.so under $BUILD_DIR" >&2
    find "$BUILD_DIR" -name '*.so' >&2 || true
    exit 1
fi

NATIVE_OUT="$REPO/artifacts/native/$RID"
mkdir -p "$NATIVE_OUT"
SO="$NATIVE_OUT/libGameNetworkingSockets.so"
cp -f "$LIB" "$SO"

# Strip and produce a separate symbol file.
"$OBJCOPY" --only-keep-debug "$SO" "${SO}.dbg" || true
"$STRIP" --strip-unneeded "$SO" || true
"$OBJCOPY" --add-gnu-debuglink="${SO}.dbg" "$SO" || true

# Symbol-export check. Capture nm output first so `grep -q` can't SIGPIPE nm
# and trip `pipefail`.
EXPORTS="$("$NM" -D --defined-only "$SO")"
if ! grep -qE ' GameNetworkingSockets_Init$' <<<"$EXPORTS"; then
    echo "libGameNetworkingSockets.so does not export GameNetworkingSockets_Init" >&2
    grep -E ' (GameNetworkingSockets_|SteamAPI_)' <<<"$EXPORTS" | head -5 >&2 || true
    exit 1
fi
if grep -qE ' (_ZN6google8protobuf|EVP_|OPENSSL_|SSL_|CRYPTO_|AES_)' <<<"$EXPORTS"; then
    echo "libGameNetworkingSockets.so exports private dependency symbols." >&2
    grep -E ' (_ZN6google8protobuf|EVP_|OPENSSL_|SSL_|CRYPTO_|AES_)' <<<"$EXPORTS" | head -20 >&2
    exit 1
fi

# c++_static + static OpenSSL/protobuf means no dynamic dependency on any of
# them (libc++_shared must not appear either).
NEEDED="$("$READELF" -d "$SO" | grep NEEDED || true)"
if grep -Eqi 'protobuf|libcrypto|libssl|libc\+\+_shared' <<<"$NEEDED"; then
    echo "$SO unexpectedly has a dynamic protobuf/OpenSSL/libc++ dependency:" >&2
    echo "$NEEDED" >&2
    exit 1
fi

echo "[build-native-android] symbol check OK - $RID artifacts staged."
ls -la "$NATIVE_OUT"
