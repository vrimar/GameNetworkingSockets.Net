#!/usr/bin/env bash
# Builds libGameNetworkingSockets.{so,dylib} on Linux or macOS using CMake.
#
# Usage: build-native-unix.sh <rid>
# Where <rid> is one of: linux-x64, osx-x64, osx-arm64
#
# Required dependencies (install before running):
#   Linux (Debian/Ubuntu): sudo apt install libssl-dev libprotobuf-dev protobuf-compiler
#   macOS (Homebrew):      brew install openssl protobuf
set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <rid>" >&2
    echo "  rid: linux-x64 | osx-x64 | osx-arm64" >&2
    exit 1
fi

RID="$1"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
GNS="$REPO/external/GameNetworkingSockets"

case "$RID" in
    linux-x64)
        LIB_EXT="so"
        EXTRA_CMAKE_ARGS=()
        ;;
    osx-x64|osx-arm64)
        LIB_EXT="dylib"
        EXTRA_CMAKE_ARGS=(-DCMAKE_OSX_DEPLOYMENT_TARGET=11.0)
        if [ "$RID" = "osx-x64" ]; then
            EXTRA_CMAKE_ARGS+=(-DCMAKE_OSX_ARCHITECTURES=x86_64)
        else
            EXTRA_CMAKE_ARGS+=(-DCMAKE_OSX_ARCHITECTURES=arm64)
        fi
        # Help CMake locate Homebrew's OpenSSL.
        if command -v brew >/dev/null 2>&1; then
            OPENSSL_PREFIX="$(brew --prefix openssl 2>/dev/null || true)"
            if [ -n "$OPENSSL_PREFIX" ]; then
                EXTRA_CMAKE_ARGS+=("-DOPENSSL_ROOT_DIR=$OPENSSL_PREFIX")
            fi
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

# Upstream bug: when STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE is enabled,
# IThinker declares a class-local operator delete via the
# STEAMNETWORKINGSOCKETS_DECLARE_CLASS_OPERATOR_NEW macro. Several classes
# privately inherit IThinker, which makes the inherited operators
# inaccessible to *further* derived classes — and their virtual destructors
# are then implicitly deleted. Switching the affected inheritance from
# private to public preserves runtime semantics (the operators are public
# in IThinker) and unblocks the build. Idempotent.
echo "[build-native-unix] Applying private-IThinker → public-IThinker workaround for MEM_OVERRIDE."
# perl -i has consistent semantics across BSD (macOS) and GNU (Linux) sed,
# and is byte-safe regardless of LC_ALL — BSD sed throws "illegal byte
# sequence" on GNS sources under macOS's default UTF-8 locale.
find "$GNS/src" -type f \( -name '*.h' -o -name '*.cpp' \) -exec \
    perl -i -pe 's|: private IThinker$|: public IThinker|' {} +

# Bump CMakeLists default CXX_STANDARD from 11 to 17. The set_target_common_gns_properties
# function pins every GNS target to C++11; the main lib then upgrades via
# target_compile_features(cxx_std_17), but the certtool target only gets the
# default. Homebrew's protobuf 34 pulls in abseil headers that require C++17
# ("C++ versions less than C++17 are not supported."), so anything below 17
# fails. Idempotent.
echo "[build-native-unix] Patching GNS CMakeLists to default CXX_STANDARD 17."
perl -i -pe 's|CXX_STANDARD 11\b|CXX_STANDARD 17|' "$GNS/CMakeLists.txt"

NPROC="$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.physicalcpu 2>/dev/null || echo 4)"
BUILD_DIR="$REPO/build/build-$RID"
rm -rf "$BUILD_DIR"

echo "[build-native-unix] cmake -S $GNS -B $BUILD_DIR -DCMAKE_BUILD_TYPE=Release ${EXTRA_CMAKE_ARGS[*]:-}"
# Static-link OpenSSL (libcrypto.a / libssl.a are PIC on Ubuntu's libssl-dev)
# so consumers don't need libcrypto.so.3 at runtime. Protobuf stays dynamic
# because Ubuntu's libprotobuf.a is not compiled with -fPIC; consumers must
# have libprotobuf installed (`apt install libprotobuf23` on jammy).
#
# STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE gates the export of
# SteamNetworkingSockets_SetCustomMemoryAllocator; without it, the symbol
# isn't even in the binary.
cmake -S "$GNS" -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_STANDARD=17 \
    -DCMAKE_CXX_STANDARD_REQUIRED=ON \
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

# Build the certtool in a SECOND cmake configure: it can't link with
# STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE defined globally (the macro
# routes malloc/free/realloc through symbols the certtool target's source
# set doesn't include — those are in steamnetworkingsockets_lowlevel.cpp,
# which only the shared lib pulls in).
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
elif [ "$LIB_EXT" = "dylib" ]; then
    EXPORTS="$(nm -gU "$NATIVE_OUT/libGameNetworkingSockets.dylib")"
    if ! grep -qE ' _GameNetworkingSockets_Init$' <<<"$EXPORTS"; then
        echo "libGameNetworkingSockets.dylib does not export GameNetworkingSockets_Init" >&2
        exit 1
    fi
fi

echo "[build-native-unix] symbol check OK - $RID artifacts staged."
ls -la "$NATIVE_OUT"
