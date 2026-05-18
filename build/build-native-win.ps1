#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds GameNetworkingSockets.dll for win-x64 (MSVC).

.DESCRIPTION
    Invokes CMake on external/GameNetworkingSockets/CMakeLists.txt, building
    the shared library with OpenSSL + Protocol Buffers provided via vcpkg
    (manifest mode — see build/vcpkg.json). Stages the produced
    GameNetworkingSockets.dll into artifacts/native/win-x64/.

.PARAMETER Configuration
    Release (default) or Debug. The package ships Release only.

.PARAMETER VcpkgRoot
    Optional explicit path to a vcpkg checkout. If omitted, the script looks
    at $env:VCPKG_ROOT and then falls back to bootstrapping a local copy
    under build/vcpkg-local.
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Generator = '',

    [string]$VcpkgRoot = ''
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot/.."
$gns = Join-Path $repo 'external/GameNetworkingSockets'

if (!(Test-Path (Join-Path $gns 'include/steam/steamnetworkingsockets.h'))) {
    Write-Error "GameNetworkingSockets submodule missing at $gns. Run bootstrap.ps1 first."
}

# Upstream bug: when STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE is enabled,
# IThinker declares a class-local operator delete via the
# STEAMNETWORKINGSOCKETS_DECLARE_CLASS_OPERATOR_NEW macro. Several classes
# privately inherit IThinker, which makes the inherited operators
# inaccessible to *further* derived classes — and their virtual destructors
# are then implicitly deleted. Switching the affected inheritance from
# private to public preserves runtime semantics (the operators are public
# in IThinker) and unblocks the build. Idempotent.
Write-Host "[build-native-win] Applying private-IThinker -> public-IThinker workaround for MEM_OVERRIDE."
Get-ChildItem -Path (Join-Path $gns 'src') -Recurse -Include '*.h', '*.cpp' | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName)
    $updated = $content -replace '(?m): private IThinker$', ': public IThinker'
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($_.FullName, $updated)
    }
}

# Resolve vcpkg.
if (-not $VcpkgRoot) {
    if ($env:VCPKG_ROOT) {
        $VcpkgRoot = $env:VCPKG_ROOT
    }
    else {
        $VcpkgRoot = Join-Path $repo 'build/vcpkg-local'
        if (!(Test-Path (Join-Path $VcpkgRoot 'vcpkg.exe'))) {
            Write-Host "[build-native-win] Bootstrapping local vcpkg at $VcpkgRoot"
            if (!(Test-Path $VcpkgRoot)) {
                git clone https://github.com/microsoft/vcpkg.git $VcpkgRoot
            }
            & (Join-Path $VcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics
            if ($LASTEXITCODE -ne 0) { Write-Error "vcpkg bootstrap failed." }
        }
    }
}
$toolchain = Join-Path $VcpkgRoot 'scripts/buildsystems/vcpkg.cmake'
if (!(Test-Path $toolchain)) {
    Write-Error "vcpkg toolchain not found at $toolchain. Set -VcpkgRoot or `$env:VCPKG_ROOT to your vcpkg checkout."
}

# Pick a VS generator (newest installed, unless overridden).
if (-not $Generator) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installVer = (& $vswhere -latest -prerelease -property installationVersion 2>$null)
        $major = if ($installVer) { ($installVer -split '\.')[0] } else { '' }
        switch ($major) {
            '18' { $Generator = 'Visual Studio 18 2026' }
            '17' { $Generator = 'Visual Studio 17 2022' }
            '16' { $Generator = 'Visual Studio 16 2019' }
            default { $Generator = 'Visual Studio 17 2022' }
        }
    }
    else {
        $Generator = 'Visual Studio 17 2022'
    }
}

$buildDir = Join-Path $repo 'build/build-win-x64'
if (Test-Path $buildDir) {
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# vcpkg manifest mode reads vcpkg.json from CMAKE_CURRENT_SOURCE_DIR
# (GNS root); the submodule already ships one declaring openssl + protobuf.

Write-Host "[build-native-win] cmake configure (Generator='$Generator', vcpkg='$VcpkgRoot')"
# STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE gates the export of
# SteamNetworkingSockets_SetCustomMemoryAllocator. Pass via CMAKE_CXX_FLAGS so
# all targets in the build see it.
& cmake -S $gns -B $buildDir -G $Generator -A x64 `
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
    "-DVCPKG_TARGET_TRIPLET=x64-windows" `
    -DBUILD_SHARED_LIB=ON `
    -DBUILD_STATIC_LIB=OFF `
    -DBUILD_TESTS=OFF `
    -DBUILD_EXAMPLES=OFF `
    -DBUILD_TOOLS=OFF `
    -DUSE_CRYPTO=OpenSSL `
    -DUSE_CRYPTO25519=OpenSSL `
    -DENABLE_ICE=OFF `
    -DProtobuf_USE_STATIC_LIBS=ON `
    "-DCMAKE_CXX_FLAGS=/DSTEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE /DSTEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS"
if ($LASTEXITCODE -ne 0) { Write-Error "cmake configure failed: $LASTEXITCODE"; exit 1 }

Write-Host "[build-native-win] cmake --build $buildDir --config $Configuration --parallel"
& cmake --build $buildDir --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { Write-Error "cmake --build failed: $LASTEXITCODE"; exit 1 }

$dll = Get-ChildItem -Path $buildDir -Filter 'GameNetworkingSockets.dll' -Recurse | Select-Object -First 1
if (-not $dll) { Write-Error "GameNetworkingSockets.dll not produced; inspect $buildDir." }

$nativeOut = Join-Path $repo 'artifacts/native/win-x64'
New-Item -ItemType Directory -Path $nativeOut -Force | Out-Null
Copy-Item -Path $dll.FullName -Destination (Join-Path $nativeOut 'GameNetworkingSockets.dll') -Force

$pdb = Get-ChildItem -Path $buildDir -Filter 'GameNetworkingSockets.pdb' -Recurse | Select-Object -First 1
if ($pdb) {
    Copy-Item -Path $pdb.FullName -Destination (Join-Path $nativeOut 'GameNetworkingSockets.pdb') -Force
}

# Stage runtime dependencies that vcpkg produced alongside the DLL.
Get-ChildItem -Path $dll.Directory -Filter '*.dll' |
    Where-Object { $_.Name -ne 'GameNetworkingSockets.dll' } |
    ForEach-Object { Copy-Item -Path $_.FullName -Destination $nativeOut -Force }

# Build the certtool in a SECOND cmake configure: it can't link with
# STEAMNETWORKINGSOCKETS_ENABLE_MEM_OVERRIDE defined globally (the macro
# routes malloc/free/realloc through symbols the certtool target's source
# set doesn't include — those are in steamnetworkingsockets_lowlevel.cpp,
# which only the shared lib pulls in).
$toolsBuildDir = Join-Path $repo 'build/build-win-x64-tools'
if (Test-Path $toolsBuildDir) { Remove-Item -Recurse -Force $toolsBuildDir }
New-Item -ItemType Directory -Path $toolsBuildDir -Force | Out-Null

Write-Host "[build-native-win] cmake configure (certtool) in $toolsBuildDir"
& cmake -S $gns -B $toolsBuildDir -G $Generator -A x64 `
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
    "-DVCPKG_TARGET_TRIPLET=x64-windows" `
    -DBUILD_SHARED_LIB=OFF `
    -DBUILD_STATIC_LIB=ON `
    -DBUILD_TESTS=OFF `
    -DBUILD_EXAMPLES=OFF `
    -DBUILD_TOOLS=ON `
    -DUSE_CRYPTO=OpenSSL `
    -DUSE_CRYPTO25519=OpenSSL `
    -DENABLE_ICE=OFF `
    -DProtobuf_USE_STATIC_LIBS=ON `
    "-DCMAKE_CXX_FLAGS=/DSTEAMNETWORKINGSOCKETS_ALLOW_DYNAMIC_SELFSIGNED_CERTS"
if ($LASTEXITCODE -ne 0) { Write-Error "cmake configure (certtool) failed: $LASTEXITCODE"; exit 1 }

Write-Host "[build-native-win] cmake --build $toolsBuildDir --target steamnetworkingsockets_certtool --config $Configuration"
& cmake --build $toolsBuildDir --target steamnetworkingsockets_certtool --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { Write-Error "cmake --build (certtool) failed: $LASTEXITCODE"; exit 1 }

$certtool = Get-ChildItem -Path $toolsBuildDir -Filter 'steamnetworkingsockets_certtool.exe' -Recurse | Select-Object -First 1
if ($certtool) {
    Copy-Item -Path $certtool.FullName -Destination (Join-Path $nativeOut 'steamnetworkingsockets_certtool.exe') -Force
} else {
    Write-Warning "steamnetworkingsockets_certtool.exe not produced; tools/ packaging will be incomplete."
}

Write-Host "[build-native-win] Staged win-x64 artifacts:"
Get-ChildItem $nativeOut | Format-Table Name, Length

$dumpbin = (Get-Command dumpbin.exe -ErrorAction SilentlyContinue)?.Path
if ($dumpbin) {
    $exports = & $dumpbin /exports (Join-Path $nativeOut 'GameNetworkingSockets.dll')
    if (-not ($exports -match '\bGameNetworkingSockets_Init\b')) {
        Write-Error "GameNetworkingSockets.dll does not export GameNetworkingSockets_Init - build is broken."
    }
    Write-Host "[build-native-win] symbol check OK: GameNetworkingSockets_Init exported."
}
else {
    Write-Warning "dumpbin.exe not in PATH; skipping symbol-export check."
}

$global:LASTEXITCODE = 0
exit 0
