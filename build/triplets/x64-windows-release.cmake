set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)

# The package ships Release binaries only. Avoid compiling duplicate Debug
# variants of OpenSSL, protobuf, and their transitive dependencies in CI.
set(VCPKG_BUILD_TYPE release)
