using System;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateGpuCrowdRuntime
{
    private async Task ValidateRollingPrecisionParity()
    {
        FpsVerticalSlice map = await CreateMap(100);
        map.DebugClearHorde();
        FpsHordeManager gpu = map.HordeManager!;
        FpsHordeManager cpu = new()
        {
            ProcessMode = ProcessModeEnum.Disabled, Settings = (FpsHordeSettings)gpu.Settings.Duplicate(true),
            BodyWorldResolver = gpu.BodyWorldResolver, SurfaceHeight = gpu.SurfaceHeight,
            SurfaceSupportHeight = gpu.SurfaceSupportHeight, MaxFootHeight = gpu.MaxFootHeight,
            ResolveMovementTarget = gpu.ResolveMovementTarget, ResolveObjectiveTarget = gpu.ResolveObjectiveTarget,
            CorePosition = gpu.CorePosition, IsPlayerAlive = () => false, CanAttack = (_, _) => false,
        };
        cpu.Settings.GpuCrowdContactsEnabled = false;
        gpu.IsPlayerAlive = () => false; gpu.CanAttack = (_, _) => false;
        gpu.Settings.RollingPauseSeconds = cpu.Settings.RollingPauseSeconds = 0;
        gpu.Settings.RollingDurationSeconds = cpu.Settings.RollingDurationSeconds = 0;
        AddChild(cpu);
        try
        {
            Vector3[] origins = { new(600, 0, 0), new(800, 0, 0), new(600, 0, 600), new(800, 0, -600) };
            FpsHordeHandle[] handles = new FpsHordeHandle[origins.Length];
            for (int i = 0; i < origins.Length; i++)
            {
                handles[i] = gpu.Spawn(map.BruiserArchetypeSettings!, FpsEnemyRole.Bruiser, origins[i]);
                cpu.Spawn(map.BruiserArchetypeSettings!, FpsEnemyRole.Bruiser, origins[i]);
                RollingPrecisionFixture.Stage(gpu, handles[i], i == 3 ? .55f : 1.1f - .0000244379f, speed: 0);
            }
            cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
            cpu.TickSimulation(Dt); gpu.TickSimulation(Dt);
            Check(gpu.LastGpuCrowdDriveResolved == !_headless, "Precision residual fixture uses full GPU drive natively");
            gpu.CompareGpuProbeState(cpu.SaveGpuProbeState(), .0002f);
            gpu.CompareGpuProbeDrive(cpu.SaveGpuProbeState(), .0002f);
            for (int i = 0; i < handles.Length; i++)
            {
                gpu.TryGetRollingState(handles[i], out float distance, out _, out _);
                Check(i == 3 ? distance == .55f : distance == 0,
                    i == 3 ? "GPU stationary mid-roll preserves the unfinished gait" : "GPU zero-travel terminal residual finishes at large coordinates");
                RollingPrecisionFixture.Stage(gpu, handles[i], 0, speed: 3.5f);
            }
            int[] cycles = new int[handles.Length]; float[] previous = new float[handles.Length];
            double maximumError = 0;
            for (int step = 0; step < 1600; step++)
            {
                cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
                cpu.TickSimulation(Dt); gpu.TickSimulation(Dt);
                if (step == 0) Check(gpu.LastGpuCrowdDriveResolved == !_headless, "Natural precision progression retains the intended full-drive backend");
                maximumError = Math.Max(maximumError, gpu.CompareGpuProbeState(cpu.SaveGpuProbeState(), .0003f).MaxError);
                gpu.CompareGpuProbeDrive(cpu.SaveGpuProbeState(), .0003f);
                bool complete = true;
                for (int i = 0; i < handles.Length; i++)
                {
                    gpu.TryGetRollingState(handles[i], out float distance, out _, out _);
                    if (distance < previous[i]) cycles[i]++;
                    previous[i] = distance;
                    complete &= cycles[i] >= 100;
                }
                if (complete) break;
                if (step % 100 == 0) await Frames(1);
            }
            for (int i = 0; i < handles.Length; i++)
            {
                gpu.TryGet(handles[i], out var body);
                Check(cycles[i] >= 100 && body.Position.DistanceTo(origins[i]) > 100,
                    "GPU/CPU large-coordinate cardinal/diagonal progression completes 100 cycles " + origins[i]);
            }
            GD.Print($"Rolling precision parity: four bodies, 100 cycles each; maximum paired contact error={maximumError:G9}.");
        }
        finally { RemoveChild(cpu); cpu.Free(); map.QueueFree(); await Frames(2); }
    }
}
