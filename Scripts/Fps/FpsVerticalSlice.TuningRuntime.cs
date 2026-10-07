using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private bool _manualHordeTesting;
	private readonly double[] _gameplayFrameIntervals = new double[240];
	private readonly double[] _gameplayFrameSort = new double[240];
	private int _gameplayFrameCount, _gameplayFrameCursor;
	private long _gameplayPreviousFrameTimestamp;
	private double _gameplayMetricsElapsed;

	public bool DebugPlayerInvulnerable
	{
		get => _player != null && GodotObject.IsInstanceValid(_player) && _player.DebugInvulnerable;
		set { if (_player != null && GodotObject.IsInstanceValid(_player)) _player.DebugInvulnerable = value; }
	}
	public bool DebugCoreInvulnerable
	{
		get => _defenseCore != null && GodotObject.IsInstanceValid(_defenseCore) && _defenseCore.DebugInvulnerable;
		set { if (_defenseCore != null && GodotObject.IsInstanceValid(_defenseCore)) _defenseCore.DebugInvulnerable = value; }
	}
	public FpsEnemyRole DebugSpawnRole { get; set; } = FpsEnemyRole.Bruiser;

	// ResourceLoader may return the same cached resources on the next run. Keep tuning local.
	private void CloneGameplayTuningResources()
	{
		ArenaLayoutSettings = CloneTuningResource(ArenaLayoutSettings);
		BruiserArchetypeSettings = CloneTuningResource(BruiserArchetypeSettings);
		StrikerArchetypeSettings = CloneTuningResource(StrikerArchetypeSettings);
		HordeSettings = CloneTuningResource(HordeSettings);
		HillMapSettings = CloneTuningResource(HillMapSettings);
		DefenseSettings = CloneTuningResource(DefenseSettings);
		if (DefenseSettings != null)
		{
			var lanes = new Godot.Collections.Array<FpsDefenseLaneSettings>();
			foreach (FpsDefenseLaneSettings lane in DefenseSettings.Lanes)
				if (lane != null) lanes.Add(CloneTuningResource(lane)!);
			DefenseSettings.Lanes = lanes;
		}
		EncounterSettings = CloneTuningResource(EncounterSettings);
		if (EncounterSettings != null)
		{
			EncounterSettings.EnemySpawns = CloneTuningSpawns(EncounterSettings.EnemySpawns);
			var waves = new Godot.Collections.Array<FpsEncounterWaveSettings>();
			foreach (FpsEncounterWaveSettings original in EncounterSettings.Waves)
			{
				if (original == null) continue;
				FpsEncounterWaveSettings wave = CloneTuningResource(original)!;
				wave.EnemySpawns = CloneTuningSpawns(wave.EnemySpawns);
				waves.Add(wave);
			}
			EncounterSettings.Waves = waves;
		}
		if (UsesHorde) HordeSettings!.Capacity = Math.Max(2000, HordeSettings.Capacity);
	}

	private static T? CloneTuningResource<T>(T? resource) where T : Resource
		=> resource == null ? null : (T)resource.Duplicate(true);

	private static Godot.Collections.Array<FpsEnemySpawnSettings> CloneTuningSpawns(Godot.Collections.Array<FpsEnemySpawnSettings> originals)
	{
		var result = new Godot.Collections.Array<FpsEnemySpawnSettings>();
		foreach (FpsEnemySpawnSettings spawn in originals)
			if (spawn != null) result.Add(CloneTuningResource(spawn)!);
		return result;
	}

	public void ApplyHordeArchetypeChangesLive() => ApplyEnemyArchetypesToActiveEnemies();

	public void DebugClearHorde()
	{
		_manualHordeTesting = true;
		_waitingForNextWave = false;
		_nextWaveDelayTimer = 0f;
		foreach (FpsSliceEnemy enemy in _enemies)
		{
			if (!GodotObject.IsInstanceValid(enemy)) continue;
			enemy.Died -= OnEnemyDied;
			enemy.QueueFree();
		}
		_enemies.Clear();
		HordeManager?.Clear();
		CorpsePool?.Clear();
		_hordeTargets.Clear();
		_combatEnemies.Clear();
		foreach (FpsEnemyProjectile projectile in _projectiles)
			if (GodotObject.IsInstanceValid(projectile)) projectile.QueueFree();
		_projectiles.Clear();
		ResetWeaponEffectRuntimeState();
	}

	/// <summary>Returns registrations that succeeded, not the requested count.</summary>
	public int DebugSpawnHorde(int count, bool replaceExisting = false)
	{
		if (HordeManager == null || !IsHillMap || _player == null || !_player.IsAlive || _defenseCore?.IsAlive != true) return 0;
		count = Math.Clamp(count, 1, 2000);
		_manualHordeTesting = true;
		if (replaceExisting) DebugClearHorde();
		else FlushHordeDeaths();
		_waitingForNextWave = false;
		_nextWaveDelayTimer = 0f;
		_victory = false;
		_defeat = false;
		int available = Math.Min(2000 - ActiveEnemyCount, HordeManager.Capacity - HordeManager.ActiveCount);
		int requested = Math.Min(count, Math.Max(0, available));
		if (requested == 0) { RefreshCombatState(); return 0; }
		FpsEnemyRole role = DebugSpawnRole == FpsEnemyRole.Striker ? FpsEnemyRole.Striker : FpsEnemyRole.Bruiser;
		FpsEnemyArchetypeSettings archetype = role == FpsEnemyRole.Striker ? StrikerArchetypeSettings! : BruiserArchetypeSettings!;
		float radius = Mathf.Max(0.1f, archetype.CollisionRadius);
		bool fresh = replaceExisting || ActiveEnemyCount == 0;
		int candidates = fresh ? requested : 10000;
		float spacing = fresh ? 2.8f : radius * 2f + 0.1f;
		List<Vector3> positions = UsesFortifications
			? BuildFortSpawnFormation(candidates, radius, spacing)
			: BuildBoundedHillFormation(candidates, radius, new Vector3(0f, 0f, -76f), 18, spacing, Vector3.Forward);

		// Keep an occupancy snapshot: querying after each spawn would rebuild the grid repeatedly.
		float maxRadius = radius;
		foreach (IFpsCombatEnemy enemy in _combatEnemies)
			if (enemy.IsAlive) maxRadius = Mathf.Max(maxRadius, enemy.CollisionRadius);
		float cellSize = Mathf.Max(0.2f, radius + maxRadius + 0.1f);
		var occupied = new Dictionary<(int X, int Z), List<(Vector3 Position, float Radius)>>();
		(int X, int Z) Cell(Vector3 position) => (Mathf.FloorToInt(position.X / cellSize), Mathf.FloorToInt(position.Z / cellSize));
		void AddOccupied(Vector3 position, float bodyRadius)
		{
			var cell = Cell(position);
			if (!occupied.TryGetValue(cell, out var entries)) occupied[cell] = entries = new();
			entries.Add((position, bodyRadius));
		}
		foreach (IFpsCombatEnemy enemy in _combatEnemies)
			if (enemy.IsAlive) AddOccupied(enemy.GlobalPosition, enemy.CollisionRadius);
		int spawned = 0;
		foreach (Vector3 position in positions)
		{
			var cell = Cell(position);
			bool blocked = false;
			for (int z = cell.Z - 1; z <= cell.Z + 1 && !blocked; z++)
			for (int x = cell.X - 1; x <= cell.X + 1 && !blocked; x++)
			{
				if (!occupied.TryGetValue((x, z), out var entries)) continue;
				foreach (var entry in entries)
				{
					Vector3 offset = position - entry.Position; offset.Y = 0f;
					float separation = radius + entry.Radius + 0.05f;
					if (offset.LengthSquared() < separation * separation) { blocked = true; break; }
				}
			}
			if (blocked) continue;
			int before = ActiveEnemyCount;
			SpawnEnemy(position, role, $"DebugHorde{before}", new FpsWaveScaling(1f, 1f, 1f, 1f));
			if (ActiveEnemyCount == before) break;
			spawned++;
			AddOccupied(position, radius);
			if (spawned >= requested) break;
		}
		RefreshCombatState();
		return spawned;
	}

	private List<Vector3> BuildFortSpawnFormation(int count, float radius, float spacing)
	{
		var sectors = new List<List<Vector3>>(4);
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side);
			sectors.Add(BuildBoundedHillFormation((count + 3 - side) / 4, radius, outward * (HillFootHalf + 16f), 18, spacing, outward));
		}
		// Interleave candidates so Add remains balanced when it reaches the remaining capacity.
		var positions = new List<Vector3>(count);
		for (int row = 0; row < (count + 3) / 4; row++)
			foreach (var sector in sectors) if (row < sector.Count) positions.Add(sector[row]);
		return positions;
	}

	private List<Vector3> BuildBoundedHillFormation(int count, float radius, Vector3 preferredOrigin, int preferredColumns, float preferredSpacing, Vector3 outward)
	{
		var result = new List<Vector3>();
		if (count <= 0) return result;
		count = Math.Min(count, 10000);
		radius = Mathf.Max(0.1f, radius);
		outward.Y = 0f;
		outward = outward.LengthSquared() > 0.001f ? outward.Normalized() : Vector3.Forward;
		Vector3 across = new(-outward.Z, 0f, outward.X);
		float acrossHalf = HillPlayableHalf - radius - 1f;
		float alongMin = HillFootHalf + radius + 1f;
		float alongMax = HillPlayableHalf - radius - 1f;
		if (acrossHalf <= 0f || alongMax < alongMin) return result;
		float minSpacing = radius * 2f + 0.1f;
		float desiredSpacing = Mathf.Max(minSpacing, preferredSpacing);
		int preferredWidth = Math.Clamp(preferredColumns, 1, count);
		bool preferredFits = true;
		for (int member = 0; member < count; member++)
		{
			int row = member / preferredWidth;
			int rowCount = Math.Min(preferredWidth, count - row * preferredWidth);
			Vector3 position = preferredOrigin + across * ((member % preferredWidth - (rowCount - 1) * 0.5f) * desiredSpacing) + outward * (row * desiredSpacing);
			float along = position.Dot(outward);
			if (Mathf.Abs(position.X) > acrossHalf || Mathf.Abs(position.Z) > acrossHalf || along < alongMin || along > alongMax) { preferredFits = false; break; }
			position.Y = SampleHillSurfaceHeight(position);
			result.Add(position);
		}
		if (preferredFits) return result;
		result.Clear();
		float width = acrossHalf * 2f, depth = alongMax - alongMin;
		int Columns(float spacing) => Math.Max(1, Mathf.FloorToInt(width / spacing) + 1);
		int Rows(float spacing) => Math.Max(1, Mathf.FloorToInt(depth / spacing) + 1);
		int targetCount = Math.Min(count, Columns(minSpacing) * Rows(minSpacing));
		float low = minSpacing, high = desiredSpacing;
		for (int iteration = 0; iteration < 24; iteration++)
		{
			float middle = (low + high) * 0.5f;
			if (Columns(middle) * Rows(middle) >= targetCount) low = middle;
			else high = middle;
		}
		int columns = Math.Min(targetCount, Columns(low));
		for (int member = 0; member < targetCount; member++)
		{
			int row = member / columns;
			int rowCount = Math.Min(columns, targetCount - row * columns);
			Vector3 position = across * ((member % columns - (rowCount - 1) * 0.5f) * low) + outward * (alongMin + row * low);
			position.Y = SampleHillSurfaceHeight(position);
			result.Add(position);
		}
		return result;
	}

	private void TickGameplayTuningMetrics(float dt)
	{
		long now = Stopwatch.GetTimestamp();
		if (_gameplayPreviousFrameTimestamp != 0)
		{
			double milliseconds = (now - _gameplayPreviousFrameTimestamp) * 1000.0 / Stopwatch.Frequency;
			_gameplayFrameIntervals[_gameplayFrameCursor] = milliseconds;
			_gameplayFrameCursor = (_gameplayFrameCursor + 1) % _gameplayFrameIntervals.Length;
			_gameplayFrameCount = Math.Min(_gameplayFrameCount + 1, _gameplayFrameIntervals.Length);
			_gameplayMetricsElapsed += milliseconds / 1000.0;
		}
		_gameplayPreviousFrameTimestamp = now;
		if (_gameplayMetricsElapsed < 0.5 || _gameplayFrameCount == 0) return;
		_gameplayMetricsElapsed = 0;
		double total = 0;
		for (int i = 0; i < _gameplayFrameCount; i++) { total += _gameplayFrameIntervals[i]; _gameplayFrameSort[i] = _gameplayFrameIntervals[i]; }
		Array.Sort(_gameplayFrameSort, 0, _gameplayFrameCount);
		double average = total / _gameplayFrameCount;
		double p95 = _gameplayFrameSort[Math.Clamp((int)Math.Ceiling(_gameplayFrameCount * 0.95) - 1, 0, _gameplayFrameCount - 1)];
		_gameplayTuningFrameMetricsText = $"Observed FPS {(average > 0 ? 1000.0 / average : 0):0.0} | frame avg {average:0.00} ms / p95 {p95:0.00} ms (includes FPS cap / VSync)";
	}
}
