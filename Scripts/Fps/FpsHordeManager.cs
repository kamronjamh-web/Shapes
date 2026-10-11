using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace SuperGatchaRebuild.Fps;

public readonly record struct FpsHordeHandle(int Slot, uint Generation)
{
	public static FpsHordeHandle Invalid => new(-1, 0);
}

public readonly record struct FpsHordeEnemySnapshot(
	FpsHordeHandle Handle, Vector3 Position, Vector3 HeadPosition, float Radius,
	float CurrentHealth, float MaxHealth, FpsEnemyRole Role, float CollisionHeight);

public readonly record struct FpsHordeAttack(
	FpsHordeHandle Handle, Vector3 Position, Vector3 TargetPosition,
	bool TargetsPlayer, float Damage, float ProjectileSpeed);

/// <summary>
/// Main-thread array crowd. Two MultiMeshes draw every enemy; the linked XZ grid
/// handles separation and weapon candidate queries without per-enemy nodes.
/// Handles retain a generation so an old projectile cannot hit a recycled slot.
/// </summary>
public partial class FpsHordeManager : Node3D
{
	[Export] public FpsHordeSettings Settings { get; set; } = new();
	public Func<Vector3, float>? SurfaceHeight { get; set; }
	public Func<Vector3, Vector3, float, float, float>? SurfaceSupportHeight { get; set; }
	public Func<Vector3, Vector3, float, Vector3>? ConstrainMovement { get; set; }
	public Func<Vector3, Vector3, float, float, Vector3>? ConstrainBodyMovement { get; set; }
	/// <summary>Optional combined constraint, support and ceiling resolver; desired XZ is already clamped to the manager bounds.</summary>
	public Func<Vector3, Vector3, float, float, Vector3>? BodyWorldResolver { get; set; }
	public Func<Vector3, float, float, float>? MaxFootHeight { get; set; }
	public Func<Vector3, Vector3, Vector3>? ResolveMovementTarget { get; set; }
	public Func<Vector3>? CorePosition { get; set; }
	public Func<Vector3, Vector3>? ResolveObjectiveTarget { get; set; }
	public Func<Vector3>? PlayerPosition { get; set; }
	public Func<bool>? IsPlayerAlive { get; set; }
	public Func<Vector3, Vector3, bool>? CanAttack { get; set; }
	public event Action<FpsHordeEnemySnapshot>? EnemyDied;
	public event Action<FpsHordeDeath>? EnemyDefeated;
	public event Action<FpsHordeAttack>? Attack;
	public int ActiveCount { get; private set; }
	public int Capacity => _enemies.Length;
	public long SimulationSteps { get; private set; }
	public bool CombatEnabled { get; private set; } = true;
	public int RenderedInstanceCount { get; private set; }
	public double LastSimulationMilliseconds { get; private set; }
	public int LastQueryCandidateCount { get; private set; }

	private struct Enemy
	{
		public bool Alive, TargetsPlayer;
		public uint Generation;
		public FpsEnemyRole Role;
		public Vector3 Position, PreviousPosition, Target;
		public Vector3 DesiredVelocity;
		public float VerticalVelocity, ContactPressure;
		public float Health, MaxHealth, Radius, Height, Speed, Damage, Range, DesiredRange;
		public float AttackInterval, AttackTimer, DecisionTimer, Flash, ProjectileSpeed, Yaw;
		public float HealthScale, DamageScale, MoveSpeedScale, AttackRateScale;
		public Color Color;
		public Quaternion RollOrientation, PreviousRollOrientation;
		public float RollDistance, RollPause, RollNextPause, RollUnusedSeconds;
	}
	private Enemy[] _enemies = Array.Empty<Enemy>();
	private int[] _free = Array.Empty<int>();
	private int _freeCount;
	private int[] _gridHeads = Array.Empty<int>(), _gridNext = Array.Empty<int>();
	private readonly List<int> _occupiedGridCells = new();
	private int _gridWidth;
	private float _cellSize, _extent, _maxRadius;
	private bool _gridDirty = true, _flushingDeaths;
	private readonly Queue<FpsHordeDeath> _deaths = new();
	private MultiMesh? _cubes, _pyramids;
	private MultiMeshInstance3D? _cubeNode, _pyramidNode;
	private double _accumulator;
	private uint _random;

	public override void _Ready() => EnsureInitialized();

	private void EnsureInitialized()
	{
		if (_enemies.Length > 0) return;
		int capacity = Math.Clamp(Settings.Capacity, 100, FpsHordeSettings.MaximumCapacity);
		_enemies = new Enemy[capacity];
		_aliveSlotWords = new ulong[(capacity + 63) >> 6];
		_free = new int[capacity];
		_gridNext = new int[capacity];
		_contactPositions = new Vector3[capacity];
		_deaths.EnsureCapacity(capacity);
		_freeCount = capacity;
		for (int i = 0; i < capacity; i++) _free[i] = capacity - i - 1;
		_random = unchecked((uint)Settings.Seed) | 1u;
		ReconfigureGrid();
		BuildRenderers(capacity);
	}

	public FpsHordeHandle Spawn(FpsEnemyArchetypeSettings archetype, FpsEnemyRole role,
		Vector3 position, float healthScale = 1f, float damageScale = 1f,
		float moveSpeedScale = 1f, float attackRateScale = 1f)
	{
		EnsureInitialized();
		if (_freeCount == 0) return FpsHordeHandle.Invalid;
		int slot = _free[--_freeCount];
		uint generation = _enemies[slot].Generation + 1;
		if (generation == 0) generation = 1;
		position = Ground(position);
		float maxHealth = Mathf.Max(1f, archetype.MaxHealth * healthScale);
		_enemies[slot] = new Enemy
		{
			Alive = true, Generation = generation, Position = position, PreviousPosition = position,
			Role = role, Health = maxHealth, MaxHealth = maxHealth,
			Radius = Mathf.Max(0.1f, archetype.CollisionRadius), Height = Mathf.Max(Mathf.Max(0.1f, archetype.CollisionRadius) * 2f, archetype.CollisionHeight),
			Speed = Mathf.Max(0f, archetype.MoveSpeed * moveSpeedScale),
			Damage = Mathf.Max(0f, (role == FpsEnemyRole.Striker ? archetype.ProjectileDamage : archetype.AttackDamage) * damageScale),
			Range = archetype.AttackRange, DesiredRange = archetype.DesiredRange,
			AttackInterval = Mathf.Max(0.05f, archetype.AttackCooldownSeconds / Mathf.Max(0.1f, attackRateScale)),
			AttackTimer = NextRandom() * archetype.AttackCooldownSeconds,
			DecisionTimer = -NextRandom() / Mathf.Max(1f, Settings.DecisionHz),
			Color = archetype.BodyColor, ProjectileSpeed = archetype.ProjectileSpeed,
			HealthScale = healthScale, DamageScale = damageScale, MoveSpeedScale = moveSpeedScale, AttackRateScale = attackRateScale,
			RollOrientation = Quaternion.Identity, PreviousRollOrientation = Quaternion.Identity,
			RollPause = ((unchecked((uint)slot * 747796405u + generation * 2891336453u + (uint)Settings.Seed) & 1023u) / 1023f) * Mathf.Clamp(Settings.RollingPauseSeconds, 0f, 0.3f),
		};
		_maxRadius = Mathf.Max(_maxRadius, _enemies[slot].Radius);
		MarkSlotAlive(slot);
		ActiveCount++;
		_gridDirty = true;
		MarkRenderStateDirty();
		return new FpsHordeHandle(slot, generation);
	}

	public bool TryGet(FpsHordeHandle handle, out FpsHordeEnemySnapshot snapshot)
	{
		if (IsAlive(handle)) { snapshot = Snapshot(handle.Slot); return true; }
		snapshot = default;
		return false;
	}
	public bool IsAlive(FpsHordeHandle handle) => handle.Slot >= 0 && handle.Slot < _enemies.Length
		&& _enemies[handle.Slot].Alive && _enemies[handle.Slot].Generation == handle.Generation;
	public bool TrySetPosition(FpsHordeHandle handle, Vector3 position)
	{
		if (!IsAlive(handle) || !position.IsFinite()) return false;
		ref Enemy e = ref _enemies[handle.Slot];
		e.Position = ResolveBodyWorldPosition(e.Position, position, e.Radius, e.Height);
		e.VerticalVelocity = 0f;
		e.PreviousPosition = e.Position;
		_gridDirty = true;
		MarkRenderStateDirty();
		return true;
	}
	/// <summary>Bounded, wall-constrained displacement for weapon pulls and knockback.</summary>
	public bool TryMoveBy(FpsHordeHandle handle, Vector3 displacement)
	{
		if (!IsAlive(handle) || !displacement.IsFinite()) return false;
		displacement.Y = 0f;
		// Normalize with double precision so even finite extreme inputs cannot overflow.
		double distance = Math.Sqrt((double)displacement.X * displacement.X + (double)displacement.Z * displacement.Z);
		if (distance <= 0.000001) return true;
		if (distance > 64.0) displacement *= (float)(64.0 / distance);
		int steps = Math.Clamp((int)Math.Ceiling(Math.Min(distance, 64.0) / 0.25), 1, 256);
		Vector3 step = displacement / steps;
		ref Enemy e = ref _enemies[handle.Slot];
		for (int i = 0; i < steps; i++)
		{
			Vector3 next = e.Position + step;
			float half = _extent * 0.5f - e.Radius;
			next.X = Mathf.Clamp(next.X, -half, half);
			next.Z = Mathf.Clamp(next.Z, -half, half);
			e.Position = ResolveBodyWorldPosition(e.Position, next, e.Radius, e.Height);
		}
		e.PreviousPosition = e.Position;
		_gridDirty = true;
		MarkRenderStateDirty();
		return true;
	}
	private FpsHordeEnemySnapshot Snapshot(int slot)
	{
		ref Enemy e = ref _enemies[slot];
		return new(new(slot, e.Generation), e.Position, e.Position + Vector3.Up * e.Height * 0.85f,
			e.Radius, e.Health, e.MaxHealth, e.Role, e.Height);
	}

	public bool ApplyDamage(FpsHordeHandle handle, float damage)
		=> ApplyDamageFrom(handle, damage, Vector3.Zero);

	public bool ApplyDamageFrom(FpsHordeHandle handle, float damage, Vector3 travelDirection)
	{
		if (!IsAlive(handle) || damage <= 0f || !float.IsFinite(damage)) return false;
		ref Enemy e = ref _enemies[handle.Slot];
		float remainingHealth = e.Health;
		e.Health = Mathf.Max(0f, e.Health - damage);
		e.Flash = Settings.HitFlashSeconds;
		MarkRenderStateDirty();
		if (e.Health <= 0f)
		{
			Vector3 direction = travelDirection.IsFinite() && travelDirection.LengthSquared() > 0.00001f
				? travelDirection.Normalized() : Vector3.Zero;
			_deaths.Enqueue(new FpsHordeDeath(Snapshot(handle.Slot), e.Color, GetEnemyVisualTransform(handle),
				direction, damage, Mathf.Max(0f, damage - remainingHealth)));
			e.Alive = false;
			MarkSlotDead(handle.Slot);
			_free[_freeCount++] = handle.Slot;
			ActiveCount--;
			_gridDirty = true;
		}
		return true;
	}

	/// <summary>Callbacks run after candidate iteration, allowing chained/radius damage safely.</summary>
	public void FlushDeathEvents()
	{
		if (_flushingDeaths) return;
		_flushingDeaths = true;
		try
		{
			while (_deaths.TryDequeue(out FpsHordeDeath death))
			{
				EnemyDefeated?.Invoke(death);
				EnemyDied?.Invoke(death.Enemy);
			}
		}
		finally { _flushingDeaths = false; }
	}
	public void SetCombatEnabled(bool enabled) => CombatEnabled = enabled;

	/// <summary>Retunes living slots without changing handles, wave scaling, or wounded health fractions.</summary>
	public void ApplyArchetypes(FpsEnemyArchetypeSettings bruiser, FpsEnemyArchetypeSettings striker)
	{
		_maxRadius = 0f;
		foreach (int i in EnumerateAliveSlots())
		{
			ref Enemy enemy = ref _enemies[i];
			if (!enemy.Alive) continue;
			FpsEnemyArchetypeSettings archetype = enemy.Role == FpsEnemyRole.Striker ? striker : bruiser;
			float healthFraction = Mathf.Clamp(enemy.Health / Mathf.Max(1f, enemy.MaxHealth), 0f, 1f);
			enemy.MaxHealth = Mathf.Max(1f, archetype.MaxHealth * enemy.HealthScale);
			enemy.Health = enemy.MaxHealth * healthFraction;
			enemy.Speed = Mathf.Max(0f, archetype.MoveSpeed * enemy.MoveSpeedScale);
			enemy.Damage = Mathf.Max(0f, (enemy.Role == FpsEnemyRole.Striker ? archetype.ProjectileDamage : archetype.AttackDamage) * enemy.DamageScale);
			enemy.Range = archetype.AttackRange;
			enemy.DesiredRange = archetype.DesiredRange;
			enemy.AttackInterval = Mathf.Max(0.05f, archetype.AttackCooldownSeconds / Mathf.Max(0.1f, enemy.AttackRateScale));
			enemy.AttackTimer = Mathf.Clamp(enemy.AttackTimer, 0f, enemy.AttackInterval);
			enemy.Radius = Mathf.Max(0.1f, archetype.CollisionRadius);
			enemy.Height = Mathf.Max(enemy.Radius * 2f, archetype.CollisionHeight);
			enemy.ProjectileSpeed = archetype.ProjectileSpeed;
			enemy.Color = archetype.BodyColor;
			_maxRadius = Mathf.Max(_maxRadius, enemy.Radius);
			float half = Mathf.Max(0f, _extent * 0.5f - enemy.Radius);
			Vector3 position = enemy.Position;
			position.X = Mathf.Clamp(position.X, -half, half);
			position.Z = Mathf.Clamp(position.Z, -half, half);
			enemy.Position = ResolveBodyWorldPosition(enemy.Position, position, enemy.Radius, enemy.Height);
			enemy.PreviousPosition = enemy.Position;
		}
		_gridDirty = true;
		MarkRenderStateDirty();
	}

	public void Clear()
	{
		foreach (int i in EnumerateAliveSlots()) _enemies[i].Alive = false;
		Array.Clear(_aliveSlotWords);
		_highestLiveSlot = -1;
		_freeCount = _enemies.Length;
		for (int i = 0; i < _freeCount; i++) _free[i] = _freeCount - i - 1;
		ActiveCount = 0;
		RenderedInstanceCount = 0;
		StackedEnemyCount = 0;
		HighestPileFeet = 0f;
		_deaths.Clear();
		_maxRadius = 0f;
		_gridDirty = true;
		MarkRenderStateDirty();
		_accumulator = 0;
		if (_cubes != null) _cubes.VisibleInstanceCount = 0;
		if (_pyramids != null) _pyramids.VisibleInstanceCount = 0;
	}

	/// <summary>Copies stable handles; mutations during subsequent damage cannot invalidate the list.</summary>
	public void CopyAlive(List<FpsHordeEnemySnapshot> results)
	{
		results.Clear();
		foreach (int i in EnumerateAliveSlots()) results.Add(Snapshot(i));
	}

	/// <summary>XZ broad-phase candidates; callers apply their own vertical/shape test.</summary>
	public void QueryRadius(Vector3 center, float radius, List<FpsHordeEnemySnapshot> results)
	{
		results.Clear();
		EnsureGrid();
		LastQueryCandidateCount = 0;
		float broadRadius = Mathf.Max(0f, radius) + _maxRadius;
		GetCellBounds(center.X - broadRadius, center.Z - broadRadius, center.X + broadRadius, center.Z + broadRadius,
			out int minX, out int minZ, out int maxX, out int maxZ);
		for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
		for (int i = _gridHeads[z * _gridWidth + x]; i >= 0; i = _gridNext[i])
		{
			ref Enemy e = ref _enemies[i];
			if (!e.Alive) continue;
			LastQueryCandidateCount++;
			Vector3 offset = e.Position - center; offset.Y = 0f;
			if (offset.LengthSquared() <= Mathf.Pow(radius + e.Radius, 2f)) results.Add(Snapshot(i));
		}
	}

	/// <summary>Swept segment versus upright enemy capsules. Results ordered by distance along segment.</summary>
	public void QuerySegment(Vector3 from, Vector3 to, float padding, List<FpsHordeEnemySnapshot> results)
	{
		results.Clear();
		EnsureGrid();
		LastQueryCandidateCount = 0;
		float margin = Mathf.Max(0f, padding) + _maxRadius;
		GetCellBounds(Mathf.Min(from.X, to.X) - margin, Mathf.Min(from.Z, to.Z) - margin,
			Mathf.Max(from.X, to.X) + margin, Mathf.Max(from.Z, to.Z) + margin,
			out int minX, out int minZ, out int maxX, out int maxZ);
		for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
		for (int i = _gridHeads[z * _gridWidth + x]; i >= 0; i = _gridNext[i])
		{
			ref Enemy e = ref _enemies[i];
			if (!e.Alive) continue;
			LastQueryCandidateCount++;
			Vector3 bottom = e.Position + Vector3.Up * Mathf.Min(e.Radius, e.Height * 0.5f);
			Vector3 top = e.Position + Vector3.Up * Mathf.Max(e.Height - e.Radius, e.Height * 0.5f);
			if (SegmentDistanceSquared(from, to, bottom, top) <= Mathf.Pow(e.Radius + padding, 2f)) results.Add(Snapshot(i));
		}
		// Insertion sort avoids a captured comparer allocation in every projectile query.
		Vector3 segment = to - from;
		for (int i = 1; i < results.Count; i++)
		{
			FpsHordeEnemySnapshot value = results[i];
			float along = (value.Position - from).Dot(segment);
			int j = i - 1;
			while (j >= 0 && (results[j].Position - from).Dot(segment) > along) { results[j + 1] = results[j]; j--; }
			results[j + 1] = value;
		}
	}

	public bool TryNearest(Vector3 center, float radius, out FpsHordeEnemySnapshot result)
	{
		EnsureGrid();
		LastQueryCandidateCount = 0;
		result = default;
		float best = radius * radius;
		bool found = false;
		GetCellBounds(center.X - radius, center.Z - radius, center.X + radius, center.Z + radius,
			out int minX, out int minZ, out int maxX, out int maxZ);
		for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
		for (int i = _gridHeads[z * _gridWidth + x]; i >= 0; i = _gridNext[i])
		{
			if (!_enemies[i].Alive) continue;
			LastQueryCandidateCount++;
			float distance = _enemies[i].Position.DistanceSquaredTo(center);
			if (distance <= best) { best = distance; result = Snapshot(i); found = true; }
		}
		return found;
	}

	/// <summary>Director calls this once before weapon ticks, then flushes deaths after weapon ticks.</summary>
	public void TickSimulation(double delta)
	{
		EnsureInitialized();
		long started = Stopwatch.GetTimestamp();
		float step = 1f / Mathf.Clamp(Settings.SimulationHz, 5f, 60f);
		_accumulator += Math.Min(delta, 0.25);
		int maxSteps = Math.Clamp(Settings.MaxCatchUpSteps, 1, 8), steps = 0;
		while (_accumulator >= step && steps++ < maxSteps)
		{
			if (CombatEnabled && Settings.Enabled) Simulate(step);
			_accumulator -= step;
		}
		if (_accumulator >= step) _accumulator = step;
		LastSimulationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
	}

	private void Simulate(float dt)
	{
		long phaseStarted = Stopwatch.GetTimestamp();
		EnsureGrid();
		Vector3 core = CorePosition?.Invoke() ?? Vector3.Zero;
		Vector3 player = PlayerPosition?.Invoke() ?? core;
		bool playerAlive = IsPlayerAlive?.Invoke() ?? false;
		bool deferMovementWorld = PrepareGpuCrowdMovement(core, player, playerAlive);
		if (!_gpuDrivePrepared)
		{
			SnapshotCrowdPreviousPositions();
			MoveCrowdBodiesCpu(dt, core, player, playerAlive, deferMovementWorld);
		}
		LastMovementMilliseconds = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
		phaseStarted = Stopwatch.GetTimestamp();
		SolveCrowdContactsWithBackend(dt);
		if (!LastGpuCrowdDriveResolved) UpdateRollingMotion();
		LastContactMilliseconds = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
		phaseStarted = Stopwatch.GetTimestamp();
		foreach (int i in EnumerateAliveSlots())
		{
			if (!CombatEnabled || !Settings.Enabled) break;
			ref Enemy e = ref _enemies[i];
			if (!e.Alive) continue;
			// Arithmetic has no callbacks; cooldown bodies cannot attack this step.
			if (!(e.AttackTimer <= 0f)) continue;
			float originY = e.Position.Y + e.Height * 0.85f;
			float contact = e.TargetsPlayer ? e.Range : e.Range + Settings.CoreContactRadius;
			bool ranged = e.Role == FpsEnemyRole.Striker;
			// Melee range is measured from the body, so upper layers cannot bite targets far below.
			float closestY = ranged ? originY : Math.Clamp(e.Target.Y, e.Position.Y, e.Position.Y + e.Height);
			float dx = e.Position.X - e.Target.X, dy = closestY - e.Target.Y, dz = e.Position.Z - e.Target.Z;
			// Match Vector3.DistanceTo's subtraction and left-to-right sum, retaining
			// sqrt so comparisons at the range boundary keep their original rounding.
			float attackDistance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
			bool canAttack = attackDistance <= (ranged ? Math.Max(e.DesiredRange * 1.25f, contact) : contact);
			if (canAttack)
			{
				Vector3 attackOrigin = new(e.Position.X, originY, e.Position.Z);
				if (!(CanAttack?.Invoke(attackOrigin, e.Target) ?? true))
				{
					e.AttackTimer = 1f / Mathf.Clamp(Settings.DecisionHz, 1f, 30f);
					continue;
				}
				e.AttackTimer = e.AttackInterval;
				Attack?.Invoke(new(new(i, e.Generation), attackOrigin,
					e.Target, e.TargetsPlayer, e.Damage, ranged ? e.ProjectileSpeed : 0f));
			}
		}
		_gridDirty = true;
		LastAttackMilliseconds = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
		SimulationSteps++;
		MarkRenderStateDirty();
	}

	private void SnapshotCrowdPreviousPositions()
	{
		foreach (int slot in EnumerateAliveSlots()) _enemies[slot].PreviousPosition = _enemies[slot].Position;
	}

	private void MoveCrowdBodiesCpu(float dt, Vector3 core, Vector3 player, bool playerAlive, bool deferMovementWorld)
	{
		foreach (int i in EnumerateAliveSlots())
		{
			ref Enemy e = ref _enemies[i];
			if (!e.Alive) continue;
			if (!CombatEnabled || !Settings.Enabled) break;
			e.AttackTimer -= dt;
			e.DecisionTimer -= dt;
			if (e.DecisionTimer <= 0f)
			{
				Vector3 playerOffset = player - e.Position; playerOffset.Y = 0;
				e.TargetsPlayer = playerAlive && playerOffset.LengthSquared() < Settings.PlayerAggroRadius * Settings.PlayerAggroRadius;
				e.DecisionTimer += 1f / Mathf.Clamp(Settings.DecisionHz, 1f, 30f);
			}
			if (!playerAlive) e.TargetsPlayer = false;
			e.Target = e.TargetsPlayer ? player : ResolveObjectiveTarget?.Invoke(e.Position) ?? core;
			Vector3 offset = e.Target - e.Position; offset.Y = 0;
			float distance = offset.Length();
			Vector3 movementTarget = ResolveMovementTarget?.Invoke(e.Position, e.Target) ?? e.Target;
			offset = movementTarget - e.Position; offset.Y = 0;
			float movementDistance = offset.Length();
			Vector3 direction = movementDistance > 0.001f ? offset / movementDistance : Vector3.Zero;
			float contact = e.TargetsPlayer ? e.Range : e.Range + Settings.CoreContactRadius;
			bool ranged = e.Role == FpsEnemyRole.Striker;
			float desired = ranged ? Mathf.Max(contact, e.DesiredRange) : contact * 0.8f;
			Vector3 steering = distance > desired ? direction : (ranged && distance < desired * 0.7f ? -direction : Vector3.Zero);
			// Soft body contacts provide crowd spacing. The legacy avoidance scan is only
			// needed when contacts are disabled, avoiding a second dense-neighbor traversal.
			if (!Settings.SoftCollisionEnabled) steering += Separation(i) * Mathf.Max(0f, Settings.SeparationStrength);
			if (steering.LengthSquared() > 1f) steering = steering.Normalized();
			e.DesiredVelocity = steering * e.Speed * (Settings.SoftCollisionEnabled ? Mathf.Clamp(Settings.CrowdPushStrength, 0.1f, 3f) : 1f);
			e.DesiredVelocity = ApplyRollingGait(ref e, e.DesiredVelocity, dt);
			Vector3 next = e.Position + e.DesiredVelocity * dt;
			e.VerticalVelocity = Mathf.Max(-30f, e.VerticalVelocity - Mathf.Clamp(Settings.Gravity, 1f, 40f) * dt);
			next.Y += e.VerticalVelocity * dt;
			float half = _extent * 0.5f - e.Radius;
			next.X = Mathf.Clamp(next.X, -half, half); next.Z = Mathf.Clamp(next.Z, -half, half);
			if (deferMovementWorld)
			{
				_gpuMovementProposals[i] = next;
				_gpuMovementPending[i] = true;
			}
			else
			{
				e.Position = ResolveBodyWorldPosition(e.Position, next, e.Radius, e.Height);
				if (e.Position.Y > next.Y + 0.0001f) e.VerticalVelocity = 0f;
			}
			if (direction.LengthSquared() > 0.001f) e.Yaw = Mathf.LerpAngle(e.Yaw, Mathf.Atan2(direction.X, direction.Z), Mathf.Min(1f, dt * 7f));
		}
	}

	private Vector3 Separation(int slot)
	{
		Vector3 position = _enemies[slot].PreviousPosition;
		float radius = Mathf.Max(0f, Settings.SeparationRadius);
		if (radius <= 0f) return Vector3.Zero;
		GetCellBounds(position.X - radius, position.Z - radius, position.X + radius, position.Z + radius,
			out int minX, out int minZ, out int maxX, out int maxZ);
		Vector3 push = Vector3.Zero;
		int neighbors = 0, budget = Math.Clamp(Settings.MaxSeparationNeighbors, 1, 128);
		for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
		for (int i = _gridHeads[z * _gridWidth + x]; i >= 0; i = _gridNext[i])
		{
			if (i == slot || !_enemies[i].Alive) continue;
			if (Mathf.Abs(position.Y - _enemies[i].PreviousPosition.Y) >= (_enemies[slot].Height + _enemies[i].Height) * 0.5f) continue;
			Vector3 away = position - _enemies[i].PreviousPosition; away.Y = 0;
			float d2 = away.LengthSquared();
			if (d2 >= radius * radius) continue;
			if (d2 < 0.0001f) away = (slot < i ? Vector3.Right : Vector3.Left) * 0.01f;
			float distance = Mathf.Max(0.01f, away.Length());
			push += away / distance * (1f - distance / radius);
			if (++neighbors >= budget) return push;
		}
		return push;
	}

	private Vector3 Ground(Vector3 position)
	{
		if (SurfaceHeight != null) position.Y = SurfaceHeight(position);
		return position;
	}
	private float NextRandom()
	{
		_random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
		return (_random & 0xffffff) / 16777216f;
	}
	private void ReconfigureGrid()
	{
		_cellSize = Mathf.Clamp(Settings.GridCellSize, 0.5f, 12f);
		_extent = Mathf.Clamp(Settings.MapExtent, 50f, 2048f);
		_gridWidth = Mathf.CeilToInt(_extent / _cellSize);
		_gridHeads = new int[_gridWidth * _gridWidth];
		Array.Fill(_gridHeads, -1);
		_occupiedGridCells.Clear();
		_gridDirty = true;
	}
	private void EnsureGrid()
	{
		EnsureInitialized();
		if (!Mathf.IsEqualApprox(_cellSize, Mathf.Clamp(Settings.GridCellSize, 0.5f, 12f))
			|| !Mathf.IsEqualApprox(_extent, Mathf.Clamp(Settings.MapExtent, 50f, 2048f))) ReconfigureGrid();
		if (!_gridDirty) return;
		// Clearing only occupied cells keeps sparse mile-wide maps proportional to population.
		foreach (int cell in _occupiedGridCells) _gridHeads[cell] = -1;
		_occupiedGridCells.Clear();
		foreach (int i in EnumerateAliveSlots())
		{
			if (!_enemies[i].Alive) continue;
			int cell = Cell(_enemies[i].Position.Z) * _gridWidth + Cell(_enemies[i].Position.X);
			if (_gridHeads[cell] < 0) _occupiedGridCells.Add(cell);
			_gridNext[i] = _gridHeads[cell]; _gridHeads[cell] = i;
		}
		_gridDirty = false;
	}
	private int Cell(float coordinate) => Math.Clamp(Mathf.FloorToInt((coordinate + _extent * 0.5f) / _cellSize), 0, _gridWidth - 1);
	private void GetCellBounds(float minXValue, float minZValue, float maxXValue, float maxZValue,
		out int minX, out int minZ, out int maxX, out int maxZ)
	{
		minX = Cell(minXValue); minZ = Cell(minZValue); maxX = Cell(maxXValue); maxZ = Cell(maxZValue);
	}

	private void BuildRenderers(int capacity)
	{
		ShaderMaterial material = new()
		{
			Shader = new Shader { Code = "shader_type spatial; varying float flash; void vertex(){flash=INSTANCE_CUSTOM.r;} void fragment(){ALBEDO=mix(COLOR.rgb,vec3(1.0),flash);ROUGHNESS=0.7;EMISSION=COLOR.rgb*0.08+vec3(flash*0.3);}" },
		};
		int initialCapacity = Math.Min(256, capacity);
		_cubes = CreateMultiMesh(new BoxMesh { Size = Vector3.One }, initialCapacity);
		_pyramids = CreateMultiMesh(CreatePyramid(), initialCapacity);
		_cubeRenderBuffer = new float[initialCapacity * RenderInstanceFloats];
		_pyramidRenderBuffer = new float[initialCapacity * RenderInstanceFloats];
		_cubeNode = new MultiMeshInstance3D { Name = "CrowdCubes", Multimesh = _cubes, MaterialOverride = material };
		_pyramidNode = new MultiMeshInstance3D { Name = "CrowdPyramids", Multimesh = _pyramids, MaterialOverride = material };
		AddChild(_cubeNode); AddChild(_pyramidNode);
	}
	private static MultiMesh CreateMultiMesh(Mesh mesh, int capacity) => new()
	{
		TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
		UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = capacity, VisibleInstanceCount = 0,
	};
	private static ArrayMesh CreatePyramid()
	{
		SurfaceTool tool = new(); tool.Begin(Mesh.PrimitiveType.Triangles);
		Vector3 a = new(-0.5f, -0.5f, -0.5f), b = new(0.5f, -0.5f, -0.5f);
		Vector3 c = new(0.5f, -0.5f, 0.5f), d = new(-0.5f, -0.5f, 0.5f), tip = new(0, 0.5f, 0);
		AddTriangle(tool, a, b, tip); AddTriangle(tool, b, c, tip); AddTriangle(tool, c, d, tip); AddTriangle(tool, d, a, tip);
		AddTriangle(tool, a, d, c); AddTriangle(tool, a, c, b);
		tool.GenerateNormals(); return tool.Commit();
	}
	private static void AddTriangle(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c)
	{
		tool.AddVertex(a); tool.AddVertex(b); tool.AddVertex(c);
	}

	private static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
	{
		Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
		float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), s, t;
		if (a <= 0.000001f && e <= 0.000001f) return p1.DistanceSquaredTo(p2);
		if (a <= 0.000001f) { s = 0; t = Mathf.Clamp(f / e, 0, 1); }
		else
		{
			float c = d1.Dot(r);
			if (e <= 0.000001f) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); }
			else
			{
				float b = d1.Dot(d2), denominator = a * e - b * b;
				s = denominator > 0.000001f ? Mathf.Clamp((b * f - c * e) / denominator, 0, 1) : 0;
				t = (b * s + f) / e;
				if (t < 0) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); }
				else if (t > 1) { t = 1; s = Mathf.Clamp((b - c) / a, 0, 1); }
			}
		}
		return (p1 + d1 * s).DistanceSquaredTo(p2 + d2 * t);
	}
}
