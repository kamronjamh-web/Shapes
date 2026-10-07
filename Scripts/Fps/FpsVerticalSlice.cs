using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice : Node3D
{
	[Export] public string HubScenePath = "res://Scenes/UI/StartHub.tscn";

	[ExportGroup("Arena Tuning")]
	[Export] public FpsArenaLayoutSettings? ArenaLayoutSettings { get; set; } = new();
	[Export] public FpsEncounterSettings? EncounterSettings { get; set; } = new();
	[Export] public bool LiveApplyArenaLayoutChanges { get; set; } = true;
	[Export] public bool LiveApplyEncounterChanges { get; set; } = true;

	[ExportGroup("Enemy Tuning")]
	[Export] public FpsEnemyArchetypeSettings? BruiserArchetypeSettings { get; set; } = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
	[Export] public FpsEnemyArchetypeSettings? StrikerArchetypeSettings { get; set; } = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
	[Export] public bool LiveApplyEnemyArchetypeChanges { get; set; } = true;

	private readonly List<FpsSliceEnemy> _enemies = new();
	private readonly List<FpsEnemyProjectile> _projectiles = new();

	private FpsPlayerController _player = null!;
	private Node3D _arenaRoot = null!;
	private Node3D _projectileRoot = null!;

	private CanvasLayer _hudLayer = null!;
	private Label _objectiveLabel = null!;
	private Label _statusLabel = null!;
	private Label _healthLabel = null!;
	private Label _weaponLabel = null!;
	private Label _weaponDetailLabel = null!;
	private Label _noticeLabel = null!;
	private Label _crosshairLabel = null!;
	private ColorRect _overlayBackdrop = null!;
	private Label _overlayTitle = null!;
	private Label _overlayBody = null!;
	private Button _resumeButton = null!;
	private Button _restartButton = null!;
	private Button _returnButton = null!;

	private bool _pauseRequested;
	private bool _victory;
	private bool _defeat;
	private float _noticeTimer;
	private int _currentWaveNumber;
	private int _nextWaveIndex;
	private int _totalWaveCount = 1;
	private bool _waitingForNextWave;
	private float _nextWaveDelayTimer;

	private FpsArenaLayoutSettings? _observedArenaLayoutSettings;
	private FpsEncounterSettings? _observedEncounterSettings;
	private FpsEnemyArchetypeSettings? _observedBruiserArchetypeSettings;
	private FpsEnemyArchetypeSettings? _observedStrikerArchetypeSettings;
	private readonly List<FpsArenaBlockSettings> _observedArenaBlocks = new();
	private readonly List<FpsEncounterWaveSettings> _observedEncounterWaves = new();
	private readonly List<FpsEnemySpawnSettings> _observedEnemySpawns = new();
	private bool _arenaLayoutDirty;
	private bool _encounterDirty;
	private bool _enemyArchetypesDirty;

	private readonly struct FpsWaveScaling
	{
		public FpsWaveScaling(float healthMultiplier, float damageMultiplier, float moveSpeedMultiplier, float attackCadenceMultiplier)
		{
			HealthMultiplier = healthMultiplier;
			DamageMultiplier = damageMultiplier;
			MoveSpeedMultiplier = moveSpeedMultiplier;
			AttackCadenceMultiplier = attackCadenceMultiplier;
		}

		public float HealthMultiplier { get; }
		public float DamageMultiplier { get; }
		public float MoveSpeedMultiplier { get; }
		public float AttackCadenceMultiplier { get; }
	}

	public override void _Ready()
	{
		EnsureTuningResources();
		CloneGameplayTuningResources();
		SyncTuningResourceBindings();
		SyncDefenseBindings();

		BuildEnvironment();
		BuildArena();
		SpawnPlayer();
		BuildWeaponArsenal();
		EquipWeapon(0, false);
		BuildHud();
		InitializeDefenseRun();
		InitializeHorde();
		SpawnEncounter();
		RefreshCombatState();

		_arenaLayoutDirty = false;
		_encounterDirty = false;
		_enemyArchetypesDirty = false;
		_defenseMapDirty = false;

		if (IsDefenseMap)
		{
			string controls = UsesFortifications ? "Defend four gates. Ramps beside each gate lead to the wall walk. F: shop. N: start wave. `: live tuning."
				: IsHillMap ? "Defend the summit room. F: shop. N: start wave. Q/E: shapes. `: live gameplay tuning and crowd tests." : "Defend the core from the north lane. F: shop. N: start wave. Q/E: shapes. `: gameplay tuning.";
			if (_recoveredModulators.Count > 0) controls += $" {_recoveredModulators.Count} incompatible parts are available for installation on another shape at the shop.";
			ShowNotice(controls, 7f);
			return;
		}
		ShowNotice(
			_usingHubPrimaryWeapon
				? "Eliminate the room. Left Mouse fires. Right Mouse tightens spread. Your primary hub weapon is loaded. Esc opens the slice menu."
				: "Eliminate the room. Left Mouse fires. Right Mouse tightens spread. No primary hub weapon was equipped, so a fallback weapon was loaded. Esc opens the slice menu.",
			5.5f);
	}

	public override void _ExitTree()
	{
		TeardownDebugPanel();
		TeardownGameplayTuningMenu();
		UnbindTuningResourceBindings();
		UnbindDefenseSettings();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		TickGameplayTuningMetrics(dt);
		TickGameplayTuningMenu(dt);

		SyncTuningResourceBindings();
		SyncDefenseBindings();
		ApplyPendingLiveTuningChanges();
		TickWaveFlow(dt);
		TickDefenseRun(dt);
		FlushHordeDeaths();

		if (IsCombatRunning() && !_shopOpen && !_gameplayTuningVisible && _player.TryConsumeFireRequest(GetCurrentWeaponCooldownSeconds(), out Vector3 shotOrigin, out Vector3 shotDirection))
		{
			FireCurrentWeapon(shotOrigin, shotDirection);
		}

		PruneFreedActors();
		UpdateHud();

		if (_noticeTimer > 0f)
		{
			_noticeTimer -= dt;
			if (_noticeTimer <= 0f)
			{
				_noticeLabel.Visible = false;
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		// These shortcuts must work even while a text field owns GUI keyboard focus.
		if (HandleGameplayTuningHotkey(@event) ||
			(_weaponInspectionOpen && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && HandleWeaponInspectionInput(@event)))
			GetViewport().SetInputAsHandled();
	}

	private bool HandleGameplayTuningHotkey(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo) return false;
		if (key.Keycode is Key.Quoteleft or Key.Asciitilde || key.PhysicalKeycode == Key.Quoteleft)
		{
			if (_weaponInspectionOpen) CloseWeaponInspectionToPreviousMenu(false);
			if (_shopOpen) CloseDefenseShop();
			if (_debugPanelVisible) SetDebugPanelVisible(false);
			SetGameplayTuningVisible(!_gameplayTuningVisible);
			return true;
		}
		if (_gameplayTuningVisible && key.Keycode == Key.Escape)
		{
			SetGameplayTuningVisible(false);
			return true;
		}
		return false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleGameplayTuningHotkey(@event) || _gameplayTuningVisible) return;
		if (HandleWeaponInspectionInput(@event)) return;
		if (HandleDefenseInput(@event)) return;
		if (_shopOpen) return;
		if (HandleWeaponInput(@event))
		{
			return;
		}

		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
		{
			return;
		}

		if (keyEvent.Keycode == Key.Escape && !_victory && !_defeat)
		{
			if (_debugPanelVisible)
			{
				SetDebugPanelVisible(false);
				return;
			}

			_pauseRequested = !_pauseRequested;
			RefreshCombatState();
			return;
		}

		if (keyEvent.Keycode == Key.F1 && !_victory && !_defeat)
		{
			if (!_pauseRequested)
			{
				_pauseRequested = true;
			}

			SetDebugPanelVisible(!_debugPanelVisible);
			RefreshCombatState();
			return;
		}

		if (keyEvent.Keycode == Key.R)
		{
			RestartSlice();
			return;
		}

		if (keyEvent.Keycode == Key.H)
		{
			ReturnToHub();
		}
	}

	public void SpawnEnemyProjectile(Vector3 origin, Vector3 direction, float speed, float damage, Rid ignoredRid)
	{
		if (!IsCombatRunning())
		{
			return;
		}

		if (_projectileRoot == null || !GodotObject.IsInstanceValid(_projectileRoot) || _projectileRoot.IsQueuedForDeletion())
		{
			return;
		}

		Vector3 localOrigin = _projectileRoot.IsInsideTree()
			? _projectileRoot.ToLocal(origin)
			: origin;

		FpsEnemyProjectile projectile = new()
		{
			Name = $"EnemyProjectile{_projectiles.Count}",
			Position = localOrigin,
		};
		_projectileRoot.AddChild(projectile);
		projectile.Configure(_player, direction, speed, damage, ignoredRid);
		projectile.SetCombatEnabled(IsCombatRunning());
		_projectiles.Add(projectile);
	}

	private void BuildEnvironment()
	{
		Environment environment = new()
		{
			BackgroundMode = Environment.BGMode.Color,
			BackgroundColor = IsHillMap ? new Color(0.42f, 0.64f, 0.86f) : new Color(0.020f, 0.030f, 0.060f, 1f),
			AmbientLightSource = Environment.AmbientSource.Color,
			AmbientLightColor = IsHillMap ? new Color(0.82f, 0.86f, 0.93f) : new Color(0.34f, 0.40f, 0.54f, 1f),
			AmbientLightEnergy = 1.1f,
			TonemapMode = Environment.ToneMapper.Aces,
		};

		WorldEnvironment worldEnvironment = new()
		{
			Environment = environment,
		};
		AddChild(worldEnvironment);

		DirectionalLight3D keyLight = new()
		{
			Name = "DirectionalLight3D",
			RotationDegrees = new Vector3(-54f, -22f, 0f),
			LightEnergy = 2.0f,
			ShadowEnabled = true,
		};
		AddChild(keyLight);

		OmniLight3D fillLight = new()
		{
			Name = "FillLight",
			Position = new Vector3(0f, IsHillMap ? HillHeight + 4.5f : 7f, 0f),
			OmniRange = 48f,
			LightColor = IsHillMap ? new Color(1f, 0.93f, 0.80f) : new Color(0.24f, 0.55f, 0.96f, 1f),
			LightEnergy = 0.95f,
		};
		AddChild(fillLight);
	}

	private void BuildArena()
	{
		EnsureArenaRuntimeRoots();
		ClearChildren(_arenaRoot);
		if (IsDefenseMap)
		{
			// Pickups belong to the run, and must survive geometry live-apply.
			SweepDefenseLoot();
			BuildDefenseMap();
			RebuildDefenseGates();
			RefreshHillWorldExtent();
			if (IsHillMap && _player != null && GodotObject.IsInstanceValid(_player))
			{
				Vector3 grounded = ResolveDefenseGroundPosition(_player.GlobalPosition);
				if (_player.GlobalPosition.Y < grounded.Y + 0.05f)
				{
					_player.GlobalPosition = grounded + Vector3.Up * 0.05f;
					_player.Velocity = new Vector3(_player.Velocity.X, Mathf.Max(0f, _player.Velocity.Y), _player.Velocity.Z);
				}
			}
			return;
		}

		FpsArenaLayoutSettings settings = GetArenaLayoutSettings();

		float halfWidth = Mathf.Max(6f, settings.HalfWidth);
		float halfDepth = Mathf.Max(6f, settings.HalfDepth);
		float floorThickness = Mathf.Max(0.2f, settings.FloorThickness);
		float wallHeight = Mathf.Max(1f, settings.WallHeight);
		float wallThickness = Mathf.Max(0.2f, settings.WallThickness);

		float floorY = floorThickness * -0.5f;
		float wallCenterY = wallHeight * 0.5f;
		float fullWidth = halfWidth * 2f;
		float fullDepth = halfDepth * 2f;

		CreateArenaBlock(_arenaRoot, "Floor", new Vector3(fullWidth, floorThickness, fullDepth), new Vector3(0f, floorY, 0f), settings.FloorColor);
		CreateArenaBlock(_arenaRoot, "NorthWall", new Vector3(fullWidth, wallHeight, wallThickness), new Vector3(0f, wallCenterY, -halfDepth), settings.WallColor);
		CreateArenaBlock(_arenaRoot, "SouthWall", new Vector3(fullWidth, wallHeight, wallThickness), new Vector3(0f, wallCenterY, halfDepth), settings.WallColor);
		CreateArenaBlock(_arenaRoot, "WestWall", new Vector3(wallThickness, wallHeight, fullDepth), new Vector3(-halfWidth, wallCenterY, 0f), settings.WallColor);
		CreateArenaBlock(_arenaRoot, "EastWall", new Vector3(wallThickness, wallHeight, fullDepth), new Vector3(halfWidth, wallCenterY, 0f), settings.WallColor);

		for (int blockIndex = 0; blockIndex < settings.CoverBlocks.Count; blockIndex++)
		{
			FpsArenaBlockSettings block = settings.CoverBlocks[blockIndex];
			if (block == null || !block.Enabled)
			{
				continue;
			}

			string blockName = string.IsNullOrWhiteSpace(block.BlockName)
				? $"CoverBlock{blockIndex}"
				: block.BlockName;
			CreateArenaBlock(_arenaRoot, blockName, block.Size, block.Position, block.Color);
		}
	}

	private void SpawnPlayer()
	{
		_player = new FpsPlayerController
		{
			Name = "FpsPlayer",
			Position = GetArenaLayoutSettings().PlayerSpawnPosition,
		};
		if (IsHillMap) _player.Position = ResolveDefenseGroundPosition(_player.Position) + Vector3.Up * 0.1f;
		AddChild(_player);
		RefreshHillWorldExtent();

		_player.HealthChanged += OnPlayerHealthChanged;
		_player.Died += OnPlayerDied;
		EnsureFpsDebugBaseStats();
	}

	private void SpawnEncounter()
	{
		ResetWaveRuntime();
		if (IsDefenseMap && DefenseSettings!.PreparationSeconds > 0f)
		{
			_waitingForNextWave = true;
			_nextWaveDelayTimer = DefenseSettings.PreparationSeconds;
			return;
		}
		if (!TrySpawnNextWave())
		{
			_victory = true;
			RefreshCombatState();
			ShowNotice("Encounter has no enabled enemy spawns.", 4.5f);
		}
	}

	private void ResetWaveRuntime()
	{
		_manualHordeTesting = false;
		_currentWaveNumber = 0;
		_nextWaveIndex = 0;
		_waitingForNextWave = false;
		_nextWaveDelayTimer = 0f;
		_totalWaveCount = Mathf.Max(1, GetConfiguredWaveCount());
	}

	private int GetConfiguredWaveCount()
	{
		FpsEncounterSettings settings = GetEncounterSettings();
		int count = 0;
		for (int waveIndex = 0; waveIndex < settings.Waves.Count; waveIndex++)
		{
			FpsEncounterWaveSettings wave = settings.Waves[waveIndex];
			if (wave != null && wave.Enabled)
			{
				count++;
			}
		}

		return count > 0 ? count : 1;
	}

	private bool TryGetWaveSettingsForIndex(int waveIndex, out FpsEncounterWaveSettings? waveSettings)
	{
		waveSettings = null;
		FpsEncounterSettings settings = GetEncounterSettings();
		int enabledWaveIndex = 0;
		for (int index = 0; index < settings.Waves.Count; index++)
		{
			FpsEncounterWaveSettings wave = settings.Waves[index];
			if (wave == null || !wave.Enabled)
			{
				continue;
			}

			if (enabledWaveIndex == waveIndex)
			{
				waveSettings = wave;
				return true;
			}

			enabledWaveIndex++;
		}

		return false;
	}

	private bool TrySpawnNextWave()
	{
		if (_nextWaveIndex >= _totalWaveCount)
		{
			return false;
		}

		FpsEncounterSettings settings = GetEncounterSettings();
		int waveNumber = _nextWaveIndex + 1;
		int spawnedCount;
		string waveLabel;
		if (TryGetWaveSettingsForIndex(_nextWaveIndex, out FpsEncounterWaveSettings? wave))
		{
			FpsEncounterWaveSettings enabledWave = wave!;
			waveLabel = string.IsNullOrWhiteSpace(enabledWave.WaveName)
				? $"Breach Wave {waveNumber}"
				: enabledWave.WaveName;
			FpsWaveScaling scaling = ComputeWaveScaling(waveNumber, enabledWave);
			spawnedCount = SpawnWaveFromSettings(enabledWave.EnemySpawns, waveNumber, scaling);
		}
		else
		{
			waveLabel = $"Breach Wave {waveNumber}";
			FpsWaveScaling scaling = ComputeWaveScaling(waveNumber, null);
			spawnedCount = SpawnWaveFromSettings(settings.EnemySpawns, waveNumber, scaling);
			if (spawnedCount <= 0 && !IsDefenseMap)
			{
				spawnedCount = SpawnFallbackWave(waveNumber, scaling);
			}
		}

		if (spawnedCount <= 0)
		{
			_nextWaveIndex++;
			_currentWaveNumber = Mathf.Min(_nextWaveIndex, _totalWaveCount);
			return TrySpawnNextWave();
		}

		_currentWaveNumber = waveNumber;
		_nextWaveIndex++;
		_waitingForNextWave = false;
		_nextWaveDelayTimer = 0f;
		ShowNotice($"{waveLabel} engaged.", 1.8f);
		return true;
	}

	private int SpawnWaveFromSettings(Godot.Collections.Array<FpsEnemySpawnSettings> spawns, int waveNumber, in FpsWaveScaling scaling)
	{
		int spawnedCount = 0;
		for (int spawnIndex = 0; spawnIndex < spawns.Count; spawnIndex++)
		{
			FpsEnemySpawnSettings spawn = spawns[spawnIndex];
			if (spawn == null || !spawn.Enabled)
			{
				continue;
			}

			string spawnName = string.IsNullOrWhiteSpace(spawn.SpawnName)
				? $"{spawn.Role}{spawnIndex}"
				: spawn.SpawnName;
			if (IsDefenseMap)
			{
				FpsDefenseLaneSettings? lane = null;
				foreach (FpsDefenseLaneSettings candidate in DefenseSettings!.Lanes)
					if (candidate != null && candidate.Direction == spawn.Lane && candidate.Enabled) { lane = candidate; break; }
				if (lane == null) continue;
				int count = Mathf.Clamp(spawn.Count, 1, 5000);
				float spacing = Mathf.Max(1f, spawn.FormationSpacing);
				float effectiveWidth = IsHillMap ? lane.Width : Mathf.Clamp(lane.Width, 8f, Mathf.Max(14f, DefenseSettings.CourtyardHalfSize) * 2f - 6f);
				int fittingColumns = Mathf.Max(1, Mathf.FloorToInt((effectiveWidth - 2f) / spacing) + 1);
				int columns = Mathf.Min(count, Mathf.Clamp(spawn.FormationColumns, 1, fittingColumns));
				if (IsHillMap && HordeManager != null)
				{
					float radius = spawn.Role == FpsEnemyRole.Striker ? StrikerArchetypeSettings!.CollisionRadius : BruiserArchetypeSettings!.CollisionRadius;
					foreach (Vector3 position in BuildBoundedHillFormation(count, radius, spawn.Position, columns, spacing, lane.Outward))
						if (SpawnEnemy(position, spawn.Role, $"{spawnName}_W{waveNumber}_{spawnedCount}", scaling)) spawnedCount++;
					continue;
				}
				Vector3 across = new(-lane.Outward.Z, 0f, lane.Outward.X);
				for (int member = 0; member < count; member++)
				{
					int row = member / columns;
					int rowCount = Mathf.Min(columns, count - row * columns);
					Vector3 position = spawn.Position + across * ((member % columns - (rowCount - 1) * 0.5f) * spacing) + lane.Outward * (row * spacing) * (IsHillMap ? 1f : -1f);
					if (IsHillMap) position.Y = SampleHillSurfaceHeight(position);
					if (SpawnEnemy(position, spawn.Role, $"{spawnName}_W{waveNumber}_{member}", scaling)) spawnedCount++;
				}
			}
			else
			{
				if (SpawnEnemy(spawn.Position, spawn.Role, $"{spawnName}_W{waveNumber}", scaling)) spawnedCount++;
			}
		}

		return spawnedCount;
	}

	private int SpawnFallbackWave(int waveNumber, in FpsWaveScaling scaling)
	{
		SpawnEnemy(new Vector3(-18f, 0f, -18f), FpsEnemyRole.Bruiser, $"BruiserNorthWest_W{waveNumber}", scaling);
		SpawnEnemy(new Vector3(18f, 0f, -16f), FpsEnemyRole.Bruiser, $"BruiserNorthEast_W{waveNumber}", scaling);
		SpawnEnemy(new Vector3(-14f, 0f, 8f), FpsEnemyRole.Bruiser, $"BruiserWest_W{waveNumber}", scaling);
		SpawnEnemy(new Vector3(14f, 0f, 10f), FpsEnemyRole.Striker, $"StrikerEast_W{waveNumber}", scaling);
		SpawnEnemy(new Vector3(0f, 0f, -22f), FpsEnemyRole.Striker, $"StrikerNorth_W{waveNumber}", scaling);
		return 5;
	}

	private FpsWaveScaling ComputeWaveScaling(int waveNumber, FpsEncounterWaveSettings? waveSettings)
	{
		FpsEncounterSettings settings = GetEncounterSettings();
		float waveStep = Mathf.Max(0f, waveNumber - 1);

		float healthMultiplier = 1f;
		float damageMultiplier = 1f;
		float moveSpeedMultiplier = 1f;
		float attackCadenceMultiplier = 1f;

		if (settings.ScaleArchetypesPerWave)
		{
			healthMultiplier += settings.HealthMultiplierPerWave * waveStep;
			damageMultiplier += settings.DamageMultiplierPerWave * waveStep;
			moveSpeedMultiplier += settings.MoveSpeedMultiplierPerWave * waveStep;
			attackCadenceMultiplier += settings.AttackCadenceMultiplierPerWave * waveStep;
		}

		if (waveSettings != null)
		{
			healthMultiplier *= Mathf.Max(0.1f, waveSettings.HealthMultiplier);
			damageMultiplier *= Mathf.Max(0.1f, waveSettings.DamageMultiplier);
			moveSpeedMultiplier *= Mathf.Max(0.1f, waveSettings.MoveSpeedMultiplier);
			attackCadenceMultiplier *= Mathf.Max(0.1f, waveSettings.AttackCadenceMultiplier);
		}

		healthMultiplier = Mathf.Clamp(healthMultiplier, 0.1f, 20f);
		damageMultiplier = Mathf.Clamp(damageMultiplier, 0.1f, 20f);
		moveSpeedMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.1f, 20f);
		attackCadenceMultiplier = Mathf.Clamp(attackCadenceMultiplier, 0.1f, 20f);

		return new FpsWaveScaling(healthMultiplier, damageMultiplier, moveSpeedMultiplier, attackCadenceMultiplier);
	}

	private bool SpawnEnemy(Vector3 position, FpsEnemyRole role, string name, in FpsWaveScaling scaling)
	{
		if (HordeManager != null)
		{
			FpsEnemyArchetypeSettings archetype = role == FpsEnemyRole.Bruiser ? BruiserArchetypeSettings! : StrikerArchetypeSettings!;
			FpsHordeHandle handle = HordeManager.Spawn(archetype, role, position, scaling.HealthMultiplier, scaling.DamageMultiplier, scaling.MoveSpeedMultiplier, scaling.AttackCadenceMultiplier);
			if (!HordeManager.TryGet(handle, out _)) return false;
			FpsHordeCombatEnemy target = new(HordeManager, handle);
			_hordeTargets.Add(handle, target);
			_combatEnemies.Add(target);
			return true;
		}
		FpsSliceEnemy enemy = new()
		{
			Name = name,
			Role = role,
			Position = position,
		};
		enemy.SetArchetypeSettings(BruiserArchetypeSettings, StrikerArchetypeSettings);
		enemy.SetWaveScaling(scaling.HealthMultiplier, scaling.DamageMultiplier, scaling.MoveSpeedMultiplier, scaling.AttackCadenceMultiplier);
		enemy.Initialize(_player, this);
		AddChild(enemy);
		enemy.Died += OnEnemyDied;
		enemy.SetCombatEnabled(IsCombatRunning());
		_enemies.Add(enemy);
		_combatEnemies.Add(enemy);
		return true;
	}

	private void OnPlayerShotFired(Vector3 origin, Vector3 direction)
	{
		if (!IsCombatRunning())
		{
			return;
		}

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, origin + (direction * _player.ShotRange));
		query.CollideWithAreas = false;
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };

		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
		{
			ShowNotice("Shot dispersed into the hull.", 0.18f);
			return;
		}

		GodotObject? collider = result["collider"].AsGodotObject();
		if (collider is FpsSliceEnemy enemy)
		{
			DealDirectionalDamageToEnemy(enemy, _player.ShotDamage, result["position"].AsVector3(), null, false, false, direction);
			ShowNotice("Direct hit.", 0.20f);
			return;
		}

		ShowNotice("Armor impact.", 0.18f);
	}

	private void OnEnemyDied(FpsSliceEnemy enemy)
	{
		SpawnDefenseLoot(enemy);
		_enemies.Remove(enemy);
		_combatEnemies.Remove(enemy);
		if (ActiveEnemyCount != 0 || _victory || _defeat)
		{
			return;
		}

		HandleWaveCleared();
	}

	private void HandleWaveCleared()
	{
		if (_manualHordeTesting)
		{
			SweepDefenseLoot();
			ShowNotice("Test crowd cleared. Use ` to spawn another population.", 3f);
			return;
		}
		if (IsDefenseMap)
		{
			SweepDefenseLoot();
			RestockDefenseShop();
		}
		FpsEncounterSettings settings = GetEncounterSettings();
		if (!settings.AdvanceOnWaveClear || !HasPendingWaves())
		{
			_victory = true;
			RefreshCombatState();
			ShowNotice(IsDefenseMap ? $"The core survived. {DefenseSettings!.MapName} defended." : "Room clear. Slice complete.", 6f);
			return;
		}

		float interWaveDelay = Mathf.Max(0f, settings.InterWaveDelaySeconds);
		if (interWaveDelay <= 0.001f)
		{
			if (!TrySpawnNextWave())
			{
				_victory = true;
				RefreshCombatState();
				ShowNotice("Room clear. Slice complete.", 6f);
			}
			return;
		}

		_waitingForNextWave = true;
		_nextWaveDelayTimer = interWaveDelay;
		int nextWaveNumber = Mathf.Min(_currentWaveNumber + 1, _totalWaveCount);
		ShowNotice($"Wave {nextWaveNumber}/{_totalWaveCount} inbound.", Mathf.Min(interWaveDelay, 2.2f));
	}

	private void TickWaveFlow(float dt)
	{
		if (_manualHordeTesting || !_waitingForNextWave || !IsCombatRunning() || _shopOpen)
		{
			return;
		}

		_nextWaveDelayTimer = Mathf.Max(0f, _nextWaveDelayTimer - dt);
		if (_nextWaveDelayTimer > 0f)
		{
			return;
		}

		_waitingForNextWave = false;
		if (!TrySpawnNextWave())
		{
			_victory = true;
			RefreshCombatState();
			ShowNotice("Room clear. Slice complete.", 6f);
		}
	}

	private bool HasPendingWaves()
	{
		return _nextWaveIndex < _totalWaveCount;
	}

	private void OnPlayerHealthChanged(float current, float max)
	{
		CaptureFpsBloodPriceHealth(current, max);
		if (_healthLabel != null) _healthLabel.Text = $"Hull {Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
	}

	private void OnPlayerDied()
	{
		_defeat = true;
		CloseDefenseShop();
		RefreshCombatState();
		ShowNotice("Pilot down.", 6f);
	}

	private void ApplyPendingLiveTuningChanges()
	{
		if (_defenseMapDirty)
		{
			_defenseMapDirty = false;
			if (LiveApplyDefenseChanges) BuildArena();
		}
		if (_arenaLayoutDirty)
		{
			_arenaLayoutDirty = false;
			if (LiveApplyArenaLayoutChanges)
			{
				BuildArena();
				ShowLiveTuningNotice("Arena layout updated.");
			}
		}

		if (_encounterDirty)
		{
			_encounterDirty = false;
			if (LiveApplyEncounterChanges)
			{
				RebuildEncounterFromSettings();
				ShowLiveTuningNotice("Encounter waves updated.");
			}
		}

		if (_enemyArchetypesDirty)
		{
			_enemyArchetypesDirty = false;
			if (LiveApplyEnemyArchetypeChanges)
			{
				ApplyEnemyArchetypesToActiveEnemies();
				ShowLiveTuningNotice("Enemy archetypes updated.");
			}
		}
	}

	private void RebuildEncounterFromSettings()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player) || !_player.IsAlive || (IsDefenseMap && _defenseCore?.IsAlive == false))
		{
			return;
		}

		for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
		{
			FpsSliceEnemy enemy = _enemies[enemyIndex];
			if (enemy == null || !GodotObject.IsInstanceValid(enemy))
			{
				continue;
			}

			enemy.Died -= OnEnemyDied;
			enemy.QueueFree();
		}

		for (int projectileIndex = 0; projectileIndex < _projectiles.Count; projectileIndex++)
		{
			FpsEnemyProjectile projectile = _projectiles[projectileIndex];
			if (projectile == null || !GodotObject.IsInstanceValid(projectile))
			{
				continue;
			}

			projectile.QueueFree();
		}

		_enemies.Clear();
		_combatEnemies.Clear();
		_hordeTargets.Clear();
		HordeManager?.Clear();
		CorpsePool?.Clear();
		_projectiles.Clear();
		ResetWeaponEffectRuntimeState();

		CloseDefenseShop();
		_pauseRequested = false;
		_victory = false;
		_defeat = false;

		SpawnEncounter();
		RefreshCombatState();
	}

	private void ApplyEnemyArchetypesToActiveEnemies()
	{
		if (BruiserArchetypeSettings != null && StrikerArchetypeSettings != null)
			HordeManager?.ApplyArchetypes(BruiserArchetypeSettings, StrikerArchetypeSettings);
		for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
		{
			FpsSliceEnemy enemy = _enemies[enemyIndex];
			if (enemy == null || !GodotObject.IsInstanceValid(enemy))
			{
				continue;
			}

			enemy.SetArchetypeSettings(BruiserArchetypeSettings, StrikerArchetypeSettings);
		}
	}

	private void ShowLiveTuningNotice(string text)
	{
		if (_noticeLabel != null && GodotObject.IsInstanceValid(_noticeLabel))
		{
			ShowNotice(text, 1.6f);
		}
	}

	private void EnsureTuningResources()
	{
		ArenaLayoutSettings ??= new FpsArenaLayoutSettings();
		EncounterSettings ??= new FpsEncounterSettings();
		BruiserArchetypeSettings ??= FpsEnemyArchetypeSettings.CreateBruiserDefaults();
		StrikerArchetypeSettings ??= FpsEnemyArchetypeSettings.CreateStrikerDefaults();
	}

	private FpsArenaLayoutSettings GetArenaLayoutSettings()
	{
		EnsureTuningResources();
		return ArenaLayoutSettings!;
	}

	private FpsEncounterSettings GetEncounterSettings()
	{
		EnsureTuningResources();
		return EncounterSettings!;
	}

	private void SyncTuningResourceBindings()
	{
		EnsureTuningResources();

		if (!ReferenceEquals(_observedArenaLayoutSettings, ArenaLayoutSettings))
		{
			if (_observedArenaLayoutSettings != null)
			{
				_observedArenaLayoutSettings.Changed -= OnArenaLayoutSettingsChanged;
			}

			UnbindArenaBlockWatchers();

			_observedArenaLayoutSettings = ArenaLayoutSettings;
			if (_observedArenaLayoutSettings != null)
			{
				_observedArenaLayoutSettings.Changed += OnArenaLayoutSettingsChanged;
			}

			BindArenaBlockWatchers();
			_arenaLayoutDirty = true;
		}

		if (!ReferenceEquals(_observedEncounterSettings, EncounterSettings))
		{
			if (_observedEncounterSettings != null)
			{
				_observedEncounterSettings.Changed -= OnEncounterSettingsChanged;
			}

			UnbindEncounterSpawnWatchers();

			_observedEncounterSettings = EncounterSettings;
			if (_observedEncounterSettings != null)
			{
				_observedEncounterSettings.Changed += OnEncounterSettingsChanged;
			}

			BindEncounterSpawnWatchers();
			_encounterDirty = true;
		}

		if (!ReferenceEquals(_observedBruiserArchetypeSettings, BruiserArchetypeSettings))
		{
			if (_observedBruiserArchetypeSettings != null)
			{
				_observedBruiserArchetypeSettings.Changed -= OnEnemyArchetypeSettingsChanged;
			}

			_observedBruiserArchetypeSettings = BruiserArchetypeSettings;
			if (_observedBruiserArchetypeSettings != null)
			{
				_observedBruiserArchetypeSettings.Changed += OnEnemyArchetypeSettingsChanged;
			}

			_enemyArchetypesDirty = true;
		}

		if (!ReferenceEquals(_observedStrikerArchetypeSettings, StrikerArchetypeSettings))
		{
			if (_observedStrikerArchetypeSettings != null)
			{
				_observedStrikerArchetypeSettings.Changed -= OnEnemyArchetypeSettingsChanged;
			}

			_observedStrikerArchetypeSettings = StrikerArchetypeSettings;
			if (_observedStrikerArchetypeSettings != null)
			{
				_observedStrikerArchetypeSettings.Changed += OnEnemyArchetypeSettingsChanged;
			}

			_enemyArchetypesDirty = true;
		}
	}

	private void UnbindTuningResourceBindings()
	{
		if (_observedArenaLayoutSettings != null)
		{
			_observedArenaLayoutSettings.Changed -= OnArenaLayoutSettingsChanged;
		}

		if (_observedEncounterSettings != null)
		{
			_observedEncounterSettings.Changed -= OnEncounterSettingsChanged;
		}

		if (_observedBruiserArchetypeSettings != null)
		{
			_observedBruiserArchetypeSettings.Changed -= OnEnemyArchetypeSettingsChanged;
		}

		if (_observedStrikerArchetypeSettings != null)
		{
			_observedStrikerArchetypeSettings.Changed -= OnEnemyArchetypeSettingsChanged;
		}

		UnbindArenaBlockWatchers();
		UnbindEncounterSpawnWatchers();
	}

	private void BindArenaBlockWatchers()
	{
		UnbindArenaBlockWatchers();

		if (_observedArenaLayoutSettings == null)
		{
			return;
		}

		for (int blockIndex = 0; blockIndex < _observedArenaLayoutSettings.CoverBlocks.Count; blockIndex++)
		{
			FpsArenaBlockSettings block = _observedArenaLayoutSettings.CoverBlocks[blockIndex];
			if (block == null || _observedArenaBlocks.Contains(block))
			{
				continue;
			}

			block.Changed += OnArenaLayoutSettingsChanged;
			_observedArenaBlocks.Add(block);
		}
	}

	private void UnbindArenaBlockWatchers()
	{
		for (int blockIndex = 0; blockIndex < _observedArenaBlocks.Count; blockIndex++)
		{
			FpsArenaBlockSettings block = _observedArenaBlocks[blockIndex];
			if (block != null)
			{
				block.Changed -= OnArenaLayoutSettingsChanged;
			}
		}

		_observedArenaBlocks.Clear();
	}

	private void BindEncounterSpawnWatchers()
	{
		UnbindEncounterSpawnWatchers();

		if (_observedEncounterSettings == null)
		{
			return;
		}

		for (int spawnIndex = 0; spawnIndex < _observedEncounterSettings.EnemySpawns.Count; spawnIndex++)
		{
			BindEncounterSpawnWatcher(_observedEncounterSettings.EnemySpawns[spawnIndex]);
		}

		for (int waveIndex = 0; waveIndex < _observedEncounterSettings.Waves.Count; waveIndex++)
		{
			FpsEncounterWaveSettings wave = _observedEncounterSettings.Waves[waveIndex];
			if (wave == null)
			{
				continue;
			}

			if (!_observedEncounterWaves.Contains(wave))
			{
				wave.Changed += OnEncounterSettingsChanged;
				_observedEncounterWaves.Add(wave);
			}

			for (int spawnIndex = 0; spawnIndex < wave.EnemySpawns.Count; spawnIndex++)
			{
				BindEncounterSpawnWatcher(wave.EnemySpawns[spawnIndex]);
			}
		}
	}

	private void BindEncounterSpawnWatcher(FpsEnemySpawnSettings? spawn)
	{
		if (spawn == null || _observedEnemySpawns.Contains(spawn))
		{
			return;
		}

		spawn.Changed += OnEncounterSettingsChanged;
		_observedEnemySpawns.Add(spawn);
	}

	private void UnbindEncounterSpawnWatchers()
	{
		for (int waveIndex = 0; waveIndex < _observedEncounterWaves.Count; waveIndex++)
		{
			FpsEncounterWaveSettings wave = _observedEncounterWaves[waveIndex];
			if (wave != null)
			{
				wave.Changed -= OnEncounterSettingsChanged;
			}
		}

		_observedEncounterWaves.Clear();

		for (int spawnIndex = 0; spawnIndex < _observedEnemySpawns.Count; spawnIndex++)
		{
			FpsEnemySpawnSettings spawn = _observedEnemySpawns[spawnIndex];
			if (spawn != null)
			{
				spawn.Changed -= OnEncounterSettingsChanged;
			}
		}

		_observedEnemySpawns.Clear();
	}

	private void OnArenaLayoutSettingsChanged()
	{
		_arenaLayoutDirty = true;
		BindArenaBlockWatchers();
	}

	private void OnEncounterSettingsChanged()
	{
		_encounterDirty = true;
		BindEncounterSpawnWatchers();
	}

	private void OnEnemyArchetypeSettingsChanged()
	{
		_enemyArchetypesDirty = true;
	}

	private void BuildHud()
	{
		_hudLayer = new CanvasLayer
		{
			Name = "HudLayer",
		};
		AddChild(_hudLayer);

		Control hudRoot = new()
		{
			Name = "HudRoot",
			MouseFilter = Control.MouseFilterEnum.Pass,
		};
		StretchToFullRect(hudRoot);
		_hudLayer.AddChild(hudRoot);

		_objectiveLabel = CreateHudLabel("Objective: clear the breach room", 24, new Vector2(28f, 22f), 620f);
		hudRoot.AddChild(_objectiveLabel);

		_statusLabel = CreateHudLabel(string.Empty, 21, new Vector2(1280f, 22f), 240f, HorizontalAlignment.Right);
		_statusLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		_statusLabel.OffsetLeft = -590f;
		_statusLabel.OffsetRight = -28f;
		_statusLabel.OffsetTop = 22f;
		_statusLabel.OffsetBottom = 54f;
		hudRoot.AddChild(_statusLabel);

		_healthLabel = CreateHudLabel("Hull 100/100", 26, new Vector2(28f, 820f), 280f);
		AnchorCombatLabel(_healthLabel, -112f, -78f, 400f);
		hudRoot.AddChild(_healthLabel);

		_weaponLabel = CreateHudLabel("Square | Flat Strike", 19, new Vector2(28f, 854f), 620f);
		AnchorCombatLabel(_weaponLabel, -76f, -45f, 1000f);
		hudRoot.AddChild(_weaponLabel);

		_weaponDetailLabel = CreateHudLabel(string.Empty, 16, new Vector2(28f, 882f), 960f);
		_weaponDetailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_weaponDetailLabel.Size = new Vector2(960f, 52f);
		AnchorCombatLabel(_weaponDetailLabel, -40f, -8f, 1200f);
		hudRoot.AddChild(_weaponDetailLabel);

		_noticeLabel = CreateHudLabel(string.Empty, 22, new Vector2(480f, 96f), 640f, HorizontalAlignment.Center);
		_noticeLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_noticeLabel.OffsetLeft = -550f;
		_noticeLabel.OffsetRight = 550f;
		_noticeLabel.OffsetTop = 144f;
		_noticeLabel.OffsetBottom = 210f;
		_noticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_noticeLabel.Visible = false;
		hudRoot.AddChild(_noticeLabel);

		_crosshairLabel = new Label
		{
			Name = "CrosshairLabel",
			Text = "+",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		StretchToFullRect(_crosshairLabel);
		_crosshairLabel.AddThemeFontSizeOverride("font_size", 34);
		_crosshairLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 1f, 0.92f));
		hudRoot.AddChild(_crosshairLabel);
		BuildDefenseHud(hudRoot);

		_overlayBackdrop = new ColorRect
		{
			Name = "OverlayBackdrop",
			Visible = false,
			Color = new Color(0.02f, 0.03f, 0.06f, 0.82f),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		StretchToFullRect(_overlayBackdrop);
		hudRoot.AddChild(_overlayBackdrop);

		PanelContainer overlayPanel = new()
		{
			Name = "OverlayPanel",
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -365f, OffsetRight = 365f, OffsetTop = -175f, OffsetBottom = 175f,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		StyleBoxFlat panelStyle = new()
		{
			BgColor = new Color(0.06f, 0.09f, 0.14f, 0.96f),
			BorderColor = new Color(0.20f, 0.44f, 0.82f, 0.95f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 18,
			CornerRadiusTopRight = 18,
			CornerRadiusBottomLeft = 18,
			CornerRadiusBottomRight = 18,
			ContentMarginLeft = 24,
			ContentMarginTop = 22,
			ContentMarginRight = 24,
			ContentMarginBottom = 22,
		};
		overlayPanel.AddThemeStyleboxOverride("panel", panelStyle);
		_overlayBackdrop.AddChild(overlayPanel);

		VBoxContainer overlayContent = new()
		{
			Name = "OverlayContent",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		StretchToFullRect(overlayContent);
		overlayContent.AddThemeConstantOverride("separation", 14);
		overlayPanel.AddChild(overlayContent);

		_overlayTitle = new Label
		{
			Name = "OverlayTitle",
			Text = "Slice Paused",
		};
		_overlayTitle.AddThemeFontSizeOverride("font_size", 30);
		overlayContent.AddChild(_overlayTitle);

		_overlayBody = new Label
		{
			Name = "OverlayBody",
			Text = "Resume, restart, or return to the hub.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0f, 78f),
		};
		_overlayBody.AddThemeFontSizeOverride("font_size", 18);
		overlayContent.AddChild(_overlayBody);

		GridContainer buttonRow = new()
		{
			Name = "ButtonRow",
			Columns = 3,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		buttonRow.AddThemeConstantOverride("h_separation", 12);
		buttonRow.AddThemeConstantOverride("v_separation", 12);
		overlayContent.AddChild(buttonRow);

		_resumeButton = CreateOverlayButton("Resume");
		_resumeButton.Pressed += OnResumePressed;
		buttonRow.AddChild(_resumeButton);

		_inventoryButton = CreateOverlayButton("Inventory / Equipment");
		_inventoryButton.Name = "InventoryButton";
		_inventoryButton.Pressed += OpenWeaponInspection;
		buttonRow.AddChild(_inventoryButton);

		_restartButton = CreateOverlayButton("Restart");
		_restartButton.Pressed += OnRestartPressed;
		buttonRow.AddChild(_restartButton);

		_debugPanelButton = CreateOverlayButton("Debug Panel");
		_debugPanelButton.Pressed += OnDebugPanelPressed;
		buttonRow.AddChild(_debugPanelButton);

		_returnButton = CreateOverlayButton("Return To Hub");
		_returnButton.Pressed += OnReturnToHubPressed;
		buttonRow.AddChild(_returnButton);

		BuildDebugPanel(hudRoot);
		BuildGameplayTuningMenu(hudRoot);
		BuildWeaponInspection(hudRoot);
	}

	private void RefreshCombatState()
	{
		bool combatRunning = IsCombatRunning();
		_player.SetCombatEnabled(combatRunning && !_shopOpen && !_gameplayTuningVisible && !_weaponInspectionOpen);
		HordeManager?.SetCombatEnabled(combatRunning);
		CorpsePool?.Tick(0f, !_pauseRequested);

		for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
		{
			FpsSliceEnemy enemy = _enemies[enemyIndex];
			if (enemy != null && GodotObject.IsInstanceValid(enemy))
			{
				enemy.SetCombatEnabled(combatRunning);
			}
		}

		for (int projectileIndex = 0; projectileIndex < _projectiles.Count; projectileIndex++)
		{
			FpsEnemyProjectile projectile = _projectiles[projectileIndex];
			if (projectile != null && GodotObject.IsInstanceValid(projectile))
			{
				projectile.SetCombatEnabled(combatRunning);
			}
		}

		if (_overlayBackdrop == null)
		{
			return;
		}
		_crosshairLabel.Visible = combatRunning && !_shopOpen && !_gameplayTuningVisible && !_weaponInspectionOpen;
		if (_inventoryButton != null) _inventoryButton.Disabled = _victory || _defeat;

		if (_victory)
		{
			ShowOverlay(IsDefenseMap ? $"{DefenseSettings!.MapName} Defended" : "Slice Complete", IsDefenseMap ? "All waves cleared and the core survived. Restart the defense or return to the hub." : "The first-person slice cleared successfully. Restart to run it again or return to the hub.", false);
		}
		else if (_defeat)
		{
			ShowOverlay("Signal Lost", IsDefenseMap ? (_defenseCore?.IsAlive == false ? "The core was destroyed. Restart the defense or return to the hub." : "The defender fell. Restart the defense or return to the hub.") : "The breach room held. Restart the slice or return to the hub.", false);
		}
		else if (_pauseRequested)
		{
			if (_weaponInspectionOpen) _overlayBackdrop.Visible = false;
			else ShowOverlay("Slice Paused", "Inspect your equipped shape, resume combat, or return to the hub.", true);
		}
		else
		{
			_overlayBackdrop.Visible = false;
		}

		RefreshDebugPanelVisibility();
	}

	private bool IsCombatRunning()
	{
		return !_pauseRequested && !_weaponInspectionOpen && !_victory && !_defeat;
	}

	private void UpdateHud()
	{
		UpdateObjectiveHudText();
		UpdateWeaponHud();
		_crosshairLabel.Scale = _player.IsAiming ? new Vector2(0.85f, 0.85f) : Vector2.One;
	}

	private void UpdateObjectiveHudText()
	{
		if (_objectiveLabel == null || !GodotObject.IsInstanceValid(_objectiveLabel))
		{
			return;
		}

		if (_victory)
		{
			_objectiveLabel.Text = IsDefenseMap ? $"{DefenseSettings!.MapName} defended / core secured" : "Objective: room clear";
			return;
		}

		if (_defeat)
		{
			_objectiveLabel.Text = IsDefenseMap ? "Defense failed" : "Objective failed: pilot down";
			return;
		}

		if (_waitingForNextWave && HasPendingWaves())
		{
			int nextWave = Mathf.Min(_currentWaveNumber + 1, _totalWaveCount);
			_objectiveLabel.Text = $"Objective: prepare for wave {nextWave}/{_totalWaveCount} ({_nextWaveDelayTimer:0.0}s)";
			return;
		}

		int activeWave = Mathf.Max(1, _currentWaveNumber);
		_objectiveLabel.Text = IsDefenseMap ? $"{DefenseSettings!.MapName} / defend the core / wave {activeWave}/{_totalWaveCount}" : $"Objective: clear wave {activeWave}/{_totalWaveCount}";
	}

	private string GetEncounterStatusSummary()
	{
		if (_defeat)
		{
			return "Defeat";
		}

		if (_victory)
		{
			return "Complete";
		}

		if (_waitingForNextWave && HasPendingWaves())
		{
			int nextWave = Mathf.Min(_currentWaveNumber + 1, _totalWaveCount);
			return $"Next {nextWave}/{_totalWaveCount} in {_nextWaveDelayTimer:0.0}s";
		}

		int activeWave = Mathf.Max(1, _currentWaveNumber);
		return $"Wave {activeWave}/{_totalWaveCount}";
	}

	private void PruneFreedActors()
	{
		for (int projectileIndex = _projectiles.Count - 1; projectileIndex >= 0; projectileIndex--)
		{
			FpsEnemyProjectile projectile = _projectiles[projectileIndex];
			if (projectile == null || !GodotObject.IsInstanceValid(projectile) || projectile.IsQueuedForDeletion())
			{
				_projectiles.RemoveAt(projectileIndex);
			}
		}

		for (int enemyIndex = _enemies.Count - 1; enemyIndex >= 0; enemyIndex--)
		{
			FpsSliceEnemy enemy = _enemies[enemyIndex];
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || enemy.IsQueuedForDeletion())
			{
				if (enemy != null) _combatEnemies.Remove(enemy);
				_enemies.RemoveAt(enemyIndex);
			}
		}
	}

	private void EnsureArenaRuntimeRoots()
	{
		if (_arenaRoot == null || !GodotObject.IsInstanceValid(_arenaRoot))
		{
			_arenaRoot = new Node3D
			{
				Name = "ArenaGeometry",
			};
			AddChild(_arenaRoot);
		}

		if (_projectileRoot == null || !GodotObject.IsInstanceValid(_projectileRoot))
		{
			_projectileRoot = new Node3D
			{
				Name = "Projectiles",
			};
			AddChild(_projectileRoot);
		}

		if (_weaponEffectRoot == null || !GodotObject.IsInstanceValid(_weaponEffectRoot))
		{
			_weaponEffectRoot = new Node3D
			{
				Name = "PlayerWeaponEffects",
			};
			AddChild(_weaponEffectRoot);
		}
	}

	private static void ClearChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			DisableArenaCollision(child);
			child.QueueFree();
		}
	}

	private static void DisableArenaCollision(Node node)
	{
		if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; }
		foreach (Node child in node.GetChildren()) DisableArenaCollision(child);
	}

	private static void CreateArenaBlock(Node3D parent, string name, Vector3 size, Vector3 position, Color color)
	{
		StaticBody3D body = new()
		{
			Name = name,
			Position = position,
		};

		CollisionShape3D collision = new()
		{
			Shape = new BoxShape3D
			{
				Size = size,
			},
		};
		body.AddChild(collision);

		StandardMaterial3D material = new()
		{
			AlbedoColor = color,
			Metallic = 0.08f,
			Roughness = 0.74f,
		};

		MeshInstance3D mesh = new()
		{
			Mesh = new BoxMesh
			{
				Size = size,
			},
			MaterialOverride = material,
		};
		body.AddChild(mesh);

		parent.AddChild(body);
	}

	private Label CreateHudLabel(string text, int fontSize, Vector2 position, float width, HorizontalAlignment alignment = HorizontalAlignment.Left)
	{
		Label label = new()
		{
			Text = text,
			Position = position,
			Size = new Vector2(width, 32f),
			HorizontalAlignment = alignment,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.7f));
		label.AddThemeConstantOverride("shadow_offset_x", 2);
		label.AddThemeConstantOverride("shadow_offset_y", 2);
		return label;
	}

	private static void AnchorCombatLabel(Label label, float top, float bottom, float width)
	{
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
		label.OffsetLeft = 28f;
		label.OffsetRight = 28f + width;
		label.OffsetTop = top;
		label.OffsetBottom = bottom;
	}

	private static Button CreateOverlayButton(string text)
	{
		Button button = new()
		{
			Text = text,
			CustomMinimumSize = new Vector2(132f, 46f),
		};
		button.AddThemeFontSizeOverride("font_size", 18);
		return button;
	}

	private void ShowNotice(string text, float duration)
	{
		_noticeLabel.Text = text;
		_noticeLabel.Visible = true;
		_noticeTimer = duration;
	}

	private void ShowOverlay(string title, string body, bool allowResume)
	{
		_overlayBackdrop.Visible = true;
		_overlayTitle.Text = title;
		_overlayBody.Text = body;
		_resumeButton.Visible = allowResume;
	}

	private void RestartSlice()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_ = GetTree().ReloadCurrentScene();
	}

	private void ReturnToHub()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		if (string.IsNullOrWhiteSpace(HubScenePath) || !FileAccess.FileExists(HubScenePath))
		{
			GD.PushWarning($"Hub scene not found: {HubScenePath}");
			return;
		}

		Error error = GetTree().ChangeSceneToFile(HubScenePath);
		if (error != Error.Ok)
		{
			GD.PushWarning($"Failed to change to hub scene: {error}");
		}
	}

	private void OnResumePressed()
	{
		_pauseRequested = false;
		RefreshCombatState();
	}

	private void OnRestartPressed()
	{
		RestartSlice();
	}

	private void OnReturnToHubPressed()
	{
		ReturnToHub();
	}

	private static void StretchToFullRect(Control control)
	{
		control.AnchorRight = 1f;
		control.AnchorBottom = 1f;
		control.OffsetLeft = 0f;
		control.OffsetTop = 0f;
		control.OffsetRight = 0f;
		control.OffsetBottom = 0f;
	}
}
