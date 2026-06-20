#!/usr/bin/env bash
# Builds libGameNetworkingSockets.{so,dylib} on Linux or macOS using CMake.
#
# Usage: build-native-unix.sh <rid>
# Where <rid> is one of: linux-x64, osx-x64, osx-arm64
#
# Required dependencies (install before running):
#   Linux (Debian/Ubuntu): sudo apt install build-essential cmake curl perl
#   macOS (Homebrew):      brew install cmake
#
# Protobuf and OpenSSL are downloaded at pinned versions and built as PIC
# static libraries. This keeps the packaged GameNetworkingSockets library
# independent of host package-manager ABIs.
set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <rid>" >&2
    echo "  rid: linux-x64 | osx-x64 | osx-arm64" >&2
    exit 1
fi

RID="$1"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
GNS="$REPO/external/GameNetworkingSockets"
PROTOBUF_VERSION="21.12"
PROTOBUF_ARCHIVE="protobuf-all-$PROTOBUF_VERSION.tar.gz"
PROTOBUF_URL="https://github.com/protocolbuffers/protobuf/releases/download/v$PROTOBUF_VERSION/$PROTOBUF_ARCHIVE"
PROTOBUF_SHA256="2c6a36c7b5a55accae063667ef3c55f2642e67476d96d355ff0acb13dbb47f09"
OPENSSL_VERSION="3.5.7"
OPENSSL_ARCHIVE="openssl-$OPENSSL_VERSION.tar.gz"
OPENSSL_URL="https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/$OPENSSL_ARCHIVE"
OPENSSL_SHA256="a8c0d28a529ca480f9f36cf5792e2cd21984552a3c8e4aa11a24aa31aeac98e8"
OPENSSL_BUILD_REVISION="2"

case "$RID" in
    linux-x64)
        LIB_EXT="so"
        OPENSSL_TARGET="linux-x86_64"
        PLATFORM_SHARED_LINKER_FLAGS="-Wl,--exclude-libs,ALL"
        EXTRA_CMAKE_ARGS=()
        ;;
    osx-x64|osx-arm64)
        LIB_EXT="dylib"
        export MACOSX_DEPLOYMENT_TARGET=11.0
        PLATFORM_SHARED_LINKER_FLAGS="-Wl,-exported_symbols_list,$REPO/build/exports-macos.txt"
        EXTRA_CMAKE_ARGS=(-DCMAKE_OSX_DEPLOYMENT_TARGET=11.0)
        if [ "$RID" = "osx-x64" ]; then
            OPENSSL_TARGET="darwin64-x86_64-cc"
            EXTRA_CMAKE_ARGS+=(-DCMAKE_OSX_ARCHITECTURES=x86_64)
        else
            OPENSSL_TARGET="darwin64-arm64-cc"
            EXTRA_CMAKE_ARGS+=(-DCMAKE_OSX_ARCHITECTURES=arm64)
        fi
        ;;
    *)
        echo "Unsupported RID: $RID" >&2
        exit 1
        ;;
esac

if [ ! -f "$GNS/include/steam/steamnetworkingsockets.h" ]; then
    echo "GameNetworkingSockets submodule missing at $GNS. Run bootstrap.ps1 first." >&2
    exit 1
fi

NPROC="$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.physicalcpu 2>/dev/null || echo 4)"
DEPS_DIR="$REPO/build/deps-$RID"
DOWNLOAD_DIR="$REPO/build/downloads"
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

build_static_protobuf() {
    local archive="$DOWNLOAD_DIR/$PROTOBUF_ARCHIVE"

    if [ -f "$PROTOBUF_PREFIX/lib/libprotobuf.a" ] && [ -x "$PROTOBUF_PREFIX/bin/protoc" ]; then
        echo "[build-native-unix] Reusing cached protobuf $PROTOBUF_VERSION."
        return
    fi

    if [ ! -f "$archive" ]; then
        echo "[build-native-unix] Downloading protobuf $PROTOBUF_VERSION."
        curl --fail --location --retry 3 --output "$archive" "$PROTOBUF_URL"
    fi
    verify_sha256 "$archive" "$PROTOBUF_SHA256"

    rm -rf "$PROTOBUF_SOURCE_DIR" "$PROTOBUF_BUILD_DIR" "$PROTOBUF_PREFIX"
    mkdir -p "$DEPS_DIR"
    tar -xzf "$archive" -C "$DEPS_DIR"

    echo "[build-native-unix] Building protobuf $PROTOBUF_VERSION as static PIC."
    cmake -S "$PROTOBUF_SOURCE_DIR/cmake" -B "$PROTOBUF_BUILD_DIR" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_CXX_STANDARD=17 \
        -DCMAKE_CXX_STANDARD_REQUIRED=ON \
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
        -DCMAKE_INSTALL_PREFIX="$PROTOBUF_PREFIX" \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -Dprotobuf_BUILD_SHARED_LIBS=OFF \
        -Dprotobuf_BUILD_TESTS=OFF \
        -Dprotobuf_BUILD_EXAMPLES=OFF \
        -Dprotobuf_BUILD_PROTOC_BINARIES=ON \
        -Dprotobuf_BUILD_LIBPROTOC=OFF \
        -Dprotobuf_WITH_ZLIB=OFF \
        "${EXTRA_CMAKE_ARGS[@]}"
    cmake --build "$PROTOBUF_BUILD_DIR" --parallel "$NPROC"
    cmake --install "$PROTOBUF_BUILD_DIR"

    test -f "$PROTOBUF_PREFIX/lib/libprotobuf.a"
    test -x "$PROTOBUF_PREFIX/bin/protoc"
}

build_static_openssl() {
    local archive="$DOWNLOAD_DIR/$OPENSSL_ARCHIVE"
    local marker="$OPENSSL_PREFIX/.gns-static-build-$OPENSSL_BUILD_REVISION"

    if [ -f "$marker" ] &&
        [ -f "$OPENSSL_PREFIX/lib/libcrypto.a" ] &&
        [ -f "$OPENSSL_PREFIX/lib/libssl.a" ]; then
        echo "[build-native-unix] Reusing cached OpenSSL $OPENSSL_VERSION."
        return
    fi

    if [ ! -f "$archive" ]; then
        echo "[build-native-unix] Downloading OpenSSL $OPENSSL_VERSION."
        curl --fail --location --retry 3 --output "$archive" "$OPENSSL_URL"
    fi
    verify_sha256 "$archive" "$OPENSSL_SHA256"

    rm -rf "$OPENSSL_SOURCE_DIR" "$OPENSSL_PREFIX"
    tar -xzf "$archive" -C "$DEPS_DIR"

    echo "[build-native-unix] Building OpenSSL $OPENSSL_VERSION as static PIC."
    (
        cd "$OPENSSL_SOURCE_DIR"
        ./Configure "$OPENSSL_TARGET" \
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

build_static_protobuf
build_static_openssl

PROTOBUF_CMAKE_ARGS=(
    "-DProtobuf_USE_STATIC_LIBS=ON"
    "-DProtobuf_DIR=$PROTOBUF_PREFIX/lib/cmake/protobuf"
    "-DProtobuf_INCLUDE_DIR=$PROTOBUF_PREFIX/include"
    "-DProtobuf_LIBRARY=$PROTOBUF_PREFIX/lib/libprotobuf.a"
    "-DProtobuf_PROTOC_EXECUTABLE=$PROTOBUF_PREFIX/bin/protoc"
)
OPENSSL_CMAKE_ARGS=(
    "-DCMAKE_DISABLE_FIND_PACKAGE_PkgConfig=TRUE"
    "-DOPENSSL_ROOT_DIR=$OPENSSL_PREFIX"
    "-DOPENSSL_CRYPTO_LIBRARY=$OPENSSL_PREFIX/lib/libcrypto.a"
    "-DOPENSSL_SSL_LIBRARY=$OPENSSL_PREFIX/lib/libssl.a"
    "-DOPENSSL_INCLUDE_DIR=$OPENSSL_PREFIX/include"
)

rm -rf "$BUILD_DIR"

echo "[build-native-unix] cmake -S $GNS -B $BUILD_DIR -DCMAKE_BUILD_TYPE=Release ${EXTRA_CMAKE_ARGS[*]:-}"
# Static-link OpenSSL and our pinned PIC protobuf build so consumers do not
# need either library installed at runtime.
#
# STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE gates the export of
# SteamNetworkingSockets_SetCustomMemoryAllocator; without it, the symbol
# isn't even in the binary.
cmake -S "$GNS" -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_STANDARD=17 \
    -DCMAKE_CXX_STANDARD_REQUIRED=ON \
    "-DCMAKE_SHARED_LINKER_FLAGS=$PLATFORM_SHARED_LINKER_FLAGS" \
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
    "${OPENSSL_CMAKE_ARGS[@]}" \
    "${EXTRA_CMAKE_ARGS[@]}"

echo "[build-native-unix] cmake --build $BUILD_DIR --parallel $NPROC"
cmake --build "$BUILD_DIR" --parallel "$NPROC"

LIB="$(find "$BUILD_DIR" -maxdepth 4 -name "libGameNetworkingSockets.$LIB_EXT" | head -n 1)"
if [ -z "$LIB" ] || [ ! -f "$LIB" ]; then
    echo "Expected output missing: libGameNetworkingSockets.$LIB_EXT under $BUILD_DIR" >&2
    find "$BUILD_DIR" -name '*.so' -o -name '*.dylib' >&2 || true
    exit 1
fi

NATIVE_OUT="$REPO/artifacts/native/$RID"
mkdir -p "$NATIVE_OUT"
cp -f "$LIB" "$NATIVE_OUT/libGameNetworkingSockets.$LIB_EXT"

# Build the certtool in a second configure without the library-only memory
# allocator override.
TOOLS_BUILD_DIR="$REPO/build/build-$RID-tools"
rm -rf "$TOOLS_BUILD_DIR"
echo "[build-native-unix] cmake configure (certtool) in $TOOLS_BUILD_DIR"
cmake -S "$GNS" -B "$TOOLS_BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_STANDARD=17 \
    -DCMAKE_CXX_STANDARD_REQUIRED=ON \
    -DBUILD_SHARED_LIB=OFF \
    -DBUILD_STATIC_LIB=ON \
    -DBUILD_TESTS=OFF \
    -DBUILD_EXAMPLES=OFF \
    -DBUILD_TOOLS=ON \
    -DUSE_CRYPTO=OpenSSL \
    -DUSE_CRYPTO25519=OpenSSL \
    -DOPENSSL_USE_STATIC_LIBS=TRUE \
    -DENABLE_ICE=OFF \
    -DCMAKE_CXX_FLAGS="-DSTEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS" \
    "${PROTOBUF_CMAKE_ARGS[@]}" \
    "${OPENSSL_CMAKE_ARGS[@]}" \
    "${EXTRA_CMAKE_ARGS[@]}"

echo "[build-native-unix] cmake --build $TOOLS_BUILD_DIR --target steamnetworkingsockets_certtool --parallel $NPROC"
cmake --build "$TOOLS_BUILD_DIR" --target steamnetworkingsockets_certtool --parallel "$NPROC"

CERTTOOL="$(find "$TOOLS_BUILD_DIR" -maxdepth 4 -type f -name 'steamnetworkingsockets_certtool' | head -n 1)"
if [ -n "$CERTTOOL" ] && [ -f "$CERTTOOL" ]; then
    cp -f "$CERTTOOL" "$NATIVE_OUT/steamnetworkingsockets_certtool"
    chmod +x "$NATIVE_OUT/steamnetworkingsockets_certtool"
else
    echo "[build-native-unix] WARN: steamnetworkingsockets_certtool not produced; tools/ packaging will be incomplete." >&2
fi

# Strip and produce separate symbol files.
if [ "$LIB_EXT" = "so" ]; then
    SO="$NATIVE_OUT/libGameNetworkingSockets.so"
    objcopy --only-keep-debug "$SO" "${SO}.dbg" || true
    strip --strip-unneeded "$SO" || true
    objcopy --add-gnu-debuglink="${SO}.dbg" "$SO" || true

    if command -v patchelf >/dev/null 2>&1; then
        patchelf --set-soname libGameNetworkingSockets.so "$SO" || true
    fi
elif [ "$LIB_EXT" = "dylib" ]; then
    DY="$NATIVE_OUT/libGameNetworkingSockets.dylib"
    if command -v dsymutil >/dev/null 2>&1; then
        dsymutil "$DY" -o "${DY}.dSYM" || true
    fi
    strip -S "$DY" || true
fi

# Symbol-export check. Capture nm output to a variable first so `grep -q`
# can't SIGPIPE nm and trip `pipefail`. \b in grep -E is glibc-version
# dependent, so anchor on whitespace explicitly.
if [ "$LIB_EXT" = "so" ]; then
    EXPORTS="$(nm -D --defined-only "$NATIVE_OUT/libGameNetworkingSockets.so")"
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

    for binary in "$NATIVE_OUT/libGameNetworkingSockets.so" "$NATIVE_OUT/steamnetworkingsockets_certtool"; do
        NEEDED="$(readelf -d "$binary" | grep NEEDED || true)"
        if grep -Eqi 'protobuf|libcrypto|libssl' <<<"$NEEDED"; then
            echo "$binary unexpectedly has a dynamic protobuf/OpenSSL dependency:" >&2
            echo "$NEEDED" >&2
            exit 1
        fi
    done
elif [ "$LIB_EXT" = "dylib" ]; then
    EXPORTS="$(nm -gU "$NATIVE_OUT/libGameNetworkingSockets.dylib")"
    if ! grep -qE ' _GameNetworkingSockets_Init$' <<<"$EXPORTS"; then
        echo "libGameNetworkingSockets.dylib does not export GameNetworkingSockets_Init" >&2
        exit 1
    fi
    if grep -qE ' __ZN6google8protobuf| _(EVP_|OPENSSL_|SSL_|CRYPTO_|AES_)' <<<"$EXPORTS"; then
        echo "libGameNetworkingSockets.dylib exports private dependency symbols." >&2
        grep -E ' __ZN6google8protobuf| _(EVP_|OPENSSL_|SSL_|CRYPTO_|AES_)' <<<"$EXPORTS" | head -20 >&2
        exit 1
    fi

    for binary in "$NATIVE_OUT/libGameNetworkingSockets.dylib" "$NATIVE_OUT/steamnetworkingsockets_certtool"; do
        DYLIBS="$(otool -L "$binary")"
        if grep -Eqi 'protobuf|libcrypto|libssl|/opt/homebrew|/usr/local' <<<"$DYLIBS"; then
            echo "$binary unexpectedly has a package-manager protobuf/OpenSSL dependency:" >&2
            echo "$DYLIBS" >&2
            exit 1
        fi
    done
fi

echo "[build-native-unix] symbol check OK - $RID artifacts staged."
ls -la "$NATIVE_OUT"
