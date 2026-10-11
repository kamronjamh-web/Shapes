Production CPU corpse worker, Windows x64

This directory packages the optional corpse backend, not the living GPU crowd
simulation. North Gate supports at most 2056 cosmetic cubes/pyramids. Generic
settings keep CorpseWorkerEnabled=false and CorpseCapacity=64; the game resource
configures the requested capacity2056. Godot physics remains the fallback when
the DLL, collision world or worker cannot be used.

Upstream Box3D: https://github.com/erincatto/box3d
Pinned commit: 5d83df83ab47172c2c745b44315e7538ead02397
Full MIT license: win-x64/BOX3D-LICENSE.txt
Toolchain/source notes: win-x64/DEPENDENCY.txt
Packaged DLL SHA256: win-x64/SHA256.txt

Build on the prepared workspace:
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Experiments/Box3dCorpses/BuildNative.ps1 -Package
This builds only inspected upstream core library, local C shim and native smoke.
It tests cube/pyramid settling, stale handles,10000 spawn/remove cycles with fixed
storage, batched reads, CCW mesh terrain and waking sleepers after terrain removal.
Terrain ownership is tested after five2056 mixed-body spawn/clear cycles. Box3D
mesh shapes borrow their mesh data; the bridge retains it until body/world removal.
Successful packaging copies the DLL/license/pin notice and writes its checksum.
Portable tools/dependency checkout are ignored under .tmp/box3d-corpses; no install
or machine PATH changes. The .gdignore excludes native artifacts from Godot import.

SuperGatchaRebuild.csproj copies win-x64/box3d_corpses.dll and the MIT notice into
Windows build/publish output. Preserve both beside the exported runtime. The
native bridge is a Cdecl x64 ABI2 DLL with an84-byte input body definition and
56-byte output state. Managed startup checks ABIversion and both sizes. Bodies
use reusable16-bit slots and15-bit generations; exhaustion retires slots.

The loader runs on the main thread before worker startup. BOX3D_CORPSES_DLL is an
explicit absolute override; otherwise it checks managed assembly/base/executable
paths, the main-thread globalized project native path, then Native/Box3D/win-x64
and the development experiment build path. Godot may load the
managed assembly from bytes (Assembly.Location empty), so validate copied-output
loading explicitly with the environment override. Missing/incompatible binaries
return a reason to the pool instead of breaking game startup. Linux/macOS and
other architectures use Godot fallback; this DLL has no GPU dependency.
The portable build targets baseline x86-64/SSE2, without global AVX2/FMA flags.
Upstream selects its scoped AVX2 kernels only after CPUID and OS XMM/YMM-state
checks; older x64 CPUs use the SSE2 path. No hard AVX2/FMA requirement is imposed.

Each dedicated background thread solely owns its serial native world. The main
thread captures immutable fort static boxes/hulls/CCW meshes and coalesces bounded
slot commands. Gate/geometry changes replace statics and wake sleepers. Completed
pose buffers use epoch/slot/generation checks, including removals/recycling that
arrive while Step executes. No Godot objects or APIs are touched by native worker.
Sleep caches retain the final transition pose and invalidate on spawn/geometry
replacement. Global world create/destroy is serialized by the shim; independent
worlds may step concurrently. Normal frames never wait for native Step. Teardown
joins before releasing the owning world.

Physics is cosmetic/asynchronous: fixed60Hz,four substeps, .25s backlog cap, dropped
time telemetry. Pause is an atomic request; main-thread lifetime/visual hold is
immediate. Cosmetic positions/velocities remain available for Godot fallback.
Rendering uses two MultiMesh batches. Living enemy health/weapons/rewards do not
move onto the worker. This is not jointed ragdoll physics, GPU corpse physics, or
a proven50k/45FPS result on GTX1060/RX580.
