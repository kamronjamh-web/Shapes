using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateGpuCrowdRuntime
{
    private async Task ValidateDriveParity()
    {
        FpsVerticalSlice map = await CreateMap(2000);
        FpsHordeManager gpu = map.HordeManager!;
        FpsHordeManager cpu = new()
        {
            ProcessMode = ProcessModeEnum.Disabled, Settings = (FpsHordeSettings)gpu.Settings.Duplicate(true),
            BodyWorldResolver = gpu.BodyWorldResolver, SurfaceHeight = gpu.SurfaceHeight,
            SurfaceSupportHeight = gpu.SurfaceSupportHeight, MaxFootHeight = gpu.MaxFootHeight,
            ResolveMovementTarget = gpu.ResolveMovementTarget, ResolveObjectiveTarget = gpu.ResolveObjectiveTarget,
            CorePosition = gpu.CorePosition,
        };
        cpu.Settings.GpuCrowdContactsEnabled = false;
        AddChild(cpu);
        try
        {
            bool playerAlive = true;
            Vector3 player = Vector3.Zero;
            cpu.PlayerPosition = gpu.PlayerPosition = () => player;
            cpu.IsPlayerAlive = gpu.IsPlayerAlive = () => playerAlive;
            // CPU attacks still run and update their timers, but cannot mutate the
            // shared gate/player state between paired reference and GPU steps.
            cpu.CanAttack = gpu.CanAttack = (_, _) => false;
            map.DebugSpawnRole = FpsEnemyRole.Bruiser;
            Check(map.DebugSpawnHorde(48, true) == 48, "Full drive stages cube bodies");
            map.DebugSpawnRole = FpsEnemyRole.Striker;
            Check(map.DebugSpawnHorde(48, false) == 48, "Full drive stages pyramid bodies");
            StagePressure(map);
            gpu.CopyAlive(_alive);
            foreach (var body in _alive)
                cpu.Spawn(body.Role == FpsEnemyRole.Bruiser ? map.BruiserArchetypeSettings! : map.StrikerArchetypeSettings!, body.Role, Vector3.Zero);
            double maxContact = 0, maxDrive = 0;
            for (int step = 0; step < 120; step++)
            {
                bool rolling = step < 60;
                float hz = step % 3 == 0 ? 5f : step % 3 == 1 ? 30f : 60f;
                gpu.Settings.SimulationHz = cpu.Settings.SimulationHz = hz;
                gpu.Settings.RollingEnabled = cpu.Settings.RollingEnabled = rolling;
                gpu.Settings.DecisionHz = cpu.Settings.DecisionHz = step % 2 == 0 ? 1f : 30f;
                gpu.Settings.CrowdPushStrength = cpu.Settings.CrowdPushStrength = step % 2 == 0 ? .1f : 3f;
                gpu.Settings.Gravity = cpu.Settings.Gravity = step % 2 == 0 ? 1f : 40f;
                gpu.Settings.PlayerAggroRadius = cpu.Settings.PlayerAggroRadius = step % 4 < 2 ? 10000f : 0f;
                playerAlive = step % 8 < 4;
                player = new Vector3(step % 2 == 0 ? 40f : -40f, 10f, step % 3 == 0 ? 40f : -40f);
                if (step == 40)
                {
                    map.DefenseGates[0].DebugInvulnerable = false;
                    map.DefenseGates[0].ApplyDamage(1000000f);
                }
                if (step == 80)
                {
                    map.DebugResetDefenseGates();
                    foreach (var gate in map.DefenseGates) gate.DebugInvulnerable = true;
                }
                cpu.RestoreGpuProbeState(gpu.SaveGpuProbeState());
                float dt = 1f / hz;
                cpu.TickSimulation(dt); gpu.TickSimulation(dt);
                Check(gpu.LastGpuCrowdDriveResolved == !_headless,
                    "Known fort callbacks select full drive natively and CPU fallback headlessly");
                object expected = cpu.SaveGpuProbeState();
                maxContact = Math.Max(maxContact, gpu.CompareGpuProbeState(expected, .0002f).MaxError);
                maxDrive = Math.Max(maxDrive, gpu.CompareGpuProbeDrive(expected, .0002f));
                Check(true, "Same-pre-step drive parity covers mixed roles, rolling, aggro/dead player, dt/settings and live gate changes");
            }
            GD.Print($"Full drive parity: 120 evolving paired steps / mixed roles; contact max={maxContact:G9}, drive max={maxDrive:G9}.");

            var route = gpu.ResolveMovementTarget!;
            gpu.ResolveMovementTarget = (from, target) => route(from, target);
            gpu.TickSimulation(1f / gpu.Settings.SimulationHz);
            Check(!gpu.LastGpuCrowdDriveResolved, "Generic route callbacks retain CPU steering and gait");
            gpu.ResolveMovementTarget = route;
            ValidateDrivePacketRejection(gpu);
            if (!_headless)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object before = gpu.SaveGpuProbeState();
                cpu.RestoreGpuProbeState(before);
                Check((bool)typeof(FpsHordeManager).GetMethod("PrepareGpuCrowdMovement", flags, null, Type.EmptyTypes, null)!.Invoke(gpu, null)!,
                    "Full drive prepares before controlled packet failure");
                Check(gpu.CompareGpuProbeState(before, 0f).MaxError == 0 && gpu.CompareGpuProbeDrive(before, 0f) == 0,
                    "Full drive preparation leaves all authoritative Enemy fields untouched");
                typeof(FpsHordeManager).GetField("_gpuContactBodies", flags)!.SetValue(gpu, Array.Empty<GpuCrowdBody>());
                float dt = 1f / gpu.Settings.SimulationHz;
                typeof(FpsHordeManager).GetMethod("SnapshotCrowdPreviousPositions", flags)!.Invoke(cpu, null);
                typeof(FpsHordeManager).GetMethod("MoveCrowdBodiesCpu", flags)!.Invoke(cpu,
                    new object[] { dt, cpu.CorePosition!(), player, playerAlive, false });
                cpu.RunCpuProbeContacts(dt);
                typeof(FpsHordeManager).GetMethod("UpdateRollingMotion", flags)!.Invoke(cpu, null);
                typeof(FpsHordeManager).GetMethod("SolveCrowdContactsWithBackend", flags)!.Invoke(gpu, new object[] { dt });
                typeof(FpsHordeManager).GetMethod("UpdateRollingMotion", flags)!.Invoke(gpu, null);
                Check(gpu.LastCrowdContactBackend == "CPU" && !gpu.LastGpuCrowdDriveResolved
                    && gpu.CompareGpuProbeState(cpu.SaveGpuProbeState(), .00001f).MaxError <= .00001f
                    && gpu.CompareGpuProbeDrive(cpu.SaveGpuProbeState(), .00001f) <= .00001f,
                    "Full drive failure replays CPU movement/contact/rolling once from unchanged authority");
            }
        }
        finally
        {
            RemoveChild(cpu); cpu.Free();
            map.QueueFree(); await Frames(2);
        }
    }

    private void ValidateDrivePacketRejection(FpsHordeManager crowd)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object before = crowd.SaveGpuProbeState();
        int stacked = crowd.StackedEnemyCount;
        float highest = crowd.HighestPileFeet;
        GpuCrowdBody[] bodies = new GpuCrowdBody[crowd.Capacity];
        GpuCrowdDriveState[] states = new GpuCrowdDriveState[crowd.Capacity];
        crowd.PackGpuCrowd(bodies, Dt, worldResolved: true);
        typeof(FpsHordeManager).GetMethod("PackGpuCrowdDrive", flags)!.Invoke(crowd, new object[] { states, bodies });
        GpuCrowdResult[] contacts = new GpuCrowdResult[crowd.Capacity];
        crowd.CopyAlive(_alive);
        foreach (var body in _alive)
        {
            contacts[body.Handle.Slot].PositionPressure = new Vector4(body.Position.X, body.Position.Y, body.Position.Z, .3f);
            contacts[body.Handle.Slot].VelocityCounts.W = 0f;
        }
        int first = _alive[0].Handle.Slot, last = _alive[^1].Handle.Slot;
        // Earlier entries are valid but visibly different. A bad late entry must
        // reject BOTH packets before previous positions/timers/poses change.
        contacts[first].PositionPressure.X += 10f;
        states[first].TimersYaw.X += 10f;
        Vector4 savedTarget = states[last].TargetPlayer;
        states[last].TargetPlayer.X = float.NaN;
        Reject("Late nonfinite drive state rejects otherwise valid contact output before authority changes");
        states[last].TargetPlayer = savedTarget;
        contacts[last].PositionPressure.X = float.NaN;
        Reject("Late nonfinite contact state rejects otherwise valid drive output before authority changes");

        void Reject(string label)
        {
            bool rejected = false;
            try
            {
                typeof(FpsHordeManager).GetMethod("ApplyGpuCrowdFullResults", flags)!.Invoke(crowd,
                    new object[] { MemoryMarshal.AsBytes(contacts.AsSpan()).ToArray(), MemoryMarshal.AsBytes(states.AsSpan()).ToArray() });
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected && crowd.CompareGpuProbeState(before, 0f).MaxError == 0 && crowd.CompareGpuProbeDrive(before, 0f) == 0
                && crowd.StackedEnemyCount == stacked && crowd.HighestPileFeet == highest, label);
        }
    }
}
