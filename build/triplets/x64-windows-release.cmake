set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)

# The package ships Release binaries only. Avoid compiling duplicate Debug
# variants of OpenSSL, protobuf, and their transitive dependencies in CI.
set(VCPKG_BUILD_TYPE release)

# __FILE__ names sources relative to the repo and vcpkg, and each DLL names its PDB by file name only.
get_filename_component(_gns_repo "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
set(_gns_trim_roots "${_gns_repo}")
if(DEFINED VCPKG_ROOT_DIR)
    list(APPEND _gns_trim_roots "${VCPKG_ROOT_DIR}")
endif()
set(_gns_trim_flags "")
foreach(_root IN LISTS _gns_trim_roots)
    file(TO_NATIVE_PATH "${_root}" _native)
    string(APPEND _gns_trim_flags " /d1trimfile:${_root} /d1trimfile:${_native}")
endforeach()
# Through CL/LINK, not VCPKG_*_FLAGS: OpenSSL bakes its compiler flags, path included, into libcrypto.
set(ENV{CL} "$ENV{CL}${_gns_trim_flags}")
set(ENV{LINK} "$ENV{LINK} /PDBALTPATH:%_PDB%")
