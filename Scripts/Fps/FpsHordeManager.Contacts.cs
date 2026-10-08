using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
	private Vector3[] _contactPositions = Array.Empty<Vector3>();
	public int StackedEnemyCount { get; private set; }
	public float HighestPileFeet { get; private set; }
	public double LastMovementMilliseconds { get; private set; }
	public double LastContactMilliseconds { get; private set; }
	public double LastAttackMilliseconds { get; private set; }

	private Vector3 ResolveBodyWorldPosition(Vector3 from, Vector3 desired, float radius, float height)
	{
		float half = Mathf.Max(0f, _extent * 0.5f - radius);
		desired.X = Mathf.Clamp(desired.X, -half, half);
		desired.Z = Mathf.Clamp(desired.Z, -half, half);
		if (ConstrainBodyMovement != null) desired = ConstrainBodyMovement(from, desired, radius, height);
		else if (ConstrainMovement != null) desired = ConstrainMovement(from, desired, radius);
		float floor = SurfaceSupportHeight?.Invoke(from, desired, radius, height) ?? SurfaceHeight?.Invoke(desired) ?? 0f;
		Vector3 clearanceProbe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
		float ceiling = MaxFootHeight?.Invoke(clearanceProbe, radius, height) ?? float.PositiveInfinity;
		if (ceiling < floor)
		{
			// A tall body cannot enter a low passage. Preserve its previous horizontal location.
			desired.X = from.X;
			desired.Z = from.Z;
			floor = SurfaceSupportHeight?.Invoke(from, desired, radius, height) ?? SurfaceHeight?.Invoke(desired) ?? 0f;
			clearanceProbe = new(desired.X, Mathf.Max(desired.Y, floor), desired.Z);
			ceiling = MaxFootHeight?.Invoke(clearanceProbe, radius, height) ?? float.PositiveInfinity;
		}
		desired.Y = Mathf.Max(floor, Mathf.Min(desired.Y, ceiling));
		return desired;
	}

	/// <summary>
	/// Rounded body contacts use bounded Jacobi relaxation. Crowding allows a moving rear body
	/// to climb the rounded surface ahead; gravity and live lower bodies supply actual support.
	/// There are no per-enemy physics nodes or permanently remembered support heights.
	/// </summary>
	private void SolveCrowdContacts(float dt)
	{
		int iterations = Math.Clamp(Settings.ContactIterations, 1, 4);
		bool contacts = Settings.SoftCollisionEnabled;
		bool stacking = contacts && Settings.StackingEnabled && Settings.MaxPileHeight > 0f;
		float climbStep = Mathf.Clamp(Settings.ClimbSpeed, 0f, 8f) * dt / iterations;
		float stiffness = 1f - Mathf.Exp(-Mathf.Clamp(Settings.ContactStiffness, 0.05f, 1f) * 60f * dt / iterations);
		int budget = Math.Clamp(Settings.ContactNeighborBudget, 8, 64);
		if (contacts)
		for (int pass = 0; pass < iterations; pass++)
		{
			BuildContactGrid();
			for (int slot = 0; slot < _enemies.Length; slot++)
			{
				ref Enemy body = ref _enemies[slot];
				if (!body.Alive) continue;
				Vector3 position = _contactPositions[slot];
				float search = body.Radius + _maxRadius + 0.05f;
				int minX = Mathf.FloorToInt((position.X - search) / _contactHorizontalCellSize);
				int maxX = Mathf.FloorToInt((position.X + search) / _contactHorizontalCellSize);
				int minZ = Mathf.FloorToInt((position.Z - search) / _contactHorizontalCellSize);
				int maxZ = Mathf.FloorToInt((position.Z + search) / _contactHorizontalCellSize);
				int minY = Mathf.FloorToInt((Mathf.Min(position.Y, body.PreviousPosition.Y) - _maxContactHeight - 0.05f) / _contactVerticalCellSize);
				int maxY = Mathf.FloorToInt((Mathf.Max(position.Y, body.PreviousPosition.Y) + body.Height + 0.05f) / _contactVerticalCellSize);
				Vector3 push = Vector3.Zero;
				float compression = 0f, support = float.NegativeInfinity, climb = float.NegativeInfinity;
				float climbFromLowerBody = float.NegativeInfinity;
				int accepted = 0, scanned = 0;
				float ground = SurfaceHeight?.Invoke(position) ?? 0f;
				float worldSupport = SurfaceSupportHeight?.Invoke(position, position, body.Radius, body.Height) ?? ground;
				float pileLimit = ground + Mathf.Clamp(Settings.MaxPileHeight, 0f, 12f);
				Vector3 drive = body.DesiredVelocity.Normalized();
				for (int y = minY; y <= maxY && accepted < budget && scanned < budget * 8; y++)
				for (int z = minZ; z <= maxZ && accepted < budget && scanned < budget * 8; z++)
				for (int x = minX; x <= maxX && accepted < budget && scanned < budget * 8; x++)
				for (int otherSlot = _contactHeads[ContactBucket(x, y, z)]; otherSlot >= 0 && accepted < budget && scanned < budget * 8; otherSlot = _contactNext[otherSlot])
				{
					scanned++;
					if (_contactCellX[otherSlot] != x || _contactCellY[otherSlot] != y || _contactCellZ[otherSlot] != z) continue;
					ref Enemy other = ref _enemies[otherSlot];
					if (otherSlot == slot || !other.Alive) continue;
					Vector3 otherPosition = _contactPositions[otherSlot];
					// Only overlapping bodies or surfaces crossed while falling can make contact.
					// Deep lower layers must not consume the budget before nearby supports.
					if (otherPosition.Y + other.Height < Mathf.Min(position.Y, body.PreviousPosition.Y) - 0.05f
						|| otherPosition.Y > Mathf.Max(position.Y, body.PreviousPosition.Y) + body.Height + 0.05f) continue;
					Vector3 away = position - otherPosition;
					away.Y = 0f;
					float distanceSquared = away.LengthSquared();
					float width = body.Radius + other.Radius;
					if (distanceSquared >= width * width) continue;
					float height = (body.Height + other.Height) * 0.5f;
					float centerDelta = position.Y + body.Height * 0.5f - otherPosition.Y - other.Height * 0.5f;
					// Ground-level layers cannot shove a body already above their top surface.
					if (Mathf.Abs(centerDelta) > height + 0.25f && body.PreviousPosition.Y < otherPosition.Y + other.Height) continue;
					accepted++;
					float distance = Mathf.Sqrt(distanceSquared);
					Vector3 normal;
					if (distance > 0.0001f) normal = away / distance;
					else
					{
						uint pair = unchecked((uint)(Math.Min(slot, otherSlot) * 73856093 ^ Math.Max(slot, otherSlot) * 19349663));
						float angle = (pair & 1023u) * (Mathf.Tau / 1024f);
						normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (slot < otherSlot ? 1f : -1f);
					}
					float verticalRatio = Mathf.Abs(centerDelta) / Mathf.Max(0.1f, height);
					float contactWidth = width * Mathf.Sqrt(Mathf.Max(0f, 1f - verticalRatio * verticalRatio));
					float overlap = contactWidth - distance;
					if (overlap > 0f)
					{
						// Near-vertical contact is supported vertically, not kicked sideways by
						// the arbitrary coincident-XZ normal used for same-level overlaps.
						float horizontalWeight = verticalRatio < 0.1f ? 1f
							: distance / Mathf.Max(0.001f, Mathf.Sqrt(distanceSquared + centerDelta * centerDelta));
						push += normal * (overlap * stiffness * 0.5f * horizontalWeight);
						compression = Mathf.Max(compression, overlap / width);
					}
					float surface = otherPosition.Y + other.Height * 0.5f - body.Height * 0.5f
						+ height * Mathf.Sqrt(Mathf.Max(0f, 1f - distanceSquared / (width * width)));
					// Compare the same contact snapshot on both sides: two equally falling
					// bodies must not each mistake the other for a lower climbing support.
					bool below = otherPosition.Y < position.Y - 0.12f;
					// Only an actual lower body may catch a falling body. Never pull it up from underneath.
					if (below && surface <= body.PreviousPosition.Y + 0.001f)
						support = Mathf.Max(support, Mathf.Min(surface, body.PreviousPosition.Y));
					bool ahead = -normal.Dot(drive) > 0.2f || (distance < 0.001f && slot > otherSlot);
					if (stacking && ahead && body.DesiredVelocity.LengthSquared() > 0.01f && overlap > width * 0.06f
						&& otherPosition.Y <= body.PreviousPosition.Y + 0.12f && surface <= pileLimit)
					{
						climb = Mathf.Max(climb, surface);
						if (below) climbFromLowerBody = Mathf.Max(climbFromLowerBody, surface);
					}
				}
				if (pass == 0) body.ContactPressure = Mathf.MoveToward(body.ContactPressure, compression, dt * 3f);
				float maximumPush = Mathf.Max(0.04f, 6f * dt / iterations);
				if (push.LengthSquared() > maximumPush * maximumPush) push = push.Normalized() * maximumPush;
				Vector3 next = position + push;
				// A live body remains solid when further climbing is disabled or the height cap is lowered.
				if (support > next.Y)
				{
					next.Y = support;
					body.VerticalVelocity = 0f;
				}
				if (stacking)
				{
					// An unsupported airborne row cannot mutually climb and cancel gravity.
					// It needs ground, a surface beneath its feet, or a genuinely lower body.
					if (position.Y > worldSupport + 0.2f && support < position.Y - 0.05f)
						climb = climbFromLowerBody;
					if (body.ContactPressure > 0.05f && climb > next.Y)
					{
						next.Y = Mathf.MoveToward(next.Y, climb, climbStep);
						body.VerticalVelocity = 0f;
					}
				}
				if (next != position) body.Position = ResolveBodyWorldPosition(position, next, body.Radius, body.Height);
			}
		}
		StackedEnemyCount = 0;
		HighestPileFeet = 0f;
		for (int i = 0; i < _enemies.Length; i++)
		{
			ref Enemy body = ref _enemies[i];
			if (!body.Alive) continue;
			float lift = body.Position.Y - (SurfaceHeight?.Invoke(body.Position) ?? 0f);
			if (lift > 0.2f) StackedEnemyCount++;
			HighestPileFeet = Mathf.Max(HighestPileFeet, lift);
		}
	}
}
