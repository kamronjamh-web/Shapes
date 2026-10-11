using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
	private Vector3 ApplyRollingGait(ref Enemy body, Vector3 desiredVelocity, float dt)
	{
		body.RollUnusedSeconds = 0f;
		if (!Settings.RollingEnabled || body.Role != FpsEnemyRole.Bruiser) return desiredVelocity;
		float requestedSpeed = desiredVelocity.Length();
		if (requestedSpeed <= 0.0001f) return Vector3.Zero;
		float activeTime = dt;
		if (body.RollPause > 0f)
		{
			float consumed = Mathf.Min(activeTime, body.RollPause);
			body.RollPause -= consumed;
			activeTime -= consumed;
			if (activeTime <= 0.000001f) return Vector3.Zero;
		}
		float width = Mathf.Max(0.2f, body.Radius * 2f);
		float cycle = width / requestedSpeed;
		float pause = Mathf.Min(Mathf.Clamp(Settings.RollingPauseSeconds, 0f, 0.3f), cycle * 0.65f);
		float duration = Mathf.Max(0.01f, cycle - pause);
		duration = Mathf.Max(duration, Mathf.Clamp(Settings.RollingDurationSeconds, 0f, 1f));
		float activeSpeed = width / duration;
		float remaining = Mathf.Max(0f, width - body.RollDistance);
		float distance = Mathf.Min(activeSpeed * activeTime, remaining);
		body.RollUnusedSeconds = Mathf.Max(0f, activeTime - distance / activeSpeed);
		body.RollNextPause = pause;
		return desiredVelocity / requestedSpeed * (distance / Mathf.Max(dt, 0.000001f));
	}

	private void UpdateRollingMotion()
	{
		foreach (int slot in EnumerateAliveSlots())
		{
			ref Enemy body = ref _enemies[slot];
			if (!body.Alive || body.Role != FpsEnemyRole.Bruiser) continue;
			body.PreviousRollOrientation = body.RollOrientation;
			if (!Settings.RollingEnabled) continue;
			Vector3 travel = body.Position - body.PreviousPosition;
			travel.Y = 0f;
			float distance = travel.Length();
			float width = Mathf.Max(0.2f, body.Radius * 2f);
			// Contacts and external pushes can move a resting cube too. Its visible roll follows
			// real displacement, while collision bounds remain upright for combat and piling.
			if (distance > 0.00001f)
			{
				Vector3 axis = Vector3.Up.Cross(travel / distance);
				float angle = distance / width * Mathf.Pi * 0.5f;
				body.RollOrientation = (new Quaternion(axis, angle) * body.RollOrientation).Normalized();
				body.RollDistance += distance;
			}
			// At large map coordinates the final fraction of a roll can be smaller
			// than one representable position step. Finish within two coordinate ULPs,
			// including a zero-displacement tick, instead of requesting that step forever.
			float coordinate = Mathf.Max(Mathf.Abs(body.Position.X), Mathf.Abs(body.Position.Z));
			float tolerance = Mathf.Max(0.00001f, 2f * (MathF.BitIncrement(coordinate) - coordinate));
			if (body.RollDistance >= width - tolerance)
			{
				body.RollDistance %= width;
				if (body.RollDistance <= tolerance || body.RollDistance >= width - tolerance) body.RollDistance = 0f;
				body.RollPause = Mathf.Max(body.RollPause, Mathf.Max(0f, body.RollNextPause - body.RollUnusedSeconds));
			}
		}
	}

	private Transform3D EnemyVisualTransform(in Enemy body, Vector3 feet, float blend)
	{
		Basis orientation = new(Vector3.Up, body.Yaw);
		if (Settings.RollingEnabled && body.Role == FpsEnemyRole.Bruiser)
			orientation = new Basis(body.PreviousRollOrientation.Slerp(body.RollOrientation, blend)) * orientation;
		Basis scaled = orientation * Basis.FromScale(new Vector3(body.Radius * 2f, body.Height, body.Radius * 2f));
		// Support the rotated box on its real foot height throughout an edge-to-edge roll.
		float halfHeight = (Mathf.Abs(scaled.X.Y) + Mathf.Abs(scaled.Y.Y) + Mathf.Abs(scaled.Z.Y)) * 0.5f;
		// The stable upright combat body is shorter than a cube balanced on a corner.
		// Tuck only the visual lift beneath lintels/ceilings so corners cannot poke through
		// the roof from below. Bodies already on the roof have no upper clearance cap.
		float visualRadius = 0.5f * Mathf.Max(Mathf.Abs(scaled.X.X) + Mathf.Abs(scaled.Y.X) + Mathf.Abs(scaled.Z.X),
			Mathf.Abs(scaled.X.Z) + Mathf.Abs(scaled.Y.Z) + Mathf.Abs(scaled.Z.Z));
		float ceilingFeet = MaxFootHeight?.Invoke(feet, visualRadius, halfHeight * 2f) ?? float.PositiveInfinity;
		feet.Y = Mathf.Min(feet.Y, ceilingFeet);
		return new Transform3D(scaled, feet + Vector3.Up * halfHeight);
	}

	/// <summary>World visual pose, including cube rolling. Valid until its slot is recycled.</summary>
	public Transform3D GetEnemyVisualTransform(FpsHordeHandle handle)
	{
		if (handle.Slot < 0 || handle.Slot >= _enemies.Length || _enemies[handle.Slot].Generation != handle.Generation)
			return Transform3D.Identity;
		ref Enemy body = ref _enemies[handle.Slot];
		return EnemyVisualTransform(body, body.Position, 1f);
	}

	public bool TryGetRollingState(FpsHordeHandle handle, out float distance, out float pauseSeconds, out Quaternion orientation)
	{
		distance = pauseSeconds = 0f;
		orientation = Quaternion.Identity;
		if (!IsAlive(handle)) return false;
		ref Enemy body = ref _enemies[handle.Slot];
		distance = body.RollDistance;
		pauseSeconds = body.RollPause;
		orientation = body.RollOrientation;
		return true;
	}
}
