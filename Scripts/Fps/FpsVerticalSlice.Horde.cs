using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	[ExportGroup("Crowd Simulation")]
	[Export] public FpsHordeSettings? HordeSettings { get; set; }
	public FpsHordeManager? HordeManager { get; private set; }
	public FpsEnemyCorpsePool? CorpsePool { get; private set; }
	public int ActiveEnemyCount => _combatEnemies.Count;
	private readonly List<IFpsCombatEnemy> _combatEnemies = new();
	private readonly Dictionary<FpsHordeHandle, FpsHordeCombatEnemy> _hordeTargets = new();
	private readonly List<FpsHordeEnemySnapshot> _hordeQuery = new();
	private bool UsesHorde => IsDefenseMap && HordeSettings != null;

	private void InitializeHorde()
	{
		if (!UsesHorde) return;
		RefreshHillWorldExtent();
		HordeManager = new FpsHordeManager { Name = "Horde", Settings = HordeSettings! };
		HordeManager.GpuCrowdWorldProvider = CaptureGpuCrowdWorld;
		HordeManager.BodyWorldResolver = ResolveHordeBodyWorld;
		HordeManager.SurfaceHeight = position => IsHillMap ? SampleHillSurfaceHeight(position) : 0f;
		HordeManager.SurfaceSupportHeight = SampleHillCrowdSupportHeight;
		HordeManager.MaxFootHeight = SampleHillCrowdMaxFootHeight;
		HordeManager.ConstrainBodyMovement = ConstrainHordeBodyWithGates;
		HordeManager.ConstrainMovement = (from, desired, radius) => IsHillMap ? ConstrainHillCrowdMovement(from, desired, radius) : desired;
		HordeManager.ResolveMovementTarget = ResolveHordeMovementTarget;
		HordeManager.CorePosition = () => _defenseCore?.GlobalPosition ?? Vector3.Zero;
		HordeManager.ResolveObjectiveTarget = ResolveHordeObjective;
		HordeManager.ResolveMovementTargetForSlot = ResolveHordeMovementTargetForSlot;
		HordeManager.PlayerPosition = () => _player.HeadPosition;
		HordeManager.IsPlayerAlive = () => _player.IsAlive;
		HordeManager.CanAttack = (from, to) =>
		{
			PhysicsRayQueryParameters3D ray = PhysicsRayQueryParameters3D.Create(from, to);
			ray.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid(), _defenseCore!.GetRid() };
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
			return hit.Count == 0 || hit["collider"].AsGodotObject() is FpsDefenseGate gate && gate == GateAtTarget(to);
		};
		HordeManager.EnemyDied += OnHordeEnemyDied;
		CorpsePool = new FpsEnemyCorpsePool { Name = "EnemyCorpses", Settings = HordeSettings!, PlayerBody = _player,
			WorkerWorldProvider = CaptureCorpseWorld };
		AddChild(CorpsePool);
		if (_defenseCore != null) CorpsePool.IgnoreBody(_defenseCore);
		HordeManager.EnemyDefeated += CorpsePool.SpawnCorpse;
		HordeManager.Attack += OnHordeAttack;
		AddChild(HordeManager);
	}

	private Vector3 ResolveHordeMovementTarget(Vector3 from, Vector3 target)
	{
		if (!IsHillMap) return target;
		if (UsesFortifications) return ResolveFortMovementTarget(from, target);
		// Once the crowd has climbed above the room walls, the doorway detour is unnecessary.
		if (from.Y >= HillHeight + Mathf.Max(4f, HillMapSettings!.RoomWallHeight) - 0.001f) return target;
		float roomHalf = HillRoomHalf;
		bool targetInside = Mathf.Abs(target.X) < roomHalf && Mathf.Abs(target.Z) < roomHalf;
		bool fromInside = Mathf.Abs(from.X) < roomHalf && Mathf.Abs(from.Z) < roomHalf;
		// Approach the outside of the north doorway before entering the summit room.
		if (targetInside && !fromInside)
		{
			float doorHalf = HillDoorWidth * 0.5f - 1f;
			if (from.Z >= -roomHalf || Mathf.Abs(from.X) > Mathf.Max(0.5f, doorHalf))
				return new Vector3(Mathf.Clamp(from.X * 0.2f, -doorHalf, doorHalf), SampleHillSurfaceHeight(from), -roomHalf - 3f);
		}
		return target;
	}

	private void OnHordeAttack(FpsHordeAttack attack)
	{
		if (!IsCombatRunning()) return;
		if (attack.ProjectileSpeed > 0f)
		{
			SpawnEnemyProjectile(attack.Position, (attack.TargetPosition - attack.Position).Normalized(), attack.ProjectileSpeed, attack.Damage, default);
			return;
		}
		if (attack.TargetsPlayer) _player.ApplyDamage(attack.Damage);
		else if (GateAtTarget(attack.TargetPosition) is FpsDefenseGate gate) gate.ApplyEnemyDamage(attack.Damage);
		else ApplyDefenseCoreDamage(attack.Damage);
	}

	private void OnHordeEnemyDied(FpsHordeEnemySnapshot enemy)
	{
		if (!_hordeTargets.Remove(enemy.Handle, out FpsHordeCombatEnemy? target)) return;
		SpawnDefenseLoot(enemy.Position);
		_combatEnemies.Remove(target);
		if (ActiveEnemyCount == 0 && !_victory && !_defeat) HandleWaveCleared();
	}

	private void FlushHordeDeaths() => HordeManager?.FlushDeathEvents();

	private List<IFpsCombatEnemy> QueryCombatRadius(Vector3 center, float radius)
	{
		List<IFpsCombatEnemy> candidates = new();
		if (HordeManager != null)
		{
			_hordeQuery.Clear();
			HordeManager.QueryRadius(center, radius, _hordeQuery);
			foreach (FpsHordeEnemySnapshot snapshot in _hordeQuery)
				if (_hordeTargets.TryGetValue(snapshot.Handle, out FpsHordeCombatEnemy? target)) candidates.Add(target);
		}
		foreach (FpsSliceEnemy enemy in _enemies)
			if (enemy.IsValid && enemy.IsAlive) candidates.Add(enemy);
		return candidates;
	}

	private List<IFpsCombatEnemy> QueryCombatSegment(Vector3 start, Vector3 end, float padding)
	{
		List<IFpsCombatEnemy> candidates = new();
		if (HordeManager != null)
		{
			_hordeQuery.Clear();
			HordeManager.QuerySegment(start, end, padding, _hordeQuery);
			foreach (FpsHordeEnemySnapshot snapshot in _hordeQuery)
				if (_hordeTargets.TryGetValue(snapshot.Handle, out FpsHordeCombatEnemy? target)) candidates.Add(target);
		}
		foreach (FpsSliceEnemy enemy in _enemies)
			if (enemy.IsValid && enemy.IsAlive) candidates.Add(enemy);
		return candidates;
	}

	private bool TryEnemySegmentHit(IFpsCombatEnemy enemy, Vector3 start, Vector3 end, float padding, out float distance)
	{
		float baseRadius = enemy.CollisionRadius;
		float height = enemy.CollisionHeight;
		float radius = baseRadius + Mathf.Max(0f, padding);
		Vector3 bottom = enemy.CollisionCenter - Vector3.Up * (height * 0.5f - baseRadius);
		Vector3 top = enemy.CollisionCenter + Vector3.Up * (height * 0.5f - baseRadius);
		Vector3 axisPoint = new(bottom.X, Mathf.Clamp(start.Y, bottom.Y, top.Y), bottom.Z);
		distance = float.PositiveInfinity;
		if (start.DistanceSquaredTo(axisPoint) <= radius * radius) { distance = 0f; return true; }
		Vector3 segment = end - start;
		float length = segment.Length();
		if (length <= 0.0001f) return false;
		Vector3 direction = segment / length;
		Vector3 local = start - bottom;
		float a = direction.X * direction.X + direction.Z * direction.Z;
		float b = local.X * direction.X + local.Z * direction.Z;
		float c = local.X * local.X + local.Z * local.Z - radius * radius;
		float discriminant = b * b - a * c;
		if (a > 0.000001f && discriminant >= 0f)
		{
			float entry = (-b - Mathf.Sqrt(discriminant)) / a;
			float y = start.Y + direction.Y * entry;
			if (entry >= 0f && entry <= length && y >= bottom.Y && y <= top.Y) distance = entry;
		}
		for (int cap = 0; cap < 2; cap++)
		{
			Vector3 center = cap == 0 ? bottom : top;
			Vector3 toStart = start - center;
			float projection = toStart.Dot(direction);
			float sphereDiscriminant = projection * projection - (toStart.LengthSquared() - radius * radius);
			if (sphereDiscriminant < 0f) continue;
			float entry = -projection - Mathf.Sqrt(sphereDiscriminant);
			if (entry >= 0f && entry <= length) distance = Mathf.Min(distance, entry);
		}
		return float.IsFinite(distance);
	}
}
