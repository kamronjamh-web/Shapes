using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateGpuCrowdRuntime
{
    private async Task ValidateMovementParity()
    {
        FpsVerticalSlice map = await CreateMap(2000);
        FpsHordeManager gpu = map.HordeManager!;
        gpu.GpuCrowdFullDriveEnabled = false; // Retain the world-only reference path.
        FpsHordeManager cpu = new()
        {
            ProcessMode = ProcessModeEnum.Disabled,
            Settings = (FpsHordeSettings)gpu.Settings.Duplicate(true),
            BodyWorldResolver = gpu.BodyWorldResolver,
            SurfaceHeight = gpu.SurfaceHeight, SurfaceSupportHeight = gpu.SurfaceSupportHeight,
            MaxFootHeight = gpu.MaxFootHeight,
            ConstrainBodyMovement = gpu.ConstrainBodyMovement, ConstrainMovement = gpu.ConstrainMovement,
            ResolveMovementTarget = gpu.ResolveMovementTarget, ResolveObjectiveTarget = gpu.ResolveObjectiveTarget,
            CorePosition = gpu.CorePosition, PlayerPosition = gpu.PlayerPosition,
            IsPlayerAlive = gpu.IsPlayerAlive, CanAttack = gpu.CanAttack,
        };
        cpu.Settings.GpuCrowdContactsEnabled = false;
        AddChild(cpu);
        try
        {
            const int count = 96;
            map.DebugSpawnRole = FpsEnemyRole.Bruiser;
            Check(map.DebugSpawnHorde(count, true) == count, "Movement parity creates an isolated fort crowd");
            StagePressure(map);
            for (int i = 0; i < count; i++)
                cpu.Spawn(map.BruiserArchetypeSettings!, FpsEnemyRole.Bruiser, Vector3.Zero);
            cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
            double maxError = 0;
            for (int step = 0; step < 90; step++)
            {
                if (step == 30)
                {
                    map.DefenseGates[0].DebugInvulnerable = false;
                    map.DefenseGates[0].ApplyDamage(1000000f);
                }
                if (step == 60)
                {
                    map.DebugResetDefenseGates();
                    foreach (FpsDefenseGate gate in map.DefenseGates) gate.DebugInvulnerable = true;
                }
                // GPU state evolves for all 90 steps. Compare each next step to
                // CPU starting from exactly that GPU pre-step state, so accumulated
                // float differences cannot change later contact budgets/steering.
                cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
                Tick(cpu, "CPU");
                Tick(gpu, _headless ? "CPU" : "GPU");
                Check(gpu.LastGpuCrowdMovementResolved == !_headless,
                    "Known fort movement uses compute natively and intentional CPU fallback headlessly");
                var comparison = gpu.CompareGpuProbeState(cpu.SaveGpuProbeState(), .0001f);
                maxError = Math.Max(maxError, comparison.MaxError);
                Check(comparison.Compared == count, "Evolving movement, world contacts and vertical velocity match CPU authority");
            }
            GD.Print($"Movement parity: 90 evolving steps, {count} bodies, breach/reset; max position/pressure/velocity error={maxError:G9}.");

            // A generic wrapper retains CPU movement ordering even when contact
            // geometry itself remains eligible for GPU processing.
            var pureResolver = gpu.BodyWorldResolver!;
            int calls = 0;
            gpu.BodyWorldResolver = (from, desired, radius, height) =>
            {
                calls++;
                return pureResolver(from, desired, radius, height);
            };
            Tick(gpu, _headless ? "CPU" : "GPU");
            Check(!gpu.LastGpuCrowdMovementResolved && calls >= count,
                "Generic movement callbacks are executed on CPU rather than bypassed by compute");
            gpu.BodyWorldResolver = pureResolver;

            if (!_headless)
            {
                // Inject a managed packing failure after eligibility and proposal
                // staging. Fallback must resolve every proposal before CPU contacts.
                gpu.AdvanceGpuProbePreviousPositions();
                cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Check((bool)typeof(FpsHordeManager).GetMethod("PrepareGpuCrowdMovement", flags, null, Type.EmptyTypes, null)!.Invoke(gpu, null)!,
                    "Deferred movement preparation succeeds before the controlled failure");
                var proposals = (Vector3[])typeof(FpsHordeManager).GetField("_gpuMovementProposals", flags)!.GetValue(gpu)!;
                var pending = (bool[])typeof(FpsHordeManager).GetField("_gpuMovementPending", flags)!.GetValue(gpu)!;
                GpuCrowdBody[] packed = new GpuCrowdBody[gpu.Capacity];
                gpu.PackGpuCrowd(packed, Dt, worldResolved: true);
                gpu.CopyAlive(_alive);
                foreach (var body in _alive)
                {
                    int slot = body.Handle.Slot;
                    Vector3 proposed = body.Position + Vector3.Right;
                    proposed.Y = map.SampleHillSurfaceHeight(proposed) - 1f;
                    proposals[slot] = proposed;
                    pending[slot] = true;
                    cpu.TrySetPosition(body.Handle, proposed);
                    GpuCrowdBody input = packed[slot];
                    cpu.SetGpuProbeMotion(body.Handle,
                        new Vector3(input.PreviousHeight.X, input.PreviousHeight.Y, input.PreviousHeight.Z),
                        new Vector3(input.DrivePressure.X, input.DrivePressure.Y, input.DrivePressure.Z), input.DrivePressure.W, 0f);
                }
                cpu.RunCpuProbeContacts(Dt);
                typeof(FpsHordeManager).GetField("_gpuContactBodies", flags)!.SetValue(gpu, Array.Empty<GpuCrowdBody>());
                typeof(FpsHordeManager).GetMethod("SolveCrowdContactsWithBackend", flags)!.Invoke(gpu, new object[] { Dt });
                Check(gpu.LastCrowdContactBackend == "CPU" && !gpu.LastGpuCrowdMovementResolved
                    && gpu.CompareGpuProbeState(cpu.SaveGpuProbeState(), .00001f).Compared == count,
                    "Failure after proposal staging resolves movement and contacts on CPU without losing displacement or floor velocity resets");
                Check(Array.TrueForAll(pending, value => !value), "Fallback clears deferred movement flags for subsequent steps");
            }
        }
        finally
        {
            RemoveChild(cpu);
            cpu.Free();
            map.QueueFree();
            await Frames(2);
        }
    }
}
