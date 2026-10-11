param([switch]$SmokeOnly, [switch]$Package)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$taskRoot = Join-Path $workspace '.tmp/box3d-corpses'
$toolRoot = Join-Path $taskRoot 'toolchain'
$cmake = Join-Path $toolRoot 'cmake-4.4.4-windows-x86_64/bin/cmake.exe'
$zig = Join-Path $toolRoot 'zig-x86_64-windows-0.15.2/zig.exe'
$ninja = Join-Path $toolRoot 'ninja.exe'
$source = Join-Path $taskRoot 'upstream'
$build = Join-Path $taskRoot 'build'
$expectedCommit = '5d83df83ab47172c2c745b44315e7538ead02397'
if (!(Test-Path $source)) { throw 'Acquire the pinned upstream checkout described in Native/DEPENDENCY.txt first.' }
$actualCommit = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) { throw "Box3D checkout differs from pinned commit: $actualCommit" }
if (!$SmokeOnly) {
    foreach ($tool in @($cmake,$zig,$ninja)) { if (!(Test-Path $tool)) { throw "Missing portable tool: $tool" } }
    New-Item -ItemType Directory -Force $build | Out-Null
    # Both compiler drivers and archive tools come from the same portable release.
    # No SDK installation, machine PATH change, or external dependency fetch.
    $zigCmake = $zig.Replace('\','/')
    $toolchain = Join-Path $build 'zig-toolchain.cmake'
    @"
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR AMD64)
set(CMAKE_C_COMPILER "$zigCmake")
set(CMAKE_C_COMPILER_ARG1 "cc -target x86_64-windows-gnu")
set(CMAKE_CXX_COMPILER "$zigCmake")
set(CMAKE_CXX_COMPILER_ARG1 "c++ -target x86_64-windows-gnu")
set(CMAKE_AR "$zigCmake")
set(CMAKE_C_ARCHIVE_CREATE "<CMAKE_AR> ar qc <TARGET> <LINK_FLAGS> <OBJECTS>")
set(CMAKE_C_ARCHIVE_FINISH "<CMAKE_AR> ranlib <TARGET>")
set(CMAKE_CXX_ARCHIVE_CREATE "<CMAKE_AR> ar qc <TARGET> <LINK_FLAGS> <OBJECTS>")
set(CMAKE_CXX_ARCHIVE_FINISH "<CMAKE_AR> ranlib <TARGET>")
"@ | Set-Content $toolchain
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $taskRoot 'zig-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $taskRoot 'zig-local-cache'
    & $cmake -S (Join-Path $PSScriptRoot 'Native') -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw 'Native CMake configure failed.' }
    & $cmake --build $build --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Native Box3D build failed.' }
}
& (Join-Path $build 'box3d_corpse_smoke.exe')
if ($LASTEXITCODE -ne 0) { throw "Native bridge smoke failed: $LASTEXITCODE" }
if ($Package) {
    $destination=Join-Path $workspace 'Native/Box3D/win-x64'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item (Join-Path $build 'box3d_corpses.dll') (Join-Path $destination 'box3d_corpses.dll') -Force
    Copy-Item (Join-Path $PSScriptRoot 'Native/BOX3D-LICENSE.txt') (Join-Path $destination 'BOX3D-LICENSE.txt') -Force
    Copy-Item (Join-Path $PSScriptRoot 'Native/DEPENDENCY.txt') (Join-Path $destination 'DEPENDENCY.txt') -Force
    $dllHash=(Get-FileHash (Join-Path $destination 'box3d_corpses.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    "box3d_corpses.dll SHA256 $dllHash" | Set-Content (Join-Path $destination 'SHA256.txt')
}
