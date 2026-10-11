using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    // Manager already applies its current map-extent clamp before invoking this.
    // The original callbacks remain available for legacy worlds and validation.
    internal Vector3 ResolveHordeBodyWorld(Vector3 from, Vector3 desired, float radius, float height)
    {
        if (!UsesFortifications) return ResolveHordeBodyWorldReference(from, desired, radius, height);
        float bodyRadius = Mathf.Max(0f, radius), bodyHeight = Mathf.Max(0f, height);
        float limit = HillPlayableHalf - bodyRadius;
        desired.X = Mathf.Clamp(desired.X, -limit, limit);
        desired.Z = Mathf.Clamp(desired.Z, -limit, limit);
        Vector3 supportPosition = desired;
        float floor = SampleHillSurfaceHeight(desired);
        float bounds = _fortGeometryHalf + bodyRadius;
        bool crossesFort = !(Mathf.Max(from.X, desired.X) < -bounds || Mathf.Min(from.X, desired.X) > bounds
            || Mathf.Max(from.Z, desired.Z) < -bounds || Mathf.Min(from.Z, desired.Z) > bounds);
        FpsFortSpatialIndex.Cell candidates = crossesFort ? FortCandidates(from, desired, bodyRadius) : default;
        if (crossesFort)
        {
            floor = SampleFortSupport(from, desired, bodyRadius, floor, candidates);
            desired.Y = Mathf.Max(desired.Y, floor);
            // Exactly the original ordered solid/ramp narrow phases. Sliding only
            // shortens XZ motion, so the original swept mask remains conservative.
            foreach (int index in SolidCandidates(candidates))
            {
                FortSolid solid = _fortSolids[index];
                if (Mathf.Min(from.Y, desired.Y) < solid.Top - 0.001f
                    && Mathf.Max(from.Y, desired.Y) + bodyHeight > solid.Bottom + 0.001f)
                    SlideHillWall(from, ref desired, solid.Center, solid.Size, bodyRadius);
            }
            foreach (int index in RampCandidates(candidates))
            {
                FortRamp ramp = _fortRamps[index];
                if (ramp.Contains(desired, bodyRadius) && desired.Y < ramp.HeightAt(desired) - 0.05f)
                {
                    Vector3 center = ramp.Outward * ((ramp.Start + ramp.End) * 0.5f) + ramp.Across * ramp.AcrossCenter;
                    Vector3 size = new(Mathf.Abs(ramp.Outward.X) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.X) * ramp.Width, 0f,
                        Mathf.Abs(ramp.Outward.Z) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.Z) * ramp.Width);
                    SlideHillWall(from, ref desired, center, size, bodyRadius);
                }
            }
        }
        else desired.Y = Mathf.Max(desired.Y, floor);

        desired = ConstrainHordeGates(from, desired, radius, height);
        if (desired.X != supportPosition.X || desired.Z != supportPosition.Z)
        {
            // Gate restoration can move outside the original sweep; query again
            // after any XZ change. Support depends on prior feet and final XZ,
            // not on proposed Y, so unchanged XZ can reuse its first result.
            candidates = FortCandidates(from, desired, bodyRadius);
            floor = SampleFortSupport(from, desired, bodyRadius, SampleHillSurfaceHeight(desired), candidates);
        }
        Vector3 probe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
        float ceiling = SampleFortMaxFoot(probe, bodyRadius, bodyHeight, candidates);
        if (ceiling < floor)
        {
            desired.X = from.X;
            desired.Z = from.Z;
            // The swept candidate query includes from; reuse it for the low
            // passage fallback while evaluating support at the original XZ.
            floor = SampleFortSupport(from, desired, bodyRadius, SampleHillSurfaceHeight(desired), candidates);
            probe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
            ceiling = SampleFortMaxFoot(probe, bodyRadius, bodyHeight, candidates);
        }
        desired.Y = Mathf.Max(floor, Mathf.Min(desired.Y, ceiling));
        return desired;
    }

    internal Vector3 ResolveHordeBodyWorldReference(Vector3 from, Vector3 desired, float radius, float height)
    {
        desired = ConstrainHordeBodyWithGates(from, desired, radius, height);
        float floor = SampleHillCrowdSupportHeight(from, desired, radius, height);
        Vector3 probe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
        float ceiling = SampleHillCrowdMaxFootHeight(probe, radius, height);
        if (ceiling < floor)
        {
            desired.X = from.X;
            desired.Z = from.Z;
            floor = SampleHillCrowdSupportHeight(from, desired, radius, height);
            probe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
            ceiling = SampleHillCrowdMaxFootHeight(probe, radius, height);
        }
        desired.Y = Mathf.Max(floor, Mathf.Min(desired.Y, ceiling));
        return desired;
    }
}
