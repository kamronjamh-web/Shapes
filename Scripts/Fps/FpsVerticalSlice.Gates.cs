using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private readonly List<FpsDefenseGate> _defenseGates = new();
	private readonly Vector3[] _defenseGateCenters = new Vector3[4];
	public IReadOnlyList<FpsDefenseGate> DefenseGates => _defenseGates;
	public bool IsFortMap => UsesFortifications;
	private static readonly string[] GateSideNames = { "N", "E", "S", "W" };
	private string DefenseGateStatus()
	{
		string status = "GATES";
		foreach (var gate in _defenseGates)
			status += $"   {GateSideNames[gate.SideIndex]}: {(gate.IsAlive ? Mathf.CeilToInt(gate.CurrentHealth).ToString() : "BREACHED")}";
		return status;
	}

	private void RebuildDefenseGates()
	{
		if (!UsesFortifications)
		{
			foreach (var gate in _defenseGates) { gate.CollisionLayer = 0; gate.QueueFree(); }
			_defenseGates.Clear();
			return;
		}
		for (int side = 0; side < 4; side++)
		{
			FpsDefenseGate? gate = _defenseGates.Find(g => g.SideIndex == side);
			if (!HillDoorEnabled(side))
			{
				if (gate != null) { gate.CollisionLayer = 0; gate.QueueFree(); _defenseGates.Remove(gate); }
				continue;
			}
			if (gate == null)
			{
				gate = new FpsDefenseGate { Name = $"DefenseGate{side}", SideIndex = side };
				_defenseGates.Add(gate);
				AddChild(gate);
			}
			gate.Position = HillOutward(side) * HillRoomHalf + Vector3.Up * HillHeight;
			_defenseGateCenters[side] = gate.GlobalPosition;
			gate.Reconfigure(CardinalSize(side, HillDoorWidth, HillDoorHeight, FortWallThickness), HillMapSettings!.GateMaxHealth, side);
		}
	}

	public void DebugResetDefenseGates()
	{
		foreach (var gate in _defenseGates) gate.ResetHealth();
	}

	private Vector3 GateTarget(FpsDefenseGate gate) => _defenseGateCenters[gate.SideIndex] + Vector3.Up * 1.1f;
	private FpsDefenseGate? GateAtTarget(Vector3 target)
	{
		foreach (var gate in _defenseGates)
			if (GateTarget(gate).DistanceSquaredTo(target) < 0.01f) return gate;
		return null;
	}

	private FpsDefenseGate? ApproachGate(Vector3 from)
	{
		if (!UsesFortifications || from.Y >= HillHeight + HillMapSettings!.RoomWallHeight - 0.001f
			|| (Mathf.Abs(from.X) < FortInteriorHalf && Mathf.Abs(from.Z) < FortInteriorHalf)) return null;
		FpsDefenseGate? closest = null;
		float best = float.PositiveInfinity;
		foreach (var gate in _defenseGates)
		{
			Vector3 offset = _defenseGateCenters[gate.SideIndex] - from; offset.Y = 0;
			float distance = offset.LengthSquared();
			if (distance < best) { best = distance; closest = gate; }
		}
		return closest;
	}

	private Vector3 ResolveHordeObjective(Vector3 from)
	{
		FpsDefenseGate? gate = ApproachGate(from);
		return gate?.IsAlive == true ? GateTarget(gate) : _defenseCore?.GlobalPosition ?? Vector3.Zero;
	}

	private Vector3 ResolveFortMovementTarget(Vector3 from, Vector3 target)
	{
		FpsDefenseGate? gate = ApproachGate(from);
		if (gate == null || Mathf.Abs(target.X) >= FortInteriorHalf || Mathf.Abs(target.Z) >= FortInteriorHalf) return target;
		Vector3 outward = HillOutward(gate.SideIndex), across = new(-outward.Z, 0, outward.X);
		float doorHalf = Mathf.Max(0.25f, HillDoorWidth * 0.5f - 1f);
		if (Mathf.Abs(from.Dot(across)) > doorHalf)
			return outward * (HillRoomHalf + 3f) + across * Mathf.Clamp(from.Dot(across) * 0.1f, -doorHalf, doorHalf) + Vector3.Up * HillHeight;
		return outward * (FortInteriorHalf - 3f) + Vector3.Up * HillHeight;
	}

	private Vector3 ConstrainHordeBodyWithGates(Vector3 from, Vector3 desired, float radius, float height)
	{
		desired = ConstrainHillCrowdBodyMovement(from, desired, radius, height);
		foreach (var gate in _defenseGates)
		{
			if (!gate.IsAlive) continue;
			// Gates are static between map rebuilds. Keep hot crowd callbacks in managed
			// data instead of fetching four native Node3D transforms per body/contact pass.
			Vector3 center = _defenseGateCenters[gate.SideIndex], half = gate.Dimensions * 0.5f;
			if (Mathf.Min(from.X, desired.X) > center.X + half.X + radius || Mathf.Max(from.X, desired.X) < center.X - half.X - radius
				|| Mathf.Min(from.Z, desired.Z) > center.Z + half.Z + radius || Mathf.Max(from.Z, desired.Z) < center.Z - half.Z - radius
				|| Mathf.Min(from.Y, desired.Y) >= center.Y + gate.Dimensions.Y - 0.001f
				|| Mathf.Max(from.Y, desired.Y) + height <= center.Y) continue;
			Vector3 outward = HillOutward(gate.SideIndex), across = new(-outward.Z, 0, outward.X);
			float normalOffset = (from - center).Dot(outward);
			float normalHalf = FortWallThickness * 0.5f + radius;
			// A restored gate can appear around a body. Push it to the nearest doorway face,
			// never sideways into a gatepost or through the newly closed barrier.
			if (Mathf.Abs(normalOffset) < normalHalf && Mathf.Abs((from - center).Dot(across)) < HillDoorWidth * 0.5f + radius)
			{
				float side = normalOffset >= 0f ? 1f : -1f;
				desired += outward * (center.Dot(outward) + side * (normalHalf + 0.001f) - desired.Dot(outward));
				continue;
			}
			SlideHillWall(from, ref desired, center, gate.Dimensions, radius);
		}
		return desired;
	}

	private void RefreshHillWorldExtent()
	{
		if (!IsHillMap) return;
		if (HordeSettings != null) HordeSettings.MapExtent = HillPlayableHalf * 2f;
		if (_player != null && GodotObject.IsInstanceValid(_player)) _player.Camera.Far = Mathf.Max(400f, HillPlayableHalf * 3f);
	}
}
