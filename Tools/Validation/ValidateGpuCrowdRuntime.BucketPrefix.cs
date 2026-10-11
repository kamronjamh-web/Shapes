using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateGpuCrowdRuntime
{
    private async Task ValidateBucketPrefixParity()
    {
        if (_headless) { GD.Print("LIMIT: bounded GPU bucket-prefix parity requires native Vulkan."); return; }
        const int capacity = 2048;
        foreach (int budget in new[] { 8, 32, 64 })
        {
            var crowd = new FpsHordeManager
            {
                ProcessMode = ProcessModeEnum.Disabled,
                Settings = new FpsHordeSettings { Capacity = capacity, MapExtent = 2048f,
                    SoftCollisionEnabled = true, ContactIterations = 1, ContactNeighborBudget = budget,
                    StackingEnabled = true, MaxPileHeight = 12f, RollingEnabled = false },
                SurfaceHeight = _ => -1000f,
            };
            AddChild(crowd);
            try
            {
                using var kernel = new GpuCrowdKernel(capacity);
                foreach (string scenario in new[] { "hidden-valid-tail", "mixed-prefix-with-self", "repeated-bucket-visits" })
                {
                    crowd.Clear();
                    int k = budget * 8;
                    Vector3I denseCell = scenario == "repeated-bucket-visits" ? new(0, 1, 0) : Vector3I.Zero;
                    Vector3 collision = FindPrefixCollision(denseCell, kernel.Buckets);
                    var archetype = new FpsEnemyArchetypeSettings { CollisionRadius = .4f, CollisionHeight = 1f };
                    List<FpsHordeHandle> handles = new(capacity);
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        Vector3 point = collision;
                        if (slot == 0 || slot == 1) point = new Vector3(.15f + slot * .08f, .1f, .15f);
                        if (scenario != "hidden-valid-tail" && slot == capacity - 1)
                        {
                            // Search radius is .85: .25 feet reaches both -1 and
                            // +1 cells. At .1 the positive side ends in cell0,
                            // omitting both cells that revisit the short bucket.
                            float xz = scenario == "repeated-bucket-visits" ? .25f : .1f;
                            point = new Vector3(xz, .1f, xz);
                        }
                        if (scenario == "mixed-prefix-with-self" && slot >= 1024 && slot < 1024 + k + 20 && slot % 5 == 0)
                            point = new Vector3(.18f + slot % 3 * .02f, .1f, .2f);
                        if (scenario == "repeated-bucket-visits" && slot is 100 or 102 or 104)
                            point = new Vector3(1.1f, .1f, -.9f);
                        FpsHordeHandle handle = crowd.Spawn(archetype, FpsEnemyRole.Bruiser, point);
                        if (handle == FpsHordeHandle.Invalid || !crowd.TrySetPosition(handle, point))
                            throw new InvalidOperationException("Could not place a bucket-prefix fixture body.");
                        handles.Add(handle);
                    }
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        bool keep = slot <= 1 || slot == capacity - 1 || slot >= 1024 && slot < 1024 + k + 20
                            || scenario == "repeated-bucket-visits" && slot is 100 or 102 or 104;
                        if (!keep) crowd.ApplyDamage(handles[slot], 100000f);
                        else
                        {
                            crowd.TryGet(handles[slot], out var body);
                            crowd.SetGpuProbeMotion(handles[slot], body.Position + Vector3.Up * .15f, Vector3.Right, .15f, -.5f);
                        }
                    }
                    crowd.FlushDeathEvents();
                    Check(crowd.HighestLiveSlot == capacity - 1 && !crowd.IsAlive(handles[500]),
                        $"Prefix/{budget}/{scenario}: sparse holes retain a high live slot");
                    var bodies = new GpuCrowdBody[capacity];
                    GpuCrowdParameters parameters = crowd.PackGpuCrowd(bodies, Dt);
                    Check(parameters.HorizontalCell == 1f && parameters.VerticalCell == 1f,
                        $"Prefix/{budget}/{scenario}: fixture cells have exact unit dimensions");
                    var buckets = PrefixReferenceBuckets(bodies, kernel.Buckets);
                    Check(buckets[PrefixHash(denseCell, kernel.Buckets)].Count > k,
                        $"Prefix/{budget}/{scenario}: hashed occupancy exceeds K={k}");
                    int probe = scenario == "hidden-valid-tail" ? 0 : capacity - 1;
                    var expectedCounts = PrefixReferenceCounts(probe, bodies, parameters, buckets, kernel.Buckets);
                    if (scenario == "hidden-valid-tail")
                        Check(expectedCounts.Accepted == 0 && expectedCounts.Scanned == k,
                            $"Prefix/{budget}: geometrically valid low-slot contact hidden beyond retained prefix");
                    else if (scenario == "mixed-prefix-with-self")
                        Check(expectedCounts.Accepted == budget && expectedCounts.Scanned > expectedCounts.Accepted,
                            $"Prefix/{budget}: self and mismatched cells consume scans before valid contacts");
                    else
                        Check(expectedCounts.Repeated && expectedCounts.Scanned == k,
                            $"Prefix/{budget}: repeated bucket visits share the global scan limit");

                    object initial = crowd.SaveGpuProbeState();
                    crowd.RunCpuProbeContacts(Dt);
                    object expected = crowd.SaveGpuProbeState();
                    crowd.RestoreGpuProbeState(initial);
                    kernel.Upload(bodies);
                    byte[] output = kernel.Run(parameters, measureGpuTime: false).Output;
                    ValidatePrefixCounts(output, bodies, parameters, buckets, kernel.Buckets, budget, scenario);
                    crowd.ApplyGpuCrowdResults(output);
                    var parity = crowd.CompareGpuProbeState(expected, .001f);
                    Check(parity.Compared == crowd.ActiveCount,
                        $"Prefix/{budget}/{scenario}: CPU/GPU positions, pressure and vertical velocity match ({parity.Compared}, max error {parity.MaxError:G6})");
                }
            }
            finally { crowd.QueueFree(); }
            await Frames(1);
        }
    }

    private static int PrefixHash(Vector3I cell, int count)
        => (int)(unchecked((uint)cell.X * 73856093u ^ (uint)cell.Y * 19349663u ^ (uint)cell.Z * 83492791u) & (uint)(count - 1));

    private static Vector3 FindPrefixCollision(Vector3I desired, int buckets)
    {
        int wanted = PrefixHash(desired, buckets);
        for (int x = 4; x <= 96; x++)
        for (int y = 4; y <= 96; y++)
        for (int z = -96; z <= 96; z++)
        {
            Vector3I cell = new(x, y, z);
            if (PrefixHash(cell, buckets) == wanted) return new Vector3(x + .25f, y + .25f, z + .25f);
        }
        throw new InvalidOperationException("Could not find a distant exact hash collision for prefix fixture.");
    }

    private static Dictionary<int, List<int>> PrefixReferenceBuckets(GpuCrowdBody[] bodies, int count)
    {
        var buckets = new Dictionary<int, List<int>>();
        // Construct the complete CPU order independently of the optimized shader.
        for (int slot = bodies.Length - 1; slot >= 0; slot--)
        {
            if (bodies[slot].WorldVelocityAlive.W == 0f) continue;
            Vector4 p = bodies[slot].PositionRadius;
            int hash = PrefixHash(new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)), count);
            if (!buckets.TryGetValue(hash, out var list)) buckets.Add(hash, list = new List<int>());
            list.Add(slot);
        }
        return buckets;
    }

    private static (int Accepted, int Scanned, bool Repeated) PrefixReferenceCounts(int slot, GpuCrowdBody[] bodies,
        GpuCrowdParameters p, Dictionary<int, List<int>> buckets, int bucketCount)
    {
        ref GpuCrowdBody body = ref bodies[slot];
        Vector3 position = new(body.PositionRadius.X, body.PositionRadius.Y, body.PositionRadius.Z);
        float search = body.PositionRadius.W + p.MaxRadius + .05f;
        int minX = Mathf.FloorToInt(position.X - search), maxX = Mathf.FloorToInt(position.X + search);
        int minZ = Mathf.FloorToInt(position.Z - search), maxZ = Mathf.FloorToInt(position.Z + search);
        int minY = Mathf.FloorToInt(Mathf.Min(position.Y, body.PreviousHeight.Y) - p.MaxHeight - .05f);
        int maxY = Mathf.FloorToInt(Mathf.Max(position.Y, body.PreviousHeight.Y) + body.PreviousHeight.W + .05f);
        int accepted = 0, scanned = 0, budget = (int)p.Budget;
        bool repeated = false;
        HashSet<int> visited = new();
        for (int y = minY; y <= maxY && accepted < budget && scanned < budget * 8; y++)
        for (int z = minZ; z <= maxZ && accepted < budget && scanned < budget * 8; z++)
        for (int x = minX; x <= maxX && accepted < budget && scanned < budget * 8; x++)
        {
            int hash = PrefixHash(new Vector3I(x, y, z), bucketCount);
            if (!buckets.TryGetValue(hash, out var list)) continue;
            if (!visited.Add(hash)) repeated = true;
            foreach (int otherSlot in list)
            {
                if (accepted >= budget || scanned >= budget * 8) break;
                scanned++;
                ref GpuCrowdBody other = ref bodies[otherSlot];
                Vector4 q = other.PositionRadius;
                if (Mathf.FloorToInt(q.X) != x || Mathf.FloorToInt(q.Y) != y || Mathf.FloorToInt(q.Z) != z
                    || otherSlot == slot || other.WorldVelocityAlive.W == 0f) continue;
                if (q.Y + other.PreviousHeight.W < Mathf.Min(position.Y, body.PreviousHeight.Y) - .05f
                    || q.Y > Mathf.Max(position.Y, body.PreviousHeight.Y) + body.PreviousHeight.W + .05f) continue;
                float dx = position.X - q.X, dz = position.Z - q.Z, width = body.PositionRadius.W + q.W;
                if (dx * dx + dz * dz >= width * width) continue;
                float height = (body.PreviousHeight.W + other.PreviousHeight.W) * .5f;
                float centerDelta = position.Y + body.PreviousHeight.W * .5f - q.Y - other.PreviousHeight.W * .5f;
                if (Mathf.Abs(centerDelta) > height + .25f && body.PreviousHeight.Y < q.Y + other.PreviousHeight.W) continue;
                accepted++;
            }
        }
        return (accepted, scanned, repeated);
    }

    private void ValidatePrefixCounts(byte[] output, GpuCrowdBody[] bodies, GpuCrowdParameters parameters,
        Dictionary<int, List<int>> buckets, int bucketCount, int budget, string scenario)
    {
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(output);
        int compared = 0;
        for (int slot = 0; slot < bodies.Length; slot++)
        {
            if (bodies[slot].WorldVelocityAlive.W == 0f) continue;
            var expected = PrefixReferenceCounts(slot, bodies, parameters, buckets, bucketCount);
            if (results[slot].VelocityCounts.Y != expected.Accepted || results[slot].VelocityCounts.Z != expected.Scanned)
                throw new InvalidOperationException($"Prefix/{budget}/{scenario}/slot{slot}: accepted/scanned GPU={results[slot].VelocityCounts.Y}/{results[slot].VelocityCounts.Z}, CPU-order reference={expected.Accepted}/{expected.Scanned}");
            compared++;
        }
        Check(compared > 0, $"Prefix/{budget}/{scenario}: exact accepted/scanned counts match full descending bucket reference ({compared})");
    }
}
