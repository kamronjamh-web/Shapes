using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

internal static class ValidateStrongholdRouting
{
    internal static int Run(FpsVerticalSlice map)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        float ground = map.HillMapSettings!.SummitHeight;
        var lanes = new HashSet<int>();
        for (int slot = 0; slot < 1000; slot++)
        {
            float lane = FpsVerticalSlice.StrongholdLane(slot);
            Check(Mathf.Abs(lane) <= 10f, "Stable lanes leave room inside the 24m ramp width");
            lanes.Add(Mathf.FloorToInt(lane));
        }
        Check(lanes.Count == 20, "Stable slot lanes use the full broad approach");
        Vector3 core = map.HordeManager!.CorePosition!();
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
            foreach (float lateral in new[] { -40f, 0f, 40f })
            {
                Vector3 start = outward * 110f + across * lateral + Vector3.Up * ground;
                Vector3 route = FpsVerticalSlice.StrongholdMovementTarget(start, core, side, ground);
                Check(route.Dot(outward) >= 60f && Mathf.Abs(route.Dot(across)) <= 10f,
                    "Exterior approach enters a broad cardinal ramp");
            }
        }

        // A small separate manager reuses the actual map world and delegates, so
        // tests exercise production motion, gravity, ramps and GPU full drive.
        FpsHordeManager source = map.HordeManager;
        FpsHordeSettings settings = (FpsHordeSettings)source.Settings.Duplicate(true);
        settings.Capacity = 100;
        settings.PlayerAggroRadius = 0f;
        settings.RollingEnabled = false;
        settings.StackingEnabled = false;
        settings.GpuCrowdRenderingEnabled = false;
        var crowd = new FpsHordeManager
        {
            Name = "StrongholdRouteProbe", Settings = settings,
            SurfaceHeight = source.SurfaceHeight, SurfaceSupportHeight = source.SurfaceSupportHeight,
            MaxFootHeight = source.MaxFootHeight, BodyWorldResolver = source.BodyWorldResolver,
            ConstrainMovement = source.ConstrainMovement, ConstrainBodyMovement = source.ConstrainBodyMovement,
            ResolveMovementTarget = source.ResolveMovementTarget,
            ResolveMovementTargetForSlot = source.ResolveMovementTargetForSlot,
            ResolveObjectiveTarget = source.ResolveObjectiveTarget,
            CorePosition = source.CorePosition, PlayerPosition = () => Vector3.Zero,
            IsPlayerAlive = () => false, CanAttack = (_, _) => false,
            GpuCrowdWorldProvider = source.GpuCrowdWorldProvider,
        };
        map.AddChild(crowd);
        crowd.SetPhysicsProcess(false);
        try
        {
            var archetype = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
            archetype.MoveSpeed = 14f;
            var handles = new List<FpsHordeHandle>();
            for (int side = 0; side < 4; side++)
            foreach (float lateral in new[] { -30f, 0f, 30f })
            {
                Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
                handles.Add(crowd.Spawn(archetype, FpsEnemyRole.Bruiser, outward * 110f + across * lateral));
            }
            bool nativeGpu = DisplayServer.GetName() != "headless" && RenderingServer.GetCurrentRenderingDriverName() == "vulkan";
            MethodInfo step = typeof(FpsHordeManager).GetMethod("Simulate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var crossedRing = new HashSet<int>();
            var crossedCore = new HashSet<int>();
            for (int tick = 0; tick < 600; tick++)
            {
                object start = crowd.SaveGpuProbeState();
                if (nativeGpu) crowd.RunCpuProbeFullDrive(1f / 30f);
                else
                {
                    settings.GpuCrowdContactsEnabled = false;
                    step.Invoke(crowd, new object[] { 1f / 30f });
                }
                if (nativeGpu)
                {
                    object cpu = crowd.SaveGpuProbeState();
                    crowd.RestoreGpuProbeState(start);
                    settings.GpuCrowdContactsEnabled = true;
                    step.Invoke(crowd, new object[] { 1f / 30f });
                    Check(crowd.LastCrowdContactBackend == "GPU" && crowd.LastGpuCrowdDriveResolved,
                        "Native ramp traversal uses GPU full drive without fallback: " + crowd.GpuCrowdContactFallbackReason);
                    crowd.CompareGpuProbeState(cpu, .004f);
                    crowd.CompareGpuProbeDrive(cpu, .004f);
                    checks += 2;
                }
                foreach (FpsHordeHandle handle in handles)
                {
                    Check(crowd.TryGet(handle, out FpsHordeEnemySnapshot enemy), "Route body remains alive");
                    float radius = Mathf.Max(Mathf.Abs(enemy.Position.X), Mathf.Abs(enemy.Position.Z));
                    if (radius < 61f && enemy.Position.Y >= ground + 2.8f) crossedRing.Add(handle.Slot);
                    if (radius < 15f && enemy.Position.Y >= ground + 5.8f) crossedCore.Add(handle.Slot);
                    Check(radius > 100f || enemy.Position.Y >= ground - .1f, $"Routes never enter below-ground platforms: slot{handle.Slot} tick{tick} {enemy.Position}");
                }
            }
            Check(crossedRing.Count == handles.Count, "All four broad approaches actually climb the middle terrace");
            Check(crossedCore.Count == handles.Count, "All bodies actually climb the core ramp and reach the raised core");
            crowd.Clear();
            crowd.SurfaceHeight = _ => ground + 6f;
            var striker = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
            striker.DesiredRange = 8f;
            striker.MoveSpeed = 14f;
            FpsHordeHandle rangedHandle = crowd.Spawn(striker, FpsEnemyRole.Striker, Vector3.Right * 12f);
            for (int tick = 0; tick < 60; tick++)
            {
                object start = crowd.SaveGpuProbeState();
                if (nativeGpu) crowd.RunCpuProbeFullDrive(1f / 30f);
                else step.Invoke(crowd, new object[] { 1f / 30f });
                if (nativeGpu)
                {
                    object cpu = crowd.SaveGpuProbeState();
                    crowd.RestoreGpuProbeState(start);
                    step.Invoke(crowd, new object[] { 1f / 30f });
                    Check(crowd.LastGpuCrowdDriveResolved, "Ranged stopping regression uses actual GPU drive");
                    crowd.CompareGpuProbeState(cpu, .004f);
                    crowd.CompareGpuProbeDrive(cpu, .004f);
                    checks += 2;
                }
            }
            Check(crowd.TryGet(rangedHandle, out FpsHordeEnemySnapshot rangedEnemy), "Ranged stopping body remains alive");
            float stopDistance = new Vector2(rangedEnemy.Position.X - core.X, rangedEnemy.Position.Z - core.Z).Length();
            Check(stopDistance >= 7.5f && stopDistance <= 8.5f,
                $"Ranged enemies preserve their final core stopping range on the elevated tier: {stopDistance}");
            GD.Print($"Stronghold routing passed {checks} checks ({(nativeGpu ? "paired CPU/GPU" : "CPU")}).");
        }
        finally { crowd.Free(); }
        return checks;
    }

    private static Vector3 Outward(int side) => side switch
    { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
}
