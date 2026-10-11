using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private bool UsesFortifications => IsHillMap && HillMapSettings!.FortificationsEnabled;
	private float FortWallThickness => Mathf.Clamp(HillMapSettings!.FortWallThickness, 0.5f, 6f);
	private float FortInteriorHalf => Mathf.Clamp(HillMapSettings!.RoomHalfSize, 6f, HillSummitHalf - FortWallThickness);
	private float HillDoorHeight => Mathf.Clamp(HillMapSettings!.DoorHeight, 3f, Mathf.Max(4f, HillMapSettings.RoomWallHeight) - 0.5f);
	private readonly List<FortSolid> _fortSolids = new();
	private readonly List<FortRamp> _fortRamps = new();
	internal FpsFortSpatialIndex? _fortSpatialIndex;
	internal bool FortSpatialQueriesEnabled { get; set; } = true;
	internal int FortSpatialIndexVersion { get; private set; }
	internal int FortSolidCount => _fortSolids.Count;
	internal int FortRampCount => _fortRamps.Count;
	private float _fortGeometryHalf;
	private readonly record struct FortSolid(Vector3 Center, Vector3 Size)
	{
		public float Bottom => Center.Y - Size.Y * 0.5f;
		public float Top => Center.Y + Size.Y * 0.5f;
		public bool Contains(Vector3 p, float r) => Mathf.Abs(p.X - Center.X) <= Size.X * 0.5f + r && Mathf.Abs(p.Z - Center.Z) <= Size.Z * 0.5f + r;
	}
	private readonly record struct FortRamp(Vector3 Outward, Vector3 Across, float AcrossCenter, float Width, float Start, float End, float Base, float Top)
	{
		public float HeightAt(Vector3 p) => Mathf.Lerp(Base, Top, Mathf.Clamp((p.Dot(Outward) - Start) / (End - Start), 0f, 1f));
		public bool Contains(Vector3 p, float r) => p.Dot(Outward) >= Start - r && p.Dot(Outward) <= End + r && Mathf.Abs(p.Dot(Across) - AcrossCenter) <= Width * 0.5f + r;
	}

	private void BuildFortifications()
	{
		_fortSolids.Clear(); _fortRamps.Clear();
		_fortSpatialIndex = null;
		FpsHillMapSettings settings = HillMapSettings!;
		float inside = FortInteriorHalf, thickness = FortWallThickness, room = HillRoomHalf;
		float outer = inside + thickness, wallHeight = Mathf.Max(4f, settings.RoomWallHeight);
		float ground = HillHeight, top = ground + wallHeight;
		float walk = Mathf.Clamp(settings.FortWallWalkWidth, thickness, 8f);
		float deck = Mathf.Clamp(settings.FortDeckThickness, 0.2f, 1f);
		float width = Mathf.Clamp(settings.FortPlatformWidth, 4f, 16f), depth = Mathf.Clamp(settings.FortPlatformDepth, 2f, 12f);
		float rampWidth = Mathf.Clamp(settings.FortAccessRampWidth, 2f, 6f), run = Mathf.Clamp(settings.FortAccessRampRun, 10f, Mathf.Max(10f, inside - 8f));
		float rail = Mathf.Clamp(settings.FortRailHeight, 0f, 1.2f);
		_fortGeometryHalf = outer + depth + 0.15f;
		Color wallColor = settings.RoomColor, deckColor = wallColor.Lightened(0.12f);
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side), across = new(-outward.Z, 0, outward.X);
			bool door = HillDoorEnabled(side);
			float panelWidth = door ? outer - HillDoorWidth * 0.5f : outer * 2f;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				Vector3 center = outward * room + (door ? across * ((outer + HillDoorWidth * 0.5f) * 0.5f * sign) : Vector3.Zero);
				AddFortSolid($"FortWall{side}_{sign}", center + Vector3.Up * (ground + wallHeight * 0.5f), CardinalSize(side, panelWidth, wallHeight, thickness), wallColor);
				if (!door) break;
			}
			if (door)
				AddFortSolid($"FortLintel{side}", outward * room + Vector3.Up * (ground + HillDoorHeight + (wallHeight - HillDoorHeight) * 0.5f), CardinalSize(side, HillDoorWidth, wallHeight - HillDoorHeight, thickness), wallColor);
			AddFortSolid($"FortWallWalk{side}", outward * (outer - walk * 0.5f) + Vector3.Up * (top - deck * 0.5f), CardinalSize(side, outer * 2f, deck, walk), deckColor);
			if (!door) continue;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				float flank = sign * (HillDoorWidth * 0.5f + 0.5f + width * 0.5f);
				Vector3 platform = outward * (outer + depth * 0.5f) + across * flank;
				AddFortSolid($"FortPlatform{side}_{sign}", platform + Vector3.Up * (top - deck * 0.5f), CardinalSize(side, width, deck, depth), deckColor);
				// Side guardrails leave the outward firing edge open.
				if (rail > 0f) foreach (int edge in new[] { -1, 1 })
					AddFortSolid($"FortPlatformRail{side}_{sign}_{edge}", platform + across * (edge * (width * 0.5f - 0.06f)) + Vector3.Up * (top + rail * 0.5f), CardinalSize(side, 0.12f, rail, depth), wallColor.Darkened(0.1f));
				FortRamp ramp = new(outward, across, flank, rampWidth, outer - walk - run, outer - walk, ground, top);
				_fortRamps.Add(ramp);
				BuildFortRamp($"FortAccessRamp{side}_{sign}", ramp, deckColor.Darkened(0.08f));
				// Visual angled braces sit below the deck; support/collision comes from its actual slab.
				foreach (int edge in new[] { -1, 1 })
				{
					Vector3 lower = outward * outer + across * (flank + edge * width * 0.35f) + Vector3.Up * (ground + wallHeight * 0.25f);
					Vector3 upper = outward * (outer + depth * 0.85f) + across * (flank + edge * width * 0.35f) + Vector3.Up * (top - deck);
					MeshInstance3D brace = AddDefenseVisual(new BoxMesh { Size = new Vector3(0.3f, 0.3f, lower.DistanceTo(upper)) }, (lower + upper) * 0.5f, wallColor.Darkened(0.18f));
					brace.LookAt(upper);
				}
			}
			AddMapLabel($"{new[] { "NORTH", "EAST", "SOUTH", "WEST" }[side]} GATE", outward * room + Vector3.Up * (top + 1.3f), new Color(0.45f, 0.9f, 1f), 38);
		}
		RebuildFortSpatialIndex();
	}

	private void RebuildFortSpatialIndex()
	{
		var solids = new FpsFortSpatialIndex.Bounds[_fortSolids.Count];
		for (int i = 0; i < solids.Length; i++)
		{
			FortSolid solid = _fortSolids[i];
			solids[i] = new(solid.Center.X - solid.Size.X * 0.5f, solid.Center.Z - solid.Size.Z * 0.5f,
				solid.Center.X + solid.Size.X * 0.5f, solid.Center.Z + solid.Size.Z * 0.5f);
		}
		var ramps = new FpsFortSpatialIndex.Bounds[_fortRamps.Count];
		for (int i = 0; i < ramps.Length; i++)
		{
			FortRamp ramp = _fortRamps[i];
			Vector3 center = ramp.Outward * ((ramp.Start + ramp.End) * 0.5f) + ramp.Across * ramp.AcrossCenter;
			float halfX = (Mathf.Abs(ramp.Outward.X) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.X) * ramp.Width) * 0.5f;
			float halfZ = (Mathf.Abs(ramp.Outward.Z) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.Z) * ramp.Width) * 0.5f;
			ramps[i] = new(center.X - halfX, center.Z - halfZ, center.X + halfX, center.Z + halfZ);
		}
		_fortSpatialIndex = FpsFortSpatialIndex.Create(solids, ramps);
		FortSpatialIndexVersion++;
	}

	private FpsFortSpatialIndex.Cell FortCandidates(Vector3 from, Vector3 desired, float radius)
		=> FortSpatialQueriesEnabled && _fortSpatialIndex != null
			? _fortSpatialIndex.Query(from, desired, Mathf.Max(0.05f, radius + 0.1f)) : default;
	private FpsFortSpatialIndex.Candidates SolidCandidates(FpsFortSpatialIndex.Cell cell)
		=> new(cell.Solids, _fortSolids.Count, FortSpatialQueriesEnabled && _fortSpatialIndex != null);
	private FpsFortSpatialIndex.Candidates RampCandidates(FpsFortSpatialIndex.Cell cell)
		=> new(cell.Ramps, _fortRamps.Count, FortSpatialQueriesEnabled && _fortSpatialIndex != null);

	private static Vector3 CardinalSize(int side, float width, float height, float depth) => side % 2 == 0 ? new(width, height, depth) : new(depth, height, width);
	private void AddFortSolid(string name, Vector3 center, Vector3 size, Color color)
	{
		_fortSolids.Add(new(center, size));
		CreateArenaBlock(_arenaRoot, name, size, center, color);
	}

	private void BuildFortRamp(string name, FortRamp ramp, Color color)
	{
		Vector3 Point(float along, float across, float y) => ramp.Outward * along + ramp.Across * (ramp.AcrossCenter + across) + Vector3.Up * y;
		float half = ramp.Width * 0.5f;
		Vector3[] points =
		[
			Point(ramp.Start, -half, ramp.Base), Point(ramp.Start, half, ramp.Base),
			Point(ramp.End, -half, ramp.Base), Point(ramp.End, half, ramp.Base),
			Point(ramp.End, -half, ramp.Top), Point(ramp.End, half, ramp.Top),
		];
		using SurfaceTool tool = new(); tool.Begin(Mesh.PrimitiveType.Triangles);
		foreach (int index in new[] { 0, 1, 5, 0, 5, 4, 0, 4, 2, 1, 3, 5, 2, 4, 5, 2, 5, 3, 0, 2, 3, 0, 3, 1 }) tool.AddVertex(points[index]);
		tool.GenerateNormals();
		StaticBody3D body = new() { Name = name, CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } });
		body.AddChild(new MeshInstance3D { Mesh = tool.Commit(), MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
		_arenaRoot.AddChild(body);
	}

	private float SampleFortSupport(Vector3 previous, Vector3 desired, float radius)
	{
		float support = SampleHillSurfaceHeight(desired);
		if (Mathf.Abs(desired.X) > _fortGeometryHalf + radius || Mathf.Abs(desired.Z) > _fortGeometryHalf + radius) return support;
		return SampleFortSupport(previous, desired, radius, support, FortCandidates(previous, desired, radius));
	}

	private float SampleFortSupport(Vector3 previous, Vector3 desired, float radius, float support, FpsFortSpatialIndex.Cell candidates)
	{
		if (Mathf.Abs(desired.X) > _fortGeometryHalf + radius || Mathf.Abs(desired.Z) > _fortGeometryHalf + radius) return support;
		float rampLanding = float.NegativeInfinity;
		foreach (int index in RampCandidates(candidates))
		{
			FortRamp ramp = _fortRamps[index];
			// Rounded bodies reach the deck edge before their foot centre reaches the ramp end.
			if (ramp.Contains(previous, 0.05f) && previous.Y >= ramp.HeightAt(previous) - 0.05f
				&& previous.Y >= ramp.Top - 0.3f && desired.Dot(ramp.Outward) >= ramp.End - radius
				&& desired.Dot(ramp.Outward) <= ramp.End + radius + 0.1f
				&& Mathf.Abs(desired.Dot(ramp.Across) - ramp.AcrossCenter) <= ramp.Width * 0.5f)
				rampLanding = Mathf.Max(rampLanding, ramp.Top);
			if (!ramp.Contains(desired, 0f)) continue;
			float rampHeight = ramp.HeightAt(desired);
			bool arrivedFromAbove = previous.Y >= rampHeight - 0.001f;
			bool walkingUp = (ramp.Contains(previous, 0.05f) && previous.Y >= ramp.HeightAt(previous) - 0.05f)
				|| (previous.Y >= ramp.Base - 0.05f && rampHeight <= previous.Y + 0.25f);
			if (arrivedFromAbove || walkingUp) support = Mathf.Max(support, rampHeight);
		}
		foreach (int index in SolidCandidates(candidates))
		{
			FortSolid solid = _fortSolids[index];
			if ((previous.Y >= solid.Top - 0.001f || Mathf.Abs(solid.Top - rampLanding) < 0.001f) && solid.Contains(desired, radius))
				support = Mathf.Max(support, solid.Top);
		}
		return support;
	}

	private float SampleFortMaxFoot(Vector3 position, float radius, float height)
	{
		if (Mathf.Abs(position.X) > _fortGeometryHalf + radius || Mathf.Abs(position.Z) > _fortGeometryHalf + radius) return float.PositiveInfinity;
		return SampleFortMaxFoot(position, radius, height, FortCandidates(position, position, radius));
	}

	private float SampleFortMaxFoot(Vector3 position, float radius, float height, FpsFortSpatialIndex.Cell candidates)
	{
		float cap = float.PositiveInfinity;
		if (Mathf.Abs(position.X) > _fortGeometryHalf + radius || Mathf.Abs(position.Z) > _fortGeometryHalf + radius) return cap;
		foreach (int index in SolidCandidates(candidates))
		{
			FortSolid solid = _fortSolids[index];
			if (position.Y < solid.Bottom && solid.Contains(position, radius)) cap = Mathf.Min(cap, solid.Bottom - height);
		}
		return cap;
	}

	private Vector3 ConstrainFortBody(Vector3 from, Vector3 desired, float radius, float height)
	{
		float limit = HillPlayableHalf - radius;
		desired.X = Mathf.Clamp(desired.X, -limit, limit); desired.Z = Mathf.Clamp(desired.Z, -limit, limit);
		float bounds = _fortGeometryHalf + radius;
		if (Mathf.Max(from.X, desired.X) < -bounds || Mathf.Min(from.X, desired.X) > bounds || Mathf.Max(from.Z, desired.Z) < -bounds || Mathf.Min(from.Z, desired.Z) > bounds)
		{ desired.Y = Mathf.Max(desired.Y, SampleHillSurfaceHeight(desired)); return desired; }
		desired.Y = Mathf.Max(desired.Y, SampleFortSupport(from, desired, radius));
		// Sliding only shortens the original XZ sweep, so its candidate set stays conservative.
		FpsFortSpatialIndex.Cell candidates = FortCandidates(from, desired, radius);
		foreach (int index in SolidCandidates(candidates))
		{
			FortSolid solid = _fortSolids[index];
			if (Mathf.Min(from.Y, desired.Y) < solid.Top - 0.001f && Mathf.Max(from.Y, desired.Y) + height > solid.Bottom + 0.001f)
				SlideHillWall(from, ref desired, solid.Center, solid.Size, radius);
		}
		foreach (int index in RampCandidates(candidates))
		{
			FortRamp ramp = _fortRamps[index];
			if (ramp.Contains(desired, radius) && desired.Y < ramp.HeightAt(desired) - 0.05f)
			{
				Vector3 center = ramp.Outward * ((ramp.Start + ramp.End) * 0.5f) + ramp.Across * ramp.AcrossCenter;
				Vector3 size = new(Mathf.Abs(ramp.Outward.X) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.X) * ramp.Width, 0f,
					Mathf.Abs(ramp.Outward.Z) * (ramp.End - ramp.Start) + Mathf.Abs(ramp.Across.Z) * ramp.Width);
				SlideHillWall(from, ref desired, center, size, radius);
			}
		}
		return desired;
	}
}
