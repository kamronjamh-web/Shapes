using System;
using System.Text.Json;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	internal const string SteppedStrongholdScenePath = "res://Resources/Forts/SteppedStronghold/SteppedStronghold.glb";
	private const string SteppedStrongholdProxyPath = "res://Resources/Forts/SteppedStronghold/CollisionProxies.tres";

	private void BuildSteppedStronghold()
	{
		float offset = HillHeight - 8f;
		Resource proxyResource = GD.Load<Resource>(SteppedStrongholdProxyPath)
			?? throw new InvalidOperationException("Stepped stronghold collision proxy resource is missing.");
		using JsonDocument data = JsonDocument.Parse(proxyResource.GetMeta("proxies").AsString());
		JsonElement solids = data.RootElement.GetProperty("solids"), ramps = data.RootElement.GetProperty("ramps");
		if (solids.GetArrayLength() != 39 || ramps.GetArrayLength() != 16)
			throw new InvalidOperationException("Stepped stronghold requires its approved 39 solid / 16 ramp proxies.");
		_fortGeometryHalf = 112f;
		foreach (JsonElement item in solids.EnumerateArray())
		{
			JsonElement center = item.GetProperty("center_blender"), size = item.GetProperty("size_blender");
			Vector3 position = new(center[0].GetSingle(), center[2].GetSingle() + offset, -center[1].GetSingle());
			Vector3 dimensions = new(size[0].GetSingle(), size[2].GetSingle(), size[1].GetSingle());
			_fortSolids.Add(new(position, dimensions));
			StaticBody3D body = new() { Name = "Proxy_" + item.GetProperty("name").GetString(), Position = position, CollisionLayer = 1, CollisionMask = 0 };
			body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = dimensions } });
			_arenaRoot.AddChild(body);
		}
		foreach (JsonElement item in ramps.EnumerateArray())
		{
			Vector3 outward = HillOutward(item.GetProperty("side").GetInt32());
			// Blender's cardinal tangent also passes through the glTF axis conversion.
			Vector3 across = new(outward.Z, 0f, -outward.X);
			float start = item.GetProperty("start").GetSingle(), end = item.GetProperty("end").GetSingle();
			// Every CPU/GPU mask and height query expects increasing ramp coordinates.
			// An inward ascent therefore reverses the longitudinal axis, retaining heights.
			if (start > end) { outward = -outward; start = -start; end = -end; }
			FortRamp ramp = new(outward, across, item.GetProperty("across_center").GetSingle(),
				item.GetProperty("width").GetSingle(), start, end,
				item.GetProperty("base").GetSingle() + offset, item.GetProperty("top").GetSingle() + offset);
			_fortRamps.Add(ramp);
			BuildFortRamp("Proxy_" + item.GetProperty("name").GetString(), ramp, Colors.White, visible: false);
		}
		PackedScene scene = GD.Load<PackedScene>(SteppedStrongholdScenePath)
			?? throw new InvalidOperationException("Stepped stronghold Blender runtime scene is missing.");
		Node3D architecture = scene.Instantiate<Node3D>();
		architecture.Name = "SteppedStrongholdArchitecture";
		architecture.Position = Vector3.Up * offset;
		// The authored foundation top coincides with the hill at 8m. Lift only
		// its visual plate slightly so the two surfaces do not fight for depth.
		if (architecture.FindChild("CourtyardFoundation", true, false) is Node3D foundation)
			foundation.Position += Vector3.Up * 0.02f;
		_arenaRoot.AddChild(architecture);
		RebuildFortSpatialIndex();
	}

	// Objective/spawn/rebuild anchors need the top surface, while moving bodies use
	// previous-foot-aware support so they cannot teleport through platform sides.
	private float SampleDefenseAnchorHeight(Vector3 position)
	{
		float support = SampleHillSurfaceHeight(position);
		if (!UsesSteppedStronghold) return support;
		foreach (FortSolid solid in _fortSolids)
			if (solid.Contains(position, 0f)) support = Mathf.Max(support, solid.Top);
		foreach (FortRamp ramp in _fortRamps)
			if (ramp.Contains(position, 0f)) support = Mathf.Max(support, ramp.HeightAt(position));
		return support;
	}
}
