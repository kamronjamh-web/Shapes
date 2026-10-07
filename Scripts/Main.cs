using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Managers;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild;

public partial class Main : Node3D, IArenaDebugRuntime, IEnemySpawnerDebugRuntime, IBossDebugRuntime, IItemDropDebugRuntime, IArenaRunOutcomeProvider
{
	[Export] public string HubScenePath = "res://Scenes/UI/StartHub.tscn";

	private const float ArenaHalfW = ArenaLayout.HalfWidth;
	private const float ArenaHalfH = ArenaLayout.HalfHeight;
	private const int BossWaveInterval = 5;
	private const float BaseMaxHp = 10f;
	private const float BaseMoveSpeed = 19f;
	private const float BaseBulletDamage = 1f;
	private const float BaseBulletSpeed = 80f;
	private const float BaseBulletRadius = 1.2f;
	private const float BaseShotLife = 3.5f;
	private const float EnemySpatialCellSize = 18f;
	private const float EnemyEssenceDropChance = 0.002f;
	private const float EnemyModulatorDropChance = 0.001f;
	private const int MaxFloatingDamageNumbers = 96;
	private const int ChallengePillarWaveInterval = 3;
	private const float RangeDisplayScale = 24f / (BaseBulletSpeed * BaseShotLife);
	private const int BubbleCost = 25;
	private const float CubeRollTelegraphDuration = 0.60f;
	private const float CubeLandingDuration = 0.18f;
	private const float CubeComboRecoverDuration = 0.24f;
	private const float CubeChainRecoverDuration = 0.66f;
	private const float CubeTeleportAscendDuration = 0.28f;
	private const float CubeTeleportDescendDuration = 0.34f;
	private const float CubeTeleportLandingPauseDuration = 1.0f;
	private const float CubeTeleportLandingSecondShakeDelay = 0.06f;
	private const float CubeTeleportMinDistance = 22f;
	private const float CubeTeleportMaxDistance = 42f;
	private const float CubeTeleportScreenMargin = 180f;
	private const float CubeTurnShakeAmount = 1.12f;
	private const float CubeTurnShakeRadius = 56f;
	private const float CubeTeleportLandingShakeScale = 1.85f;
	private const float CubeTeleportLandingInitialShakeScale = 0.72f;
	private const float CubeTeleportLandingImpactStrength = 1.85f;
	private const float CubeArenaWaveSpeed = 150f;
	private const float CubeArenaWaveWidth = 7.5f;
	private const float CubeArenaWaveTail = 0.28f;
	private const float CubeArenaCoverChance = 0.18f;
	private const float CubeArenaCoverTeleportChance = 0.34f;
	private const int CubeArenaCoverMaxCount = 5;
	private const int CubeTeleportRuptureCoverMinCount = 1;
	private const int CubeTeleportRuptureCoverMaxCount = 3;
	private const float CubeTeleportRuptureCoverMinDistance = 10f;
	private const float CubeTeleportRuptureCoverMaxDistance = 24f;
	private const float CubeArenaCoverMinDistance = 18f;
	private const float CubeArenaCoverMaxDistance = 52f;
	private const float CubeArenaCoverSeparation = 7f;
	private const float CubeRollStopPadding = 1.2f;
	private const float CubeRollClearanceTolerance = 0.05f;
	private const string CubeFaceLandingSfxPath = "res://Resources/Audio/Boss/massive_cube_face_slam_short_game_ready.wav";
	private const float CubeFaceLandingPanStrength = 0.85f;
	private const float ScreenSpaceSfxEdgeMargin = 36f;
	private const int ArenaImpactLightCount = 4;
	private const float ArenaLightResponseSpeed = 8f;
	private const float ArenaPerimeterSweepDuration = 1.05f;

	private readonly RandomNumberGenerator _rng = new();

	private PlayerActor _player = null!;
	private LaserBeamActor _bossBeam = null!;
	private ArenaCamera _camera = null!;
	private AudioManager? _audioManager;
	private AudioStream? _cubeFaceLandingSfx;
	private SettingsManager? _settingsManager;

	private readonly List<EnemyActor> _enemies = new();
	private readonly List<BulletActor> _bullets = new();
	private readonly List<PickupActor> _pickups = new();
	private readonly Queue<FloatingDamageNumber3D> _floatingDamageNumbers = new();
	private readonly Dictionary<Vector2I, List<int>> _enemySpatialIndex = new();
	private ArenaChallengePillarActor? _challengePillar;

	private BossActor? _boss;
	private bool _bossLaserActive = false;
	private float _bossLaserTick = 0f;
	private float _enemySpatialMaxRadius = 0f;

	private Vector2 _playerVel = Vector2.Zero;
	private Vector2 _prevPlayerPos = Vector2.Zero;

	private int _waveNum = 1;
	private int _kills = 0;
	private float _waveTimer = 0f;
	private float _gameTimer = 0f;
	private float _spawnCd = 0f;
	private int _waveLeft = 8;
	private float _waveSpawnCadence = 0.5f;
	private float _enemyHpScale = 1f;
	private float _enemyDmgScale = 1f;
	private float _bossHpScale = 1f;
	private float _bossDmgScale = 1f;
	private int _level = 1;
	private float _xp = 0f;
	private float _xpToNext = 120f;
	private int _coins = 0;
	private WeaponKind _weaponKind = WeaponKind.Square;
	private RarityTier _weaponRarity = RarityTier.Common;
	private float _weaponCooldownSeconds = 1f;
	private ShapeKind _shapeKind = ShapeKind.CoreCube;
	private RarityTier _shapeRarity = RarityTier.Common;
	private int _pendingItemChoices = 0;
	private bool _pauseForItemChoice = false;
	private float _bulletRadiusScale = 1f;
	private bool _buyHeld = false;

	private CanvasLayer _hud = null!;
	private Label _hpLabel = null!;
	private ColorRect _hpBarBack = null!;
	private ColorRect _hpBarFill = null!;
	private ColorRect _hpOvershieldFill = null!;
	private Label _timeLabel = null!;
	private Label _waveLabel = null!;
	private Label _shrineLeftLabel = null!;
	private Label _killsLabel = null!;
	private Label _xpLabel = null!;
	private Label _coinsLabel = null!;
	private Label _itemSummaryLabel = null!;
	private Label _debugWeaponTimingLabel = null!;
	private Button _exitArenaButton = null!;
	private Label _spMaxHpValue = null!;
	private Label _spMoveSpeedValue = null!;
	private Label _spDodgeValue = null!;
	private Label _spShieldValue = null!;
	private Label _spCritChanceValue = null!;
	private Label _spCritDamageValue = null!;
	private Label _spRegenValue = null!;
	private Label _spLifeStealValue = null!;
	private Label _spRangeValue = null!;
	private Label _spLuckValue = null!;
	private Label _waveBanner = null!;
	private ColorRect _hurtFlashOverlay = null!;
	private ColorRect _lowHpOverlay = null!;
	private float _waveBannerTimer = 0f;
	private float _hurtFlashTimer = 0f;
	private bool _flashReductionEnabled;
	private Label _bossLabel = null!;
	private ColorRect _bossHpBack = null!;
	private ColorRect _bossHpFill = null!;
	private Label _bossPctLabel = null!;
	private Panel _levelUpPanel = null!;
	private Label _levelUpTitle = null!;
	private Button _itemBtn0 = null!;
	private Button _itemBtn1 = null!;
	private Button _itemBtn2 = null!;

	private Control _waypointRoot = null!;
	private Polygon2D _waypointArrow = null!;
	private Label _waypointText = null!;

	private CanvasLayer _debugLayer = null!;
	private readonly List<Rect2> _pillars = new();
	private readonly List<float> _pillarHeights = new();
	private readonly Dictionary<RunItemId, int> _itemStacks = new();
	private readonly Dictionary<RunItemId, float> _itemRarityPower = new();
	private readonly List<ModifierRoll> _activeModifiers = new();
	private readonly ChoiceRoll[] _currentChoiceRolls = new ChoiceRoll[3];
	private static readonly RunItemId[] ItemPool =
	{
		RunItemId.SoldierSyringe,
		RunItemId.Whetstone,
		RunItemId.MagnifyingLens,
		RunItemId.RunnerBoots,
		RunItemId.VitalCore,
		RunItemId.SplitShot,
	};
	private static readonly WeaponKind[] WeaponPool = Enum.GetValues<WeaponKind>();
	private static readonly ShapeKind[] ShapePool =
	{
		ShapeKind.CoreCube,
		ShapeKind.StrikerPyramid,
		ShapeKind.BulwarkSphere,
	};

	private ArenaDebugOverrides _arenaDebugOverrides;
	private bool _arenaDebugOverridesActive;
	private bool _debugPanelVisible;
	private readonly Dictionary<BulletActor, int> _debugRicochetLeft = new();
	private readonly Dictionary<BulletActor, int> _debugBounceLeft = new();
	private WorldEnvironment? _arenaEnvironmentNode;
	private Godot.Environment? _arenaEnvironment;
	private DirectionalLight3D? _arenaSun;
	private OmniLight3D? _arenaPlayerLight;
	private OmniLight3D? _arenaBossLight;
	private readonly List<OmniLight3D> _arenaPerimeterLights = new();
	private readonly List<OmniLight3D> _arenaImpactLights = new();
	private readonly List<ArenaGridLineVisual> _arenaGridLines = new();
	private readonly List<ArenaWallVisual> _arenaWalls = new();
	private readonly List<ArenaPillarVisual> _arenaPillarVisuals = new();
	private readonly List<CubeArenaImpactWave> _cubeArenaImpactWaves = new();
	private readonly List<ArenaTransientCue> _arenaTransientCues = new();
	private readonly List<ArenaPerimeterSweep> _arenaPerimeterSweeps = new();
	private BossActor? _arenaTrackedBoss;
	private BossPhase? _arenaTrackedBossPhase;
	private int _cubeDynamicCoverCount;
	private int _cubePendingRollCrushCount;
	private bool _cubeLandingFromTeleport;
	private bool _cubeTeleportLandingAftershockTriggered;

	private struct ChoiceRoll
	{
		public LevelChoiceKind Kind;
		public RarityTier Rarity;
		public RunItemId ItemId;
		public WeaponKind WeaponId;
		public ShapeKind ShapeId;
		public ModifierRoll[] Modifiers;
	}

	private sealed class ArenaGridLineVisual
	{
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public MeshInstance3D PulseMesh = null!;
		public StandardMaterial3D PulseMaterial = null!;
		public Vector3 BasePulsePosition;
		public bool IsVertical;
		public float AxisCoord;
	}

	private sealed class ArenaWallVisual
	{
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public bool UsesXAxis;
		public float Boundary;
	}

	private sealed class ArenaPillarVisual
	{
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public Vector3 BasePosition;
		public OmniLight3D SurgeLight = null!;
		public float Height;
		public bool DynamicCover;
	}

	private sealed class CubeArenaImpactWave
	{
		public Vector2 Origin;
		public float Age;
		public float Speed;
		public float Width;
		public float Duration;
		public float Strength;
		public Color Tint;
	}

	private sealed class ArenaTransientCue
	{
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public OmniLight3D Light = null!;
		public Vector3 BasePosition;
		public Color Color;
		public float Age;
		public float Duration;
		public float MaxLightEnergy;
		public float MaxLightRange;
		public bool VerticalBeam;
	}

	private sealed class ArenaPerimeterSweep
	{
		public Color Color;
		public float Age;
		public float Duration;
		public float Strength;
	}

	public override void _Ready()
	{
		_rng.Randomize();

		RenderingServer.SetDefaultClearColor(new Color(0.03f, 0.04f, 0.12f));
		BuildPillars();

		BuildArenaBackdrop();
		ResolveSettingsManager();
		ResolveAudioManager();
		LoadArenaAudio();

		_player = new PlayerActor();
		AddChild(_player);
		_player.Position = new Vector2(ArenaLayout.PlayerStartX, ArenaLayout.PlayerStartZ);
		_player.MaxHp = BaseMaxHp;
		_player.Hp = BaseMaxHp;
		_player.MoveSpeed = BaseMoveSpeed;
		_player.BulletDamage = BaseBulletDamage;
		_player.BulletSpeed = BaseBulletSpeed;
		_player.FireRate = 1f / Mathf.Max(0.05f, _weaponCooldownSeconds);
		if (IsInstanceValid(_camera))
		{
			_camera.Target = _player;
		}

		_bossBeam = new LaserBeamActor();
		AddChild(_bossBeam);
		_bossBeam.Disable();

		BuildHud();
		BuildDebugPanel();
		ResetProgressionState();
		RebuildWaveBalance(_waveNum);
		RefreshChallengePillarForWave();

		_prevPlayerPos = _player.Position;
		UpdateHud();
	}

	public override void _ExitTree()
	{
		if (_settingsManager != null)
		{
			_settingsManager.GraphicsSettingsChanged -= OnGraphicsSettingsChanged;
		}
	}

	private void ResolveSettingsManager()
	{
		_settingsManager = SettingsManager.Resolve(this);
		if (_settingsManager == null)
		{
			return;
		}

		_settingsManager.GraphicsSettingsChanged -= OnGraphicsSettingsChanged;
		_settingsManager.GraphicsSettingsChanged += OnGraphicsSettingsChanged;
		ApplyGraphicsSettings(_settingsManager.GraphicsSettings);
	}

	private void ResolveAudioManager()
	{
		_audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
	}

	private void LoadArenaAudio()
	{
		_cubeFaceLandingSfx = ResourceLoader.Load<AudioStream>(CubeFaceLandingSfxPath);
		if (_cubeFaceLandingSfx == null)
		{
			GD.PushWarning($"Failed to load cube face landing SFX: {CubeFaceLandingSfxPath}");
		}
	}

	private void PlayWorldScreenSpaceSfx(
		AudioStream? stream,
		Vector3 worldPosition,
		float volumeDb = 0f,
		float pitchScale = 1f,
		float panningStrength = 1f)
	{
		if (_audioManager == null)
		{
			return;
		}

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		if (!IsInstanceValid(_camera) || viewportSize.X <= 0f || viewportSize.Y <= 0f)
		{
			_audioManager.PlayOneShot(stream, volumeDb, pitchScale);
			return;
		}

		Vector2 screenPosition = ProjectWorldPositionToAudioScreen(worldPosition, viewportSize, ScreenSpaceSfxEdgeMargin);
		_audioManager.PlayScreenSpaceOneShot(stream, screenPosition, viewportSize, volumeDb, pitchScale, panningStrength);
	}

	private Vector2 ProjectWorldPositionToAudioScreen(Vector3 worldPosition, Vector2 viewportSize, float margin)
	{
		if (!IsInstanceValid(_camera))
		{
			return viewportSize * 0.5f;
		}

		bool behind = _camera.IsPositionBehind(worldPosition);
		Vector2 screenPosition = _camera.UnprojectPosition(worldPosition);
		Rect2 screenBounds = new(
			new Vector2(margin, margin),
			new Vector2(
				Mathf.Max(viewportSize.X - margin * 2f, 1f),
				Mathf.Max(viewportSize.Y - margin * 2f, 1f)));
		if (!behind && screenBounds.HasPoint(screenPosition))
		{
			return screenPosition;
		}

		Vector2 center = viewportSize * 0.5f;
		Vector2 delta = screenPosition - center;
		if (behind)
		{
			delta = -delta;
		}

		if (delta.LengthSquared() < 0.001f)
		{
			delta = Vector2.Right;
		}

		float t = float.PositiveInfinity;
		if (Mathf.Abs(delta.X) > 0.001f)
		{
			float tx = (delta.X > 0f ? (viewportSize.X - margin - center.X) : (margin - center.X)) / delta.X;
			t = Mathf.Min(t, tx);
		}

		if (Mathf.Abs(delta.Y) > 0.001f)
		{
			float ty = (delta.Y > 0f ? (viewportSize.Y - margin - center.Y) : (margin - center.Y)) / delta.Y;
			t = Mathf.Min(t, ty);
		}

		if (float.IsInfinity(t) || t <= 0f)
		{
			return new Vector2(
				Mathf.Clamp(screenPosition.X, margin, viewportSize.X - margin),
				Mathf.Clamp(screenPosition.Y, margin, viewportSize.Y - margin));
		}

		Vector2 clamped = center + delta * t;
		return new Vector2(
			Mathf.Clamp(clamped.X, margin, viewportSize.X - margin),
			Mathf.Clamp(clamped.Y, margin, viewportSize.Y - margin));
	}

	private void OnGraphicsSettingsChanged(GraphicsSettingsData settings)
	{
		ApplyGraphicsSettings(settings);
	}

	private void ApplyGraphicsSettings(GraphicsSettingsData settings)
	{
		if (_arenaEnvironment != null)
		{
			_arenaEnvironment.GlowEnabled = settings.GlowEnabled;
		}

		if (IsInstanceValid(_arenaSun))
		{
			_arenaSun.ShadowEnabled = settings.ShadowsEnabled;
		}

		_flashReductionEnabled = settings.FlashReductionEnabled;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		ShapeVisualFactory.PumpShapeVisualSettingsHotReload();
		HandleArenaPauseToggle();
		HandleProgressionHotkeys();

		if (!IsArenaGameplayPaused())
		{
			_gameTimer += dt;
			TickEquipmentModifierRuntime(dt);
			TickPlayer(dt);
			TickMerchantEncounter();
			TickChallengePillar();
			TickSpawns(dt);
			TickEnemies(dt);
			TickBullets(dt);
			TickLegacyWeaponEffects(dt);
			TickPickups(dt);
			TickBoss(dt);
			TickCollisions(dt);
			CleanupDead();
			ApplyArenaDebugRegen(dt);

			_playerVel = (_player.Position - _prevPlayerPos) / Mathf.Max(0.0001f, dt);
			_prevPlayerPos = _player.Position;
		}
		else
		{
			_merchantInteractHeld = false;
			_playerVel = Vector2.Zero;
		}

		if (!_arenaPauseMenuVisible)
		{
			UpdateBossWaypoint();
			TickArenaWeaponVisuals(dt);
			UpdateArenaVisualEffects(dt);
		}
		UpdateHud();
	}

	private void TickPlayer(float dt)
	{
		Vector2 input = Vector2.Zero;
		if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A)) input.X -= 1f;
		if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D)) input.X += 1f;
		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W)) input.Y -= 1f;
		if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S)) input.Y += 1f;

		if (input.LengthSquared() > 0f)
		{
			input = input.Normalized();
		}

		_player.Position += input * _player.MoveSpeed * dt;
		_player.Position = ClampResolve(_player.Position, _player.Radius);

		if (TryFindCurrentWeaponTarget(out Vector2 targetPosition) && _player.ConsumeShootWindow())
		{
			FireLegacyWeaponVolley(targetPosition);
			TryFireSyncedWingSupportVolleys(targetPosition);
			TryFirePrismaticSurge(targetPosition);
		}

		TickWingSupportWeapons(dt);
	}

	private void TickSpawns(float dt)
	{
		if (_merchantEncounterActive)
		{
			return;
		}

		_waveTimer += dt;
		_spawnCd -= dt;

		if (_waveLeft > 0 && _spawnCd <= 0f)
		{
			_spawnCd = _waveSpawnCadence;
			SpawnEnemyForWave(_waveNum);
			_waveLeft--;
		}

		if (_waveLeft <= 0 && _enemies.Count == 0 && _boss == null)
		{
			if (_challengePillar != null && IsInstanceValid(_challengePillar))
			{
				if (_challengePillar.IsActivated)
				{
					CompleteChallengePillar();
				}
				else
				{
					ClearChallengePillar();
				}
			}

			_waveNum++;
			RebuildWaveBalance(_waveNum);
			ShowWaveBanner(_waveNum);
			_waveTimer = 0f;

			if (_waveNum % BossWaveInterval == 0)
			{
				SpawnBossForWave(_waveNum);
			}
			else
			{
				RefreshChallengePillarForWave();
			}
		}
	}

	private void TickChallengePillar()
	{
		if (_challengePillar == null || !IsInstanceValid(_challengePillar) || _challengePillar.IsActivated)
		{
			return;
		}

		float activationRadius = _challengePillar.Radius + _player.Radius;
		if ((_challengePillar.Position - _player.Position).LengthSquared() > activationRadius * activationRadius)
		{
			return;
		}

		ActivateChallengePillar();
	}

	private void TickEnemies(float dt)
	{
		foreach (EnemyActor e in _enemies)
		{
			if (!e.IsAlive)
			{
				continue;
			}

			if (TickEquipmentEnemyOverride(e, dt))
			{
				continue;
			}

			e.TickAI(
				dt,
				_player.Position,
				_playerVel,
				(pos, vel, dmg, team, color, radius) => SpawnBullet(pos, vel, dmg, team, color, radius, 4f),
				ClampResolve,
				IsBlockedByPillar);

			if (e.ContactDamageEnabled)
			{
				float rr = e.Radius + _player.Radius;
				if ((_player.Position - e.Position).LengthSquared() < rr * rr)
				{
					ApplyPlayerDamage(e.ContactDps * dt);
				}
			}
		}
	}

	private void TickBullets(float dt)
	{
		RebuildEnemySpatialIndex();
		_packHunterMarkTimeLeft = Mathf.Max(0f, _packHunterMarkTimeLeft - dt);

		for (int i = _bullets.Count - 1; i >= 0; i--)
		{
			BulletActor b = _bullets[i];
			if (!IsInstanceValid(b) || !b.Active)
			{
				CleanupBulletDebugState(b);
				_bullets.RemoveAt(i);
				continue;
			}

			if (Mathf.Abs(b.Position.X) > ArenaHalfW + 80f || Mathf.Abs(b.Position.Y) > ArenaHalfH + 80f)
			{
				if (TryBounceBulletAtBounds(b))
				{
					continue;
				}

				b.Deactivate();
				CleanupBulletDebugState(b);
				_bullets.RemoveAt(i);
				continue;
			}

			if (IsPointBlockedByPillar(b.Position, b.Radius + 1f))
			{
				if (TryBounceBulletFromPillar(b))
				{
					continue;
				}

				b.Deactivate();
				CleanupBulletDebugState(b);
				_bullets.RemoveAt(i);
				continue;
			}

			if (b.Team == TeamId.Monster)
			{
				if (_player.BubbleActive)
				{
					float bubbleR = _player.BubbleRadius;
					if ((_player.Position - b.Position).LengthSquared() <= bubbleR * bubbleR)
					{
						_player.BubbleHp -= b.Damage;
						if (_player.BubbleHp <= 0f)
						{
							_player.BubbleHp = 0f;
							_player.BubbleActive = false;
						}

						b.Deactivate();
						CleanupBulletDebugState(b);
						_bullets.RemoveAt(i);
						continue;
					}
				}

				float hitR = _player.Radius + b.Radius;
				if ((_player.Position - b.Position).LengthSquared() <= hitR * hitR)
				{
					ApplyPlayerDamage(b.Damage);
					b.Deactivate();
					CleanupBulletDebugState(b);
					_bullets.RemoveAt(i);
					continue;
				}
			}
			else if (b.Team == TeamId.Player)
			{
				bool hit = false;
				Vector2 hitPos = b.Position;

				if (_boss != null && _boss.IsAlive)
				{
					float rr = _boss.Radius + b.Radius;
					if ((_boss.Position - b.Position).LengthSquared() <= rr * rr)
					{
						DealDamageToBoss(b.Damage, b.IsCriticalHit, true, b.SourceItem);
						hitPos = _boss.Position;
						hit = true;
					}
				}

				if (!hit)
				{
					int enemyIndex = FindBulletHitEnemyIndex(b.Position, b.Radius);
					if (enemyIndex >= 0)
					{
						EnemyActor e = _enemies[enemyIndex];
						DealDamageToEnemy(e, b.Damage, b.Position, true, b.IsCriticalHit, true, b.SourceItem);
						hitPos = e.Position;
						hit = true;
					}
				}

				if (hit)
				{
					if (_legacyHomingBullets.TryGetValue(b, out HomingBulletState? seekerState) && seekerState.PackHunterEnabled)
					{
						_packHunterMarkPosition = hitPos;
						_packHunterMarkTimeLeft = seekerState.PackHunterMarkDuration;
					}

					if (!TryRicochetBullet(b, hitPos) && !TryConsumeLegacyBulletHit(b, hitPos))
					{
						b.Deactivate();
						CleanupBulletDebugState(b);
						_bullets.RemoveAt(i);
					}
				}
			}
		}
	}

	private void RebuildEnemySpatialIndex()
	{
		foreach (List<int> cellEnemyIndices in _enemySpatialIndex.Values)
		{
			cellEnemyIndices.Clear();
		}

		_enemySpatialMaxRadius = 0f;

		for (int i = 0; i < _enemies.Count; i++)
		{
			EnemyActor enemy = _enemies[i];
			if (!enemy.IsAlive)
			{
				continue;
			}

			Vector2I cell = GetEnemySpatialCell(enemy.Position);
			if (!_enemySpatialIndex.TryGetValue(cell, out List<int>? cellEnemyIndices))
			{
				cellEnemyIndices = new List<int>(4);
				_enemySpatialIndex[cell] = cellEnemyIndices;
			}

			cellEnemyIndices.Add(i);
			_enemySpatialMaxRadius = Mathf.Max(_enemySpatialMaxRadius, enemy.Radius);
		}
	}

	private int FindBulletHitEnemyIndex(Vector2 bulletPos, float bulletRadius)
	{
		if (_enemySpatialIndex.Count == 0)
		{
			return -1;
		}

		Vector2I centerCell = GetEnemySpatialCell(bulletPos);
		int cellRange = Mathf.Max(1, Mathf.CeilToInt((bulletRadius + _enemySpatialMaxRadius) / EnemySpatialCellSize));
		int bestEnemyIndex = -1;

		for (int y = centerCell.Y - cellRange; y <= centerCell.Y + cellRange; y++)
		{
			for (int x = centerCell.X - cellRange; x <= centerCell.X + cellRange; x++)
			{
				if (!_enemySpatialIndex.TryGetValue(new Vector2I(x, y), out List<int>? cellEnemyIndices))
				{
					continue;
				}

				for (int i = 0; i < cellEnemyIndices.Count; i++)
				{
					int enemyIndex = cellEnemyIndices[i];
					if (bestEnemyIndex >= 0 && enemyIndex >= bestEnemyIndex)
					{
						continue;
					}

					EnemyActor enemy = _enemies[enemyIndex];
					if (!enemy.IsAlive)
					{
						continue;
					}

					float rr = enemy.Radius + bulletRadius;
					if ((enemy.Position - bulletPos).LengthSquared() <= rr * rr)
					{
						bestEnemyIndex = enemyIndex;
					}
				}
			}
		}

		return bestEnemyIndex;
	}

	private static Vector2I GetEnemySpatialCell(Vector2 position)
	{
		return new Vector2I(
			Mathf.FloorToInt(position.X / EnemySpatialCellSize),
			Mathf.FloorToInt(position.Y / EnemySpatialCellSize));
	}

	private void TickCollisions(float dt)
	{
		if (_boss == null || !_boss.IsAlive || _boss.Kind != BossKind.Cube)
		{
			return;
		}

		if (_boss.Phase == BossPhase.TeleportAscend || _boss.Phase == BossPhase.TeleportDescend)
		{
			return;
		}

		float rr = _boss.Radius + _player.Radius;
		if ((_player.Position - _boss.Position).LengthSquared() > rr * rr)
		{
			return;
		}

		float contactDps = _boss.ContactDps * _bossDmgScale;
		if (_boss.Phase == BossPhase.Rolling)
		{
			contactDps *= 1.4f;
		}

		ApplyPlayerDamage(contactDps * dt);
	}

	private void CleanupDead()
	{
		for (int i = _enemies.Count - 1; i >= 0; i--)
		{
			EnemyActor e = _enemies[i];
			if (!IsInstanceValid(e) || !e.IsAlive)
			{
				if (IsInstanceValid(e))
				{
					GrantEnemyDropRewards(e);
					e.QueueFree();
				}

				_enemies.RemoveAt(i);
				_kills++;
			}
		}
	}

	private void SpawnEnemyForWave(int wave)
	{
		EnemyKind kind = RollEnemyKindForWave(wave);

		Vector2 pos = GetEnemySpawnPosition();

		EnemyActor e = new();
		AddChild(e);
		e.Configure(kind, _enemyHpScale, _enemyDmgScale, pos, _player.Position);
		ApplyEquipmentEnemyStatModifiers(e);
		_enemies.Add(e);
		TriggerArenaEnemySpawnCue(pos, kind);
	}

	private EnemyKind RollEnemyKindForWave(int wave)
	{
		if (wave <= 2)
		{
			return EnemyKind.Grunt;
		}

		float gruntWeight = Mathf.Max(0.55f, 1.45f - wave * 0.06f);
		float bulwarkWeight = wave >= 3 ? Mathf.Min(0.55f, 0.22f + (wave - 3) * 0.05f) : 0f;
		float chargerWeight = wave >= 4 ? Mathf.Min(0.44f, 0.16f + (wave - 4) * 0.045f) : 0f;
		float mortarWeight = wave >= 5 ? Mathf.Min(0.36f, 0.10f + (wave - 5) * 0.04f) : 0f;
		float interceptorWeight = wave >= 6 ? Mathf.Min(0.42f, 0.14f + (wave - 6) * 0.045f) : 0f;

		float totalWeight = gruntWeight + bulwarkWeight + chargerWeight + mortarWeight + interceptorWeight;
		float roll = _rng.RandfRange(0f, totalWeight);
		if ((roll -= gruntWeight) <= 0f)
		{
			return EnemyKind.Grunt;
		}

		if ((roll -= bulwarkWeight) <= 0f)
		{
			return EnemyKind.Bulwark;
		}

		if ((roll -= chargerWeight) <= 0f)
		{
			return EnemyKind.Charger;
		}

		if ((roll -= mortarWeight) <= 0f)
		{
			return EnemyKind.Mortar;
		}

		return EnemyKind.Interceptor;
	}

	private Vector2 GetEnemySpawnPosition()
	{
		float angle = _rng.RandfRange(0f, Mathf.Tau);
		float distance = _rng.RandfRange(ArenaLayout.EnemySpawnMinDistance, ArenaLayout.EnemySpawnMaxDistance);
		Vector2 pos = _player.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

		pos.X = Mathf.Clamp(pos.X, -ArenaHalfW + ArenaLayout.EnemySpawnMargin, ArenaHalfW - ArenaLayout.EnemySpawnMargin);
		pos.Y = Mathf.Clamp(pos.Y, -ArenaHalfH + ArenaLayout.EnemySpawnMargin, ArenaHalfH - ArenaLayout.EnemySpawnMargin);
		return ClampResolve(pos, 1f);
	}

	private void SpawnBossForWave(int wave)
	{
		if (_boss != null)
		{
			return;
		}

		BossKind kind = GetBossKindForWave(wave);

		float spawnRadius = (Mathf.Min(ArenaHalfW, ArenaHalfH) - ArenaLayout.InteractableSpawnMargin) * 0.75f;
		Vector2 spawnDir = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f));
		if (spawnDir.LengthSquared() < 0.001f)
		{
			spawnDir = Vector2.Right;
		}

		Vector2 pos = _player.Position + spawnDir.Normalized() * spawnRadius;
		pos = ClampResolve(pos, ArenaLayout.InteractableSpawnMargin);

		_boss = new BossActor();
		AddChild(_boss);
		_boss.Configure(kind, pos, _player.Position, _bossHpScale);

		_bossLabel.Text = GetBossDisplayName(kind);
	}

	private BulletActor SpawnBullet(Vector2 pos, Vector2 vel, float dmg, TeamId team, Color col, float radius, float life, bool isCriticalHit = false, EquipmentItem? sourceItem = null)
	{
		BulletActor b = new();
		AddChild(b);
		Vector2 safePosition = RuntimeMath.FiniteOr(pos, _player != null ? _player.Position : Vector2.Zero);
		Vector2 safeVelocity = RuntimeMath.FiniteOr(vel, Vector2.Zero);
		float safeDamage = Mathf.Max(0f, RuntimeMath.FiniteOr(dmg, 0f));
		float safeRadius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, 1f), 0.05f, 80f);
		float safeLife = Mathf.Clamp(RuntimeMath.FiniteOr(life, 4f), 0.01f, 30f);
		b.Configure(safePosition, safeVelocity, safeDamage, team, col, safeRadius, safeLife, isCriticalHit, sourceItem);
		RegisterBulletDebugState(b);
		_bullets.Add(b);
		return b;
	}

	private void RegisterBulletDebugState(BulletActor bullet)
	{
		if (bullet.Team != TeamId.Player || !_arenaDebugOverridesActive)
		{
			CleanupBulletDebugState(bullet);
			return;
		}

		int ricochet = Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponRicochetFlat));
		int bounce = Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponBounceTimesFlat));

		if (ricochet > 0)
		{
			_debugRicochetLeft[bullet] = ricochet;
		}
		else
		{
			_debugRicochetLeft.Remove(bullet);
		}

		if (bounce > 0)
		{
			_debugBounceLeft[bullet] = bounce;
		}
		else
		{
			_debugBounceLeft.Remove(bullet);
		}
	}

	private void CleanupBulletDebugState(BulletActor bullet)
	{
		_debugRicochetLeft.Remove(bullet);
		_debugBounceLeft.Remove(bullet);
		CleanupLegacyBulletState(bullet);
	}

	private bool TryRicochetBullet(BulletActor bullet, Vector2 hitPos)
	{
		if (!_debugRicochetLeft.TryGetValue(bullet, out int ricochetLeft) || ricochetLeft <= 0)
		{
			return false;
		}

		Vector2 targetPos;
		if (!TryFindRicochetTarget(hitPos, out targetPos))
		{
			return false;
		}

		Vector2 dir = RuntimeMath.SafeDirection(targetPos - hitPos, bullet.Velocity);
		if (dir.LengthSquared() <= 0.0001f)
		{
			return false;
		}

		bullet.Position = hitPos + dir * (bullet.Radius + 2f);
		float speed = RuntimeMath.FiniteOr(bullet.Velocity.Length(), BaseBulletSpeed);
		bullet.Velocity = dir * speed;
		bullet.Damage *= 0.88f;

		ricochetLeft--;
		if (ricochetLeft <= 0)
		{
			_debugRicochetLeft.Remove(bullet);
		}
		else
		{
			_debugRicochetLeft[bullet] = ricochetLeft;
		}

		return true;
	}

	private bool TryFindRicochetTarget(Vector2 fromPos, out Vector2 targetPos)
	{
		targetPos = Vector2.Zero;
		float bestDistSq = float.MaxValue;

		if (_boss != null && _boss.IsAlive)
		{
			float dSq = (_boss.Position - fromPos).LengthSquared();
			if (dSq > 25f && dSq < bestDistSq && !IsBlockedByPillar(fromPos, _boss.Position))
			{
				targetPos = _boss.Position;
				bestDistSq = dSq;
			}
		}

		for (int i = 0; i < _enemies.Count; i++)
		{
			EnemyActor enemy = _enemies[i];
			if (!enemy.IsAlive)
			{
				continue;
			}

			float dSq = (enemy.Position - fromPos).LengthSquared();
			if (dSq <= 25f || dSq >= bestDistSq)
			{
				continue;
			}

			if (IsBlockedByPillar(fromPos, enemy.Position))
			{
				continue;
			}

			bestDistSq = dSq;
			targetPos = enemy.Position;
		}

		return bestDistSq < float.MaxValue;
	}

	private bool TryBounceBulletAtBounds(BulletActor bullet)
	{
		if (!_debugBounceLeft.TryGetValue(bullet, out int bouncesLeft) || bouncesLeft <= 0)
		{
			return false;
		}

		Vector2 pos = bullet.Position;
		Vector2 vel = bullet.Velocity;
		bool bounced = false;

		float minX = -ArenaHalfW + bullet.Radius;
		float maxX = ArenaHalfW - bullet.Radius;
		float minY = -ArenaHalfH + bullet.Radius;
		float maxY = ArenaHalfH - bullet.Radius;

		if (pos.X < minX || pos.X > maxX)
		{
			vel.X = -vel.X;
			pos.X = Mathf.Clamp(pos.X, minX, maxX);
			bounced = true;
		}

		if (pos.Y < minY || pos.Y > maxY)
		{
			vel.Y = -vel.Y;
			pos.Y = Mathf.Clamp(pos.Y, minY, maxY);
			bounced = true;
		}

		if (!bounced)
		{
			return false;
		}

		bullet.Position = pos;
		bullet.Velocity = vel;
		ConsumeBounceCharge(bullet, bouncesLeft);
		return true;
	}

	private bool TryBounceBulletFromPillar(BulletActor bullet)
	{
		if (!_debugBounceLeft.TryGetValue(bullet, out int bouncesLeft) || bouncesLeft <= 0)
		{
			return false;
		}

		for (int i = 0; i < _pillars.Count; i++)
		{
			Rect2 expanded = ExpandRect(_pillars[i], bullet.Radius + 1f);
			if (!expanded.HasPoint(bullet.Position))
			{
				continue;
			}

			float minX = expanded.Position.X;
			float minY = expanded.Position.Y;
			float maxX = expanded.Position.X + expanded.Size.X;
			float maxY = expanded.Position.Y + expanded.Size.Y;

			float dLeft = Mathf.Abs(bullet.Position.X - minX);
			float dRight = Mathf.Abs(maxX - bullet.Position.X);
			float dTop = Mathf.Abs(bullet.Position.Y - minY);
			float dBottom = Mathf.Abs(maxY - bullet.Position.Y);

			float minDist = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dTop, dBottom));

			Vector2 vel = bullet.Velocity;
			Vector2 pos = bullet.Position;
			if (minDist == dLeft || minDist == dRight)
			{
				vel.X = -vel.X;
			}
			else
			{
				vel.Y = -vel.Y;
			}

			Vector2 push = vel.LengthSquared() > 0.001f
				? vel.Normalized() * Mathf.Max(3f, bullet.Radius)
				: Vector2.Right * Mathf.Max(3f, bullet.Radius);

			bullet.Velocity = vel;
			bullet.Position = ClampResolve(pos + push, bullet.Radius);

			ConsumeBounceCharge(bullet, bouncesLeft);
			return true;
		}

		return false;
	}

	private void ConsumeBounceCharge(BulletActor bullet, int current)
	{
		int left = current - 1;
		if (left <= 0)
		{
			_debugBounceLeft.Remove(bullet);
		}
		else
		{
			_debugBounceLeft[bullet] = left;
		}
	}

	private Vector2 ClampResolve(Vector2 pos, float radius)
	{
		return ClampResolve(pos, radius, false);
	}

	private Vector2 ClampResolve(Vector2 pos, float radius, bool ignoreCubeCrushable)
	{
		if (!RuntimeMath.IsFinite(pos))
		{
			pos = _player != null ? _player.Position : Vector2.Zero;
		}

		radius = Mathf.Max(0.01f, RuntimeMath.FiniteOr(radius, 1f));
		pos.X = Mathf.Clamp(pos.X, -ArenaHalfW + radius, ArenaHalfW - radius);
		pos.Y = Mathf.Clamp(pos.Y, -ArenaHalfH + radius, ArenaHalfH - radius);

		for (int i = 0; i < 3; i++)
		{
			Vector2 before = pos;
			pos = ResolvePillarCollision(pos, radius, ignoreCubeCrushable);
			pos.X = Mathf.Clamp(pos.X, -ArenaHalfW + radius, ArenaHalfW - radius);
			pos.Y = Mathf.Clamp(pos.Y, -ArenaHalfH + radius, ArenaHalfH - radius);
			if ((pos - before).LengthSquared() < 0.01f)
			{
				break;
			}
		}

		return pos;
	}

	private void ApplyPlayerDamage(float amount)
	{
		float incomingDamage = amount;
		if (amount <= 0f)
		{
			return;
		}

		NotifyEquipmentPlayerIncomingDamage();

		float dodgeChance = Mathf.Clamp(GetTotalDodgeChancePercent() / 100f, 0f, 0.95f);
		if (dodgeChance > 0f)
		{
			if (_rng.Randf() < dodgeChance)
			{
				return;
			}
		}

		if (_player.BubbleActive)
		{
			_player.BubbleHp -= amount;
			if (_player.BubbleHp <= 0f)
			{
				_player.BubbleHp = 0f;
				_player.BubbleActive = false;
			}

			NotifyEquipmentPlayerDamageBlocked();
			return;
		}

		if (_player.BuffShieldCharges > 0)
		{
			_player.BuffShieldCharges--;
			NotifyEquipmentPlayerDamageBlocked();
			return;
		}

		if (_player.ShieldedPieceCount >= 1 && _player.Overshield > 0f)
		{
			_player.Overshield -= amount;
			if (_player.Overshield < 0f)
			{
				amount = -_player.Overshield;
				_player.Overshield = 0f;
				TriggerEquipmentOvershieldBreak();
			}
			else
			{
				amount = 0f;
				NotifyEquipmentPlayerDamageBlocked();
			}
		}

		if (amount > 0f)
		{
			_player.Hp -= amount;
			_player.TriggerHurtFlash();
			_hurtFlashTimer = 0.22f;
			TrackBloodPriceDamage(incomingDamage);
			if (_player.Hp <= 0f)
			{
				_player.Hp = 0f;
				ResetRun();
			}
		}
	}

	private void TrackBloodPriceDamage(float amount)
	{
		if (amount <= 0f)
		{
			return;
		}

		EquipmentItem? primary = _equippedLoadout != null && _equippedLoadout.Length > 0 ? _equippedLoadout[0] : null;
		if (primary == null || !HasWeaponTransformFlag(primary, "blood_price_reactor"))
		{
			return;
		}

		float storePercent = Mathf.Clamp(GetWeaponTransformMagnitude(primary, "blood_price_reactor", 0.30f), 0f, 2f);
		float stored = amount * storePercent;
		if (stored <= 0f)
		{
			return;
		}

		_bloodPriceStoredDamage = Mathf.Clamp(_bloodPriceStoredDamage + stored, 0f, _player.MaxHp * 6f);
		if (_bloodPricePulseTimer <= 0f)
		{
			_bloodPricePulseTimer = 1.5f;
		}
	}

	private bool TrySpawnSerrationBloom(BulletActor bullet, Vector2 hitPosition)
	{
		EquipmentItem? sourceItem = bullet.SourceItem;
		if (sourceItem == null || !HasWeaponTransformFlag(sourceItem, "serration_bloom"))
		{
			return false;
		}

		int splitCount = Mathf.Clamp(GetWeaponTransformCount(sourceItem, "serration_bloom", 2), 1, 8);
		float splitDamageScale = Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "serration_bloom", 0.55f), 0.05f, 3f);
		float baseAngle = Mathf.Atan2(bullet.Velocity.Y, bullet.Velocity.X);
		float spread = splitCount <= 1 ? 0f : Mathf.DegToRad(40f);
		float speed = Mathf.Max(8f, bullet.Velocity.Length() * 0.9f);
		float life = Mathf.Max(0.15f, bullet.Life * 0.55f);

		for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
		{
			float t = splitCount <= 1 ? 0.5f : splitIndex / (float)(splitCount - 1);
			float angle = baseAngle + Mathf.Lerp(-spread, spread, t);
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
			SpawnBullet(
				hitPosition + direction * (bullet.Radius + 2f),
				direction * speed,
				bullet.Damage * splitDamageScale,
				bullet.Team,
				bullet.BulletColor,
				Mathf.Max(0.1f, bullet.Radius * 0.75f),
				life,
				bullet.IsCriticalHit,
				sourceItem);
		}

		return true;
	}

	private void SpawnEnemyDamageNumber(EnemyActor enemy, float damage, bool critical)
	{
		float headHeight = enemy.Kind switch
		{
			EnemyKind.Interceptor => 3.0f,
			EnemyKind.Bulwark => 3.2f,
			EnemyKind.Charger => 2.5f,
			EnemyKind.Mortar => 2.7f,
			_ => 2.0f,
		};
		SpawnFloatingDamageNumber(enemy.Position, headHeight + enemy.Radius * 0.35f, damage, critical);
	}

	private void SpawnBossDamageNumber(BossActor boss, float damage, bool critical)
	{
		SpawnFloatingDamageNumber(boss.Position, 5.4f + boss.Radius * 0.25f, damage, critical);
	}

	private void SpawnFloatingDamageNumber(Vector2 worldPosition, float height, float damage, bool critical)
	{
		if (damage <= 0f)
		{
			return;
		}

		FloatingDamageNumber3D number = new();
		AddChild(number);
		number.Configure(damage, critical, new Vector3(worldPosition.X, height, worldPosition.Y));
		_floatingDamageNumbers.Enqueue(number);

		while (_floatingDamageNumbers.Count > MaxFloatingDamageNumbers)
		{
			FloatingDamageNumber3D oldest = _floatingDamageNumbers.Dequeue();
			if (IsInstanceValid(oldest))
			{
				oldest.QueueFree();
			}
		}
	}

	private void ResetRun()
	{
		PersistRunLoadoutWeaponProgress();

		foreach (EnemyActor e in _enemies)
		{
			if (IsInstanceValid(e))
			{
				e.QueueFree();
			}
		}
		_enemies.Clear();

		foreach (BulletActor b in _bullets)
		{
			if (IsInstanceValid(b))
			{
				b.QueueFree();
			}
		}
		_bullets.Clear();
		_debugRicochetLeft.Clear();
		_debugBounceLeft.Clear();

		foreach (PickupActor p in _pickups)
		{
			if (IsInstanceValid(p))
			{
				p.QueueFree();
			}
		}
		_pickups.Clear();

		while (_floatingDamageNumbers.Count > 0)
		{
			FloatingDamageNumber3D number = _floatingDamageNumbers.Dequeue();
			if (IsInstanceValid(number))
			{
				number.QueueFree();
			}
		}

		if (_boss != null && IsInstanceValid(_boss))
		{
			_boss.QueueFree();
		}
		_boss = null;
		_bossLaserActive = false;
		_bossBeam.Disable();

		_player.Position = new Vector2(ArenaLayout.PlayerStartX, ArenaLayout.PlayerStartZ);
		_player.ResetStats();
		ResetProgressionState();

		_waveNum = 1;
		_waveTimer = 0f;
		_gameTimer = 0f;
		_spawnCd = 0f;
		_waveLeft = 8;
		_kills = 0;
		RebuildWaveBalance(_waveNum);
		RefreshChallengePillarForWave();
		_pauseForItemChoice = false;
		_levelUpPanel.Visible = false;
		_waveBannerTimer = 0f;
		_hurtFlashTimer = 0f;
		if (IsInstanceValid(_waveBanner))
		{
			_waveBanner.Visible = false;
		}
		if (IsInstanceValid(_hurtFlashOverlay))
		{
			_hurtFlashOverlay.Color = new Color(0.85f, 0.06f, 0.08f, 0f);
		}
		if (IsInstanceValid(_lowHpOverlay))
		{
			_lowHpOverlay.Color = new Color(0.20f, 0.02f, 0.02f, 0f);
		}
	}

	private Vector2 GetMouseGroundPoint()
	{
		if (!IsInstanceValid(_camera))
		{
			return _player != null ? _player.Position : Vector2.Zero;
		}

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 rayOrigin = _camera.ProjectRayOrigin(mouse);
		Vector3 rayDir = _camera.ProjectRayNormal(mouse);

		if (Mathf.Abs(rayDir.Y) < 0.0001f)
		{
			return _player != null ? _player.Position : Vector2.Zero;
		}

		float t = -rayOrigin.Y / rayDir.Y;
		if (t < 0f)
		{
			t = 0f;
		}

		Vector3 hit = rayOrigin + rayDir * t;
		return new Vector2(
			Mathf.Clamp(hit.X, -ArenaHalfW, ArenaHalfW),
			Mathf.Clamp(hit.Z, -ArenaHalfH, ArenaHalfH));
	}


	private void BuildDebugPanel()
	{
		_debugLayer = new CanvasLayer();
		AddChild(_debugLayer);

		_arenaDebugPanel = GetNodeOrNull<Control>("ArenaDebugPanel");
		if (!IsInstanceValid(_arenaDebugPanel))
		{
			return;
		}

		_arenaDebugPanel.Reparent(_debugLayer);
		_arenaDebugPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		_arenaDebugPanel.OffsetLeft = -760f;
		_arenaDebugPanel.OffsetTop = 50f;
		_arenaDebugPanel.OffsetRight = -12f;
		_arenaDebugPanel.OffsetBottom = 850f;
		_arenaDebugPanel.MouseFilter = Control.MouseFilterEnum.Pass;

		_arenaDebugPanelUi = _arenaDebugPanel as ArenaDebugPanelUI;
		_arenaDebugPanelUi?.EnableExternalMenuGate(true);

		if (_arenaDebugPanelUi != null)
		{
			_arenaDebugPanelUi.ExternalMenuVisibilityRequested += SetDebugPanelVisible;
		};

		SetDebugPanelVisible(false);
	}

	private void ForceSpawnBoss(BossKind kind)
	{
		if (_boss != null)
		{
			_boss.QueueFree();
			_boss = null;
		}

		float spawnRadius = (Mathf.Min(ArenaHalfW, ArenaHalfH) - ArenaLayout.InteractableSpawnMargin) * 0.80f;
		Vector2 spawnDir = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f));
		if (spawnDir.LengthSquared() < 0.001f)
		{
			spawnDir = Vector2.Right;
		}

		Vector2 pos = _player.Position + spawnDir.Normalized() * spawnRadius;
		pos = ClampResolve(pos, ArenaLayout.InteractableSpawnMargin);

		_boss = new BossActor();
		AddChild(_boss);
		_boss.Configure(kind, pos, _player.Position, _bossHpScale);

		_bossLabel.Text = GetBossDisplayName(kind);
	}

	private void TickPickups(float dt)
	{
		MergeNearbyRewardPickups(dt);

		for (int i = _pickups.Count - 1; i >= 0; i--)
		{
			PickupActor p = _pickups[i];
			if (!IsInstanceValid(p) || !p.Active)
			{
				_pickups.RemoveAt(i);
				continue;
			}

			float rr = p.Radius + _player.Radius;
			if ((p.Position - _player.Position).LengthSquared() <= rr * rr)
			{
				if (!p.TryBeginCollection())
				{
					continue;
				}

				if (CollectPickup(p))
				{
					p.Deactivate();
					_pickups.RemoveAt(i);
				}
				else
				{
					p.CancelCollectionAttempt(1.25f);
				}
			}
		}
	}

	private void MergeNearbyRewardPickups(float dt)
	{
		const float attractionRange = 42f;
		const float attractionStrength = 360f;
		const float mergeRadiusScale = 0.55f;

		for (int i = _pickups.Count - 1; i >= 0; i--)
		{
			PickupActor first = _pickups[i];
			if (!CanMergeRewardPickup(first))
			{
				continue;
			}

			for (int j = i - 1; j >= 0; j--)
			{
				PickupActor second = _pickups[j];
				if (!CanMergeRewardPickup(second) || first.Kind != second.Kind)
				{
					continue;
				}

				Vector2 offset = second.Position - first.Position;
				float distanceSquared = offset.LengthSquared();
				if (distanceSquared > attractionRange * attractionRange)
				{
					continue;
				}

				float distance = Mathf.Max(0.001f, Mathf.Sqrt(distanceSquared));
				Vector2 direction = offset / distance;
				float closeness = 1f - Mathf.Clamp(distance / attractionRange, 0f, 1f);
				float attraction = attractionStrength * (0.35f + closeness * 0.85f);
				first.Velocity += direction * attraction * dt;
				second.Velocity -= direction * attraction * dt;

				float mergeRadius = Mathf.Max(8f, (first.Radius + second.Radius) * mergeRadiusScale);
				if (distanceSquared > mergeRadius * mergeRadius)
				{
					continue;
				}

				PickupActor recipient = SelectRewardMergeRecipient(first, second);
				PickupActor donor = ReferenceEquals(recipient, first) ? second : first;
				recipient.AbsorbPickup(donor);
				donor.Deactivate();

				if (ReferenceEquals(donor, first))
				{
					break;
				}
			}
		}
	}

	private static bool CanMergeRewardPickup(PickupActor? pickup)
	{
		return pickup != null
			&& IsInstanceValid(pickup)
			&& pickup.Active
			&& pickup.SupportsValueFusion
			&& pickup.CanAttemptCollection;
	}

	private static PickupActor SelectRewardMergeRecipient(PickupActor first, PickupActor second)
	{
		if (first.Value != second.Value)
		{
			return first.Value > second.Value ? first : second;
		}

		if (first.Radius != second.Radius)
		{
			return first.Radius >= second.Radius ? first : second;
		}

		return first;
	}

	private bool CollectPickup(PickupActor pickup)
	{
		float xpGainMult = (1f + GetModifierTotal(ModifierKind.XpGainPct)) * GetEquipmentXpGainMultiplier();
		float coinGainMult = (1f + GetModifierTotal(ModifierKind.CoinGainPct)) * GetEquipmentCoinGainMultiplier();

		switch (pickup.Kind)
		{
			case PickupKind.Coin:
				_coins += Mathf.Max(1, Mathf.RoundToInt(pickup.Value * coinGainMult));
				return true;
			case PickupKind.Heal:
				_player.Hp = Mathf.Min(_player.MaxHp, _player.Hp + pickup.Value);
				return true;
			case PickupKind.ShapeItem:
				return CollectShapeItemPickup(pickup);
			case PickupKind.Essence:
				return CollectEssencePickup(pickup);
			case PickupKind.Modulator:
				return CollectModulatorPickup(pickup);
			default:
				GrantXp(pickup.Value * GetRarityMultiplier(pickup.Rarity) * xpGainMult);
				for (int i = 0; i < pickup.Modifiers.Count; i++)
				{
					ModifierRoll m = pickup.Modifiers[i];
					_activeModifiers.Add(new ModifierRoll(m.Kind, m.Value * 0.45f, m.Rarity));
				}
				ApplyDerivedPlayerStats(false);
				return true;
		}
	}

	private bool CollectShapeItemPickup(PickupActor pickup)
	{
		EquipmentItem? item = pickup.EquipmentItem;
		if (item == null)
		{
			return true;
		}

		_inventoryManager ??= GetNodeOrNull<InventoryManager>("/root/InventoryManager");
		if (_inventoryManager == null)
		{
			GD.PushWarning("Cannot collect shape drop: InventoryManager not found.");
			return false;
		}

		if (!_inventoryManager.TryAddItem(item))
		{
			GD.PushWarning("Cannot collect shape drop: inventory is full.");
			return false;
		}

		ShowStatusBanner($"Picked up {item.Rarity?.DisplayName ?? "Common"} {item.Shape?.DisplayName ?? "Shape"}");
		return true;
	}

	private bool CollectEssencePickup(PickupActor pickup)
	{
		ModifierData? essence = pickup.EssenceData;
		if (essence == null)
		{
			return true;
		}

		_inventoryManager ??= GetNodeOrNull<InventoryManager>("/root/InventoryManager");
		if (_inventoryManager == null)
		{
			GD.PushWarning("Cannot collect essence drop: InventoryManager not found.");
			return false;
		}

		if (!_inventoryManager.StoreEssence(essence))
		{
			return false;
		}

		ShowStatusBanner($"Stored {essence.DisplayName} essence");
		return true;
	}

	private bool CollectModulatorPickup(PickupActor pickup)
	{
		WeaponModulatorData? modulator = pickup.ModulatorData;
		if (modulator == null)
		{
			return true;
		}

		if (!TryStoreRunModulator(modulator))
		{
			return false;
		}

		ShowStatusBanner($"Stashed {modulator.DisplayName} modulator");
		return true;
	}

	private PickupActor SpawnPickup(PickupKind kind, int value, Vector2 pos, float spread = 180f, RarityTier? forcedRarity = null)
	{
		PickupActor p = new();
		AddChild(p);

		float ang = _rng.RandfRange(0f, Mathf.Tau);
		float spd = _rng.RandfRange(40f, spread);
		Vector2 impulse = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd;
		RarityTier rarity = forcedRarity ?? RollPickupRarity(kind);
		ModifierRoll[] mods = kind == PickupKind.XpOrb ? RollDropModifiers(rarity) : Array.Empty<ModifierRoll>();

		p.Configure(kind, value, pos, impulse, rarity, mods);
		_pickups.Add(p);
		return p;
	}

	public void SpawnRandomShapeItemDrop()
	{
		EquipmentItem item = CreateRandomShapeDropItem();
		SpawnShapeItemPickup(item, GetShapeItemDropPosition());
	}

	public void SpawnRandomShapeItemDrop(string rarityKey)
	{
		EquipmentItem item = CreateRandomShapeDropItem(rarityKey);
		SpawnShapeItemPickup(item, GetShapeItemDropPosition());
	}

	public void SpawnRandomEssenceDrop()
	{
		ModifierData essence = CreateRandomEssenceDrop();
		SpawnEssencePickup(essence, GetShapeItemDropPosition());
	}

	public void SpawnRandomModulatorDrop()
	{
		WeaponModulatorData modulator = CreateRandomModulatorDrop();
		SpawnModulatorPickup(modulator, GetShapeItemDropPosition());
	}

	public void SpawnEssenceDrop(ModifierKey key)
	{
		if (key == ModifierKey.Normal)
		{
			return;
		}

		ModifierData essence = EquipmentCatalogDefaults.CreateModifierByKey(key, includeBaselineNormal: false);
		SpawnEssencePickup(essence, GetShapeItemDropPosition());
	}

	public void SpawnModulatorDrop(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		string requestedKey = key.Trim();
		WeaponModulatorTier tier = WeaponModulatorTier.Baseline;
		int tierSeparatorIndex = requestedKey.LastIndexOf(':');
		if (tierSeparatorIndex > 0 && tierSeparatorIndex < requestedKey.Length - 1)
		{
			string requestedTier = requestedKey[(tierSeparatorIndex + 1)..];
			if (WeaponModulatorData.TryParseTierKey(requestedTier, out WeaponModulatorTier parsedTier))
			{
				tier = parsedTier;
				requestedKey = requestedKey[..tierSeparatorIndex];
			}
		}

		WeaponModulatorData modulator = ModulatorCatalog.CreateByKey(requestedKey, tier);
		SpawnModulatorPickup(modulator, GetShapeItemDropPosition());
	}

	private PickupActor SpawnShapeItemPickup(EquipmentItem item, Vector2 pos)
	{
		PickupActor p = new();
		AddChild(p);
		p.ConfigureShapeItem(item, pos, Vector2.Zero);
		_pickups.Add(p);
		return p;
	}

	private PickupActor SpawnEssencePickup(ModifierData essence, Vector2 pos)
	{
		PickupActor p = new();
		AddChild(p);
		p.ConfigureEssence(essence, pos, Vector2.Zero);
		_pickups.Add(p);
		return p;
	}

	private PickupActor SpawnModulatorPickup(WeaponModulatorData modulator, Vector2 pos)
	{
		PickupActor p = new();
		AddChild(p);
		p.ConfigureModulator(modulator, pos, Vector2.Zero);
		_pickups.Add(p);
		return p;
	}

	private EquipmentItem CreateRandomShapeDropItem(string? forcedRarityKey = null)
	{
		List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
		List<RarityData> rarities = EquipmentCatalogDefaults.CreateDefaultRarities();
		List<ModifierData> modifiers = EquipmentCatalogDefaults.CreateDefaultModifiers(includeBaselineNormal: true);

		ShapeData shape = PickWeighted(shapes, static s => s.Weight);
		RarityData rarity = string.IsNullOrWhiteSpace(forcedRarityKey)
			? PickWeighted(rarities, static r => r.Weight)
			: EquipmentCatalogDefaults.CreateRarityByKey(forcedRarityKey);
		ModifierData modifier = PickWeighted(modifiers, static m => m.Weight);

		StatGenerator generator = new(_rng);
		return generator.BuildItem(shape, rarity, modifier, 1);
	}

	private ModifierData CreateRandomEssenceDrop()
	{
		List<ModifierData> essences = EquipmentCatalogDefaults.CreateDefaultModifiers(includeBaselineNormal: false);
		return PickWeighted(essences, static essence => essence.Weight);
	}

	private WeaponModulatorData CreateRandomModulatorDrop()
	{
		List<WeaponModulatorData> modulators = ModulatorCatalog.CreateWeightedRollPool();
		WeaponModulatorData modulator = PickWeighted(modulators, static modulator => modulator.Weight);
		if (!modulator.UsesTierPresentation)
		{
			return modulator;
		}

		return ModulatorCatalog.CreateByKey(modulator.Key, RollRandomTieredModulatorTier());
	}

	private WeaponModulatorTier RollRandomTieredModulatorTier()
	{
		float roll = _rng.Randf();
		if (roll < 0.25f)
		{
			return WeaponModulatorTier.Flawed;
		}

		if (roll < 0.75f)
		{
			return WeaponModulatorTier.Baseline;
		}

		if (roll < 0.95f)
		{
			return WeaponModulatorTier.Enhanced;
		}

		return WeaponModulatorTier.Flawless;
	}

	private Vector2 GetShapeItemDropPosition()
	{
		Vector2 center = _player?.Position ?? Vector2.Zero;
		float angle = _rng.RandfRange(0f, Mathf.Tau);
		float distance = _rng.RandfRange(14f, 24f);
		Vector2 offset = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
		Vector2 pos = center + offset;

		return new Vector2(
			Mathf.Clamp(pos.X, -ArenaHalfW + 18f, ArenaHalfW - 18f),
			Mathf.Clamp(pos.Y, -ArenaHalfH + 18f, ArenaHalfH - 18f));
	}

	private T PickWeighted<T>(IReadOnlyList<T> entries, Func<T, float> weightSelector)
	{
		if (entries.Count == 0)
		{
			throw new InvalidOperationException("Cannot pick from an empty weighted list.");
		}

		float total = 0f;
		for (int i = 0; i < entries.Count; i++)
		{
			total += Mathf.Max(0.0001f, weightSelector(entries[i]));
		}

		float roll = _rng.RandfRange(0f, total);
		float cursor = 0f;
		for (int i = 0; i < entries.Count; i++)
		{
			cursor += Mathf.Max(0.0001f, weightSelector(entries[i]));
			if (roll <= cursor)
			{
				return entries[i];
			}
		}

		return entries[^1];
	}

	private void GrantEnemyDropRewards(EnemyActor enemy)
	{
		int xpBase = enemy.Kind switch
		{
			EnemyKind.Bulwark => 28,
			EnemyKind.Interceptor => 24,
			EnemyKind.Mortar => 20,
			EnemyKind.Charger => 18,
			_ => 12,
		};
		int xp = Mathf.RoundToInt(xpBase * (0.95f + (_waveNum - 1) * 0.07f));
		SpawnPickup(PickupKind.XpOrb, xp, enemy.Position);
		TrySpawnEquipmentBonusXpOrb(enemy, xp);

		if (_rng.Randf() < 0.22f)
		{
			SpawnPickup(PickupKind.Coin, 1, enemy.Position, 130f);
		}

		if (_rng.Randf() < 0.06f && _player.Hp < _player.MaxHp * 0.8f)
		{
			SpawnPickup(PickupKind.Heal, Mathf.RoundToInt(_player.MaxHp * 0.04f), enemy.Position, 140f);
		}

		if (_rng.Randf() < EnemyEssenceDropChance)
		{
			SpawnEssencePickup(CreateRandomEssenceDrop(), enemy.Position);
		}

		if (_rng.Randf() < EnemyModulatorDropChance)
		{
			SpawnModulatorPickup(CreateRandomModulatorDrop(), enemy.Position);
		}
	}

	private void GrantBossDropRewards(Vector2 pos)
	{
		for (int i = 0; i < 10; i++)
		{
			SpawnPickup(PickupKind.XpOrb, Mathf.RoundToInt((34 + _waveNum * 2) * 1.1f), pos, 240f, RarityTier.Rare);
		}

		for (int i = 0; i < 8; i++)
		{
			SpawnPickup(PickupKind.XpOrb, Mathf.RoundToInt((30 + _waveNum * 2) * 1.25f), pos, 250f, RarityTier.Epic);
		}

		for (int i = 0; i < 12; i++)
		{
			SpawnPickup(PickupKind.Coin, 1, pos, 220f);
		}

		if (_player.Hp < _player.MaxHp * 0.65f)
		{
			SpawnPickup(PickupKind.Heal, Mathf.RoundToInt(_player.MaxHp * 0.16f), pos, 200f);
		}

		SpawnEssencePickup(CreateRandomEssenceDrop(), pos + new Vector2(-10f, 0f));
		SpawnModulatorPickup(CreateRandomModulatorDrop(), pos + new Vector2(10f, 0f));
	}

	private void GrantXp(float amount)
	{
		if (amount <= 0f)
		{
			return;
		}

		_xp += amount;
		GrantXpToRunLoadoutWeapons(amount);
	}

	private void HandleProgressionHotkeys()
	{
		if (IsArenaGameplayPaused())
		{
			_buyHeld = false;
			return;
		}

		bool buyPressed = Input.IsKeyPressed(Key.B);
		if (buyPressed && !_buyHeld)
		{
			TryBuyBubble();
		}

		_buyHeld = buyPressed;
	}

	private void TryBuyBubble()
	{
		if (_coins < BubbleCost || _player.BubbleActive)
		{
			return;
		}

		_coins -= BubbleCost;
		float bubble = 2200f + 200f * GetItemPower(RunItemId.VitalCore);
		bubble *= 1f + GetModifierTotal(ModifierKind.BubbleStrengthPct);
		if (_shapeKind == ShapeKind.BulwarkSphere)
		{
			bubble *= 1.20f;
		}

		_player.GrantBubble(bubble);
	}

	private void FirePlayerVolley(Vector2 dir)
	{
		dir = RuntimeMath.SafeDirection(dir, Vector2.Right);
		float radius = Mathf.Clamp(RuntimeMath.FiniteOr(BaseBulletRadius * _bulletRadiusScale, BaseBulletRadius), 0.05f, 80f);
		float damage = Mathf.Max(0f, RuntimeMath.FiniteOr(_player.BulletDamage, BaseBulletDamage));
		Color shotColor = GetWeaponShotColor(_weaponKind, _weaponRarity);
		float shotLife = Mathf.Max(0.01f, RuntimeMath.FiniteOr(GetWeaponShotLife(_weaponKind, _weaponRarity), BaseShotLife));
		bool isCriticalHit = false;

		if (_arenaDebugOverridesActive)
		{
			radius *= Mathf.Max(0.05f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponSizePercent / 100f, 1f));
			damage *= Mathf.Max(0f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponDamagePercentBonus / 100f, 1f));
			shotLife *= Mathf.Max(0.01f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.RangeFlat / 100f, 1f));

			float critChance = Mathf.Clamp(_arenaDebugOverrides.CritChancePercent / 100f, 0f, 1f);
			if (critChance > 0f && _rng.Randf() < critChance)
			{
				damage *= GetCritDamageMultiplier(_arenaDebugOverrides.CritDamagePercent);
				isCriticalHit = true;
			}
		}

		damage = ApplyEquipmentShotDamageModifiers(damage, out bool equipmentCritical);
		isCriticalHit |= equipmentCritical;

		radius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, BaseBulletRadius), 0.05f, 80f);
		damage = Mathf.Max(0f, RuntimeMath.FiniteOr(damage, BaseBulletDamage));
		shotLife = Mathf.Clamp(RuntimeMath.FiniteOr(shotLife, BaseShotLife), 0.01f, 30f);

		SpawnBullet(
			_player.Position,
			dir * _player.BulletSpeed,
			damage,
			TeamId.Player,
			shotColor,
			radius,
			shotLife,
			isCriticalHit);

		int extraProjectileCount = _arenaDebugOverridesActive
			? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponProjectileCountFlat))
			: 0;

		for (int p = 0; p < extraProjectileCount; p++)
		{
			float spread = Mathf.DegToRad(2.5f * (p + 1));
			Vector2 extraDir = (p % 2 == 0) ? dir.Rotated(spread) : dir.Rotated(-spread);
			SpawnBullet(_player.Position, extraDir * _player.BulletSpeed, damage * 0.9f, TeamId.Player, shotColor, radius * 0.92f, shotLife, isCriticalHit);
		}

		int split = Mathf.FloorToInt(GetItemPower(RunItemId.SplitShot)) + GetModifierFlat(ModifierKind.ExtraSplitShot);
		for (int i = 1; i <= split; i++)
		{
			float ang = Mathf.DegToRad(8f * i);
			float dmgScale = Mathf.Max(0.45f, 0.86f - 0.08f * (i - 1));

			Vector2 d1 = dir.Rotated(ang);
			Vector2 d2 = dir.Rotated(-ang);

			SpawnBullet(_player.Position, d1 * _player.BulletSpeed, damage * dmgScale, TeamId.Player, shotColor, radius * 0.92f, shotLife, isCriticalHit);
			SpawnBullet(_player.Position, d2 * _player.BulletSpeed, damage * dmgScale, TeamId.Player, shotColor, radius * 0.92f, shotLife, isCriticalHit);
		}
	}

	private void ResetProgressionState()
	{
		ClearChallengePillar();
		ResetMerchantEncounterState();

		_level = 1;
		_xp = 0f;
		_xpToNext = 120f;
		_coins = 0;
		_weaponKind = WeaponKind.Square;
		_weaponRarity = RarityTier.Common;
		_weaponCooldownSeconds = GetLegacyWeaponCooldown(_weaponKind);
		_shapeKind = ShapeKind.CoreCube;
		_shapeRarity = RarityTier.Common;
		_pendingItemChoices = 0;
		_pauseForItemChoice = false;
		_buyHeld = false;
		_itemStacks.Clear();
		_itemRarityPower.Clear();
		_activeModifiers.Clear();
		ResetLegacyWeaponEffects();
		ResetEquipmentModifierRuntime();
		ApplyEquippedPrimaryToRun();

		if (_levelUpPanel != null)
		{
			_levelUpPanel.Visible = false;
		}

		_player.MaxHp = BaseMaxHp;
		_player.Hp = BaseMaxHp;
		_player.BulletSpeed = BaseBulletSpeed;
		_player.BubbleActive = false;
		_player.BubbleHp = 0f;
		_player.BuffShieldCharges = 0;
		_player.Overshield = 0f;
		_player.ShieldedPieceCount = 0;

		ApplyDerivedPlayerStats(false);
		RebuildWaveBalance(_waveNum);
	}

	private RarityTier RollPickupRarity(PickupKind kind)
	{
		if (kind == PickupKind.Heal)
		{
			return _rng.Randf() < 0.08f ? RarityTier.Uncommon : RarityTier.Common;
		}

		if (kind == PickupKind.Coin)
		{
			RarityTier r = RollRarity(_waveNum * 0.008f + _level * 0.005f);
			return (int)r > (int)RarityTier.Rare ? RarityTier.Rare : r;
		}

		return RollRarity(_waveNum * 0.014f + _level * 0.012f);
	}

	private ModifierRoll[] RollDropModifiers(RarityTier rarity)
	{
		if ((int)rarity < (int)RarityTier.Rare)
		{
			return Array.Empty<ModifierRoll>();
		}

		if (_rng.Randf() > 0.34f + (int)rarity * 0.08f)
		{
			return Array.Empty<ModifierRoll>();
		}

		RarityTier modRarity = rarity;
		if ((int)modRarity > (int)RarityTier.Legendary)
		{
			modRarity = RarityTier.Legendary;
		}

		return RollChoiceModifiers(modRarity);
	}

	private static Color GetWeaponShotColor(WeaponKind kind, RarityTier rarity)
	{
		float t = (int)rarity * 0.10f;
		Color baseColor = kind switch
		{
			WeaponKind.Circle => new Color(0.35f, 0.95f, 0.55f, 1f),
			WeaponKind.Triangle => new Color(1f, 0.48f, 0.24f, 1f),
			WeaponKind.Hexagon => new Color(1f, 0.82f, 0.22f, 1f),
			WeaponKind.Sphere => new Color(0.30f, 0.62f, 1f, 1f),
			WeaponKind.Cube => new Color(0.70f, 0.46f, 1f, 1f),
			WeaponKind.Pyramid => new Color(1f, 0.34f, 0.78f, 1f),
			WeaponKind.Cone => new Color(1f, 0.42f, 0.16f, 1f),
			WeaponKind.Tesseract => new Color(0.58f, 0.52f, 1f, 1f),
			WeaponKind.Hyperphere => new Color(0.28f, 1f, 0.86f, 1f),
			WeaponKind.Pentachoron => new Color(1f, 0.62f, 0.95f, 1f),
			WeaponKind.Hypercone => new Color(1f, 0.62f, 0.18f, 1f),
			WeaponKind.ImpossibleCube => new Color(0.35f, 1f, 0.45f, 1f),
			WeaponKind.Mobius => new Color(0.74f, 0.82f, 1f, 1f),
			WeaponKind.Klein => new Color(0.45f, 0.78f, 1f, 1f),
			WeaponKind.BoysSurface => new Color(1f, 0.74f, 0.26f, 1f),
			_ => new Color(0.35f, 1f, 1f, 1f),
		};

		return baseColor.Lerp(new Color(1f, 0.9f, 0.4f, 1f), t);
	}

	private static float GetWeaponShotLife(WeaponKind kind, RarityTier rarity)
	{
		float life = kind switch
		{
			WeaponKind.Circle => 2.8f,
			WeaponKind.Triangle => 0.45f,
			WeaponKind.Hexagon => 3.2f,
			WeaponKind.Sphere => 1.2f,
			WeaponKind.Cube => 10f,
			WeaponKind.Pyramid => 4f,
			WeaponKind.Cone => 0.6f,
			WeaponKind.Tesseract => 3f,
			WeaponKind.Hyperphere => 4f,
			WeaponKind.Pentachoron => 0.7f,
			WeaponKind.Hypercone => 5f,
			WeaponKind.ImpossibleCube => 4f,
			WeaponKind.Mobius => 3.5f,
			WeaponKind.Klein => 0.8f,
			WeaponKind.BoysSurface => 5f,
			_ => BaseShotLife,
		};

		return life * (1f + (GetRarityMultiplier(rarity) - 1f) * 0.18f);
	}

	private void RebuildWaveBalance(int wave)
	{
		float wf = Mathf.Max(0f, wave - 1);
		float bossStep = Mathf.Max(1f, wave / (float)BossWaveInterval);

		_enemyHpScale = 1f + 0.085f * Mathf.Pow(wf, 1.12f);
		_enemyDmgScale = 1f + 0.065f * Mathf.Pow(wf, 1.08f);
		_bossHpScale = 1f + 0.34f * Mathf.Pow(Mathf.Max(0f, bossStep - 1f), 1.18f);
		_bossDmgScale = 1f + 0.20f * Mathf.Pow(Mathf.Max(0f, bossStep - 1f), 1.10f) + wf * 0.01f;

		_waveSpawnCadence = Mathf.Clamp(0.52f - wf * 0.015f, 0.24f, 0.52f);
		_waveLeft = 8 + Mathf.Min(30, Mathf.FloorToInt(wf * 2.25f));
	}

	private float GetInterceptorChance(int wave)
	{
		if (wave < 6)
		{
			return 0f;
		}

		float chance = 0.15f + (wave - 6) * 0.02f;
		return Mathf.Clamp(chance, 0.15f, 0.68f);
	}

	private void OpenNextItemChoice()
	{
		if (_pendingItemChoices <= 0)
		{
			_pauseForItemChoice = false;
			_levelUpPanel.Visible = false;
			return;
		}

		_pauseForItemChoice = true;
		RollItemChoices();
		_levelUpTitle.Text = _pendingItemChoices > 1
			? $"LEVEL UP - CHOOSE UPGRADE ({_pendingItemChoices} LEFT)"
			: "LEVEL UP - CHOOSE UPGRADE";
		_itemBtn0.Text = BuildChoiceButtonText(_currentChoiceRolls[0]);
		_itemBtn1.Text = BuildChoiceButtonText(_currentChoiceRolls[1]);
		_itemBtn2.Text = BuildChoiceButtonText(_currentChoiceRolls[2]);
		_levelUpPanel.Visible = true;
		_levelUpPanel.MoveToFront();
		_itemBtn0.GrabFocus();
	}

	private void RollItemChoices()
	{
		for (int i = 0; i < _currentChoiceRolls.Length; i++)
		{
			ChoiceRoll roll = new()
			{
				Kind = RollChoiceKind(),
				Rarity = RollRarity(_waveNum * 0.015f + _level * 0.01f),
				Modifiers = RollChoiceModifiers(RollRarity(_waveNum * 0.008f + _level * 0.006f)),
			};

			if (roll.Kind == LevelChoiceKind.Item)
			{
				roll.ItemId = ItemPool[_rng.RandiRange(0, ItemPool.Length - 1)];
			}
			else if (roll.Kind == LevelChoiceKind.Weapon)
			{
				roll.WeaponId = WeaponPool[_rng.RandiRange(0, WeaponPool.Length - 1)];
			}
			else
			{
				roll.ShapeId = ShapePool[_rng.RandiRange(0, ShapePool.Length - 1)];
			}

			_currentChoiceRolls[i] = roll;
		}
	}

	private string BuildChoiceButtonText(ChoiceRoll roll)
	{
		string rarity = GetRarityName(roll.Rarity);
		if (roll.Kind == LevelChoiceKind.Item)
		{
			int nextStack = GetItemStack(roll.ItemId) + 1;
			return $"[{rarity}] ITEM: {GetItemName(roll.ItemId)} x{nextStack} - {GetItemEffectText(roll.ItemId)}{BuildModifierSuffix(roll.Modifiers)}";
		}

		if (roll.Kind == LevelChoiceKind.Weapon)
		{
			return $"[{rarity}] WEAPON: {GetWeaponName(roll.WeaponId)} - reroll fire profile{BuildModifierSuffix(roll.Modifiers)}";
		}

		return $"[{rarity}] SHAPE: {GetShapeName(roll.ShapeId)} - reroll chassis role{BuildModifierSuffix(roll.Modifiers)}";
	}

	private void SelectLevelItem(int index)
	{
		if (index < 0 || index >= _currentChoiceRolls.Length)
		{
			return;
		}

		ApplyChoiceRoll(_currentChoiceRolls[index]);
		_pendingItemChoices = Mathf.Max(0, _pendingItemChoices - 1);

		if (_pendingItemChoices > 0)
		{
			OpenNextItemChoice();
		}
		else
		{
			_pauseForItemChoice = false;
			_levelUpPanel.Visible = false;
		}
	}

	private void ApplyChoiceRoll(ChoiceRoll roll)
	{
		for (int i = 0; i < roll.Modifiers.Length; i++)
		{
			_activeModifiers.Add(roll.Modifiers[i]);
		}

		if (roll.Kind == LevelChoiceKind.Item)
		{
			ApplyItem(roll.ItemId, roll.Rarity);
		}
		else if (roll.Kind == LevelChoiceKind.Weapon)
		{
			_weaponKind = roll.WeaponId;
			_weaponRarity = MaxRarity(_weaponRarity, roll.Rarity);
			_weaponCooldownSeconds = GetLegacyWeaponCooldown(_weaponKind);
		}
		else
		{
			_shapeKind = roll.ShapeId;
			_shapeRarity = MaxRarity(_shapeRarity, roll.Rarity);
		}

		ApplyDerivedPlayerStats(false);
	}

	private void ApplyItem(RunItemId item, RarityTier rarity)
	{
		if (!_itemStacks.TryGetValue(item, out int stack))
		{
			stack = 0;
		}

		_itemStacks[item] = stack + 1;
		if (!_itemRarityPower.TryGetValue(item, out float power))
		{
			power = 0f;
		}

		_itemRarityPower[item] = power + GetRarityMultiplier(rarity);

		float hpBefore = _player.Hp;
		ApplyDerivedPlayerStats(false);

		if (item == RunItemId.VitalCore)
		{
			_player.Hp = Mathf.Min(_player.MaxHp, hpBefore + _player.MaxHp * 0.28f);
		}
	}

	private void ApplyDerivedPlayerStats(bool keepHpRatio)
	{
		float hpRatio = _player.MaxHp > 0f ? _player.Hp / _player.MaxHp : 1f;

		float bootsPower = GetItemPower(RunItemId.RunnerBoots);
		float syringePower = GetItemPower(RunItemId.SoldierSyringe);
		float whetPower = GetItemPower(RunItemId.Whetstone);
		float vitalPower = GetItemPower(RunItemId.VitalCore);

		float moveMult = 1f + bootsPower * 0.10f;
		float fireMult = 1f + syringePower * 0.18f;
		float dmgMult = 1f + whetPower * 0.20f;
		float hpMult = 1f + vitalPower * 0.18f;

		float shapeTier = GetRarityMultiplier(_shapeRarity);
		float weaponTier = GetRarityMultiplier(_weaponRarity);

		ApplyShapeProfile(ref moveMult, ref dmgMult, ref hpMult, shapeTier);
		ApplyWeaponProfile(ref fireMult, ref dmgMult, ref moveMult, weaponTier);

		float modDmg = GetModifierTotal(ModifierKind.DamagePct);
		float modFire = GetModifierTotal(ModifierKind.FireRatePct);
		float modMove = GetModifierTotal(ModifierKind.MoveSpeedPct);
		float modHp = GetModifierTotal(ModifierKind.MaxHpPct);
		float modProj = GetModifierTotal(ModifierKind.ProjectileSizePct);

		moveMult *= 1f + modMove;
		fireMult *= 1f + modFire;
		dmgMult *= 1f + modDmg;
		hpMult *= 1f + modHp;
		moveMult *= 1f + _equipmentMoveSpeedPct / 100f;
		fireMult *= 1f + _equipmentAttackSpeedPct / 100f;
		dmgMult *= 1f + _equipmentDamagePct / 100f;
		hpMult *= 1f + _equipmentHealthPct / 100f;
		float weaponCooldown = Mathf.Max(0.05f, RuntimeMath.FiniteOr(_weaponCooldownSeconds, GetLegacyWeaponCooldown(_weaponKind)));

		_bulletRadiusScale = (1f + GetItemPower(RunItemId.MagnifyingLens) * 0.12f) * (1f + modProj) * GetEquipmentWeaponSizeMultiplier();
		_player.MoveSpeed = Mathf.Clamp(RuntimeMath.FiniteOr(BaseMoveSpeed * moveMult, BaseMoveSpeed), 1f, 240f);
		_player.FireRate = Mathf.Clamp(RuntimeMath.FiniteOr(fireMult / weaponCooldown, 1f / weaponCooldown), 0.05f, 120f);
		float weaponBaseDamage = _primaryWeaponDamage > 0f ? _primaryWeaponDamage : BaseBulletDamage;
		_player.BulletDamage = Mathf.Max(0f, RuntimeMath.FiniteOr(weaponBaseDamage * dmgMult, weaponBaseDamage));
		_player.BulletSpeed = Mathf.Clamp(
			RuntimeMath.FiniteOr(BaseBulletSpeed * GetWeaponSpeedMultiplier(_weaponKind, weaponTier) * GetEquipmentProjectileSpeedMultiplier(), BaseBulletSpeed),
			1f,
			600f);
		_player.BubbleRadius = _shapeKind == ShapeKind.BulwarkSphere ? 18f : (_shapeKind == ShapeKind.StrikerPyramid ? 14f : 15f);
		_player.ApplyShape(_shapeKind, _shapeRarity);

		float oldMax = _player.MaxHp;
		_player.MaxHp = Mathf.Max(1f, RuntimeMath.FiniteOr(BaseMaxHp * hpMult + _equipmentHealthFlat, BaseMaxHp));

		if (keepHpRatio)
		{
			_player.Hp = _player.MaxHp;
		}
		else if (oldMax > 0f)
		{
			_player.Hp = Mathf.Clamp(_player.MaxHp * hpRatio, 1f, _player.MaxHp);
		}
		else
		{
			_player.Hp = _player.MaxHp;
		}

		ApplyArenaDebugOverridesToPlayer();
		RefreshEquipmentPlayerDefenses();
	}

	private int GetItemStack(RunItemId item)
	{
		return _itemStacks.TryGetValue(item, out int stack) ? stack : 0;
	}

	private float GetItemPower(RunItemId item)
	{
		return _itemRarityPower.TryGetValue(item, out float power) ? power : 0f;
	}

	private static string GetItemName(RunItemId item)
	{
		return item switch
		{
			RunItemId.SoldierSyringe => "Soldier Syringe",
			RunItemId.Whetstone => "Whetstone",
			RunItemId.MagnifyingLens => "Magnifying Lens",
			RunItemId.RunnerBoots => "Runner Boots",
			RunItemId.VitalCore => "Vital Core",
			RunItemId.SplitShot => "Split Shot",
			_ => "Unknown",
		};
	}

	private static string GetItemShortName(RunItemId item)
	{
		return item switch
		{
			RunItemId.SoldierSyringe => "Syringe",
			RunItemId.Whetstone => "Whetstone",
			RunItemId.MagnifyingLens => "Lens",
			RunItemId.RunnerBoots => "Boots",
			RunItemId.VitalCore => "Vital",
			RunItemId.SplitShot => "Split",
			_ => "Item",
		};
	}

	private static string GetArenaLoadoutSlotLabel(int slotIndex)
	{
		return slotIndex == 0 ? "Primary" : $"Support {slotIndex}";
	}

	private static string GetItemEffectText(RunItemId item)
	{
		return item switch
		{
			RunItemId.SoldierSyringe => "+18% fire rate",
			RunItemId.Whetstone => "+20% damage",
			RunItemId.MagnifyingLens => "+12% projectile size",
			RunItemId.RunnerBoots => "+10% move speed",
			RunItemId.VitalCore => "+18% max HP + heal",
			RunItemId.SplitShot => "+2 side projectiles",
			_ => "",
		};
	}

	private static string GetWeaponName(WeaponKind kind)
	{
		return kind switch
		{
			WeaponKind.Square => "Square",
			WeaponKind.Circle => "Circle",
			WeaponKind.Triangle => "Triangle",
			WeaponKind.Hexagon => "Hexagon",
			WeaponKind.Sphere => "Sphere",
			WeaponKind.Cube => "Cube",
			WeaponKind.Pyramid => "Pyramid",
			WeaponKind.Cone => "Cone",
			WeaponKind.Tesseract => "Tesseract",
			WeaponKind.Hyperphere => "Hyperphere",
			WeaponKind.Pentachoron => "Pentachoron",
			WeaponKind.Hypercone => "Hypercone",
			WeaponKind.ImpossibleCube => "Impossible Cube",
			WeaponKind.Mobius => "Möbius Strip",
			WeaponKind.Klein => "Klein Bottle",
			WeaponKind.BoysSurface => "Boy's Surface",
			_ => "Unknown",
		};
	}

	private static string GetShapeName(ShapeKind kind)
	{
		return kind switch
		{
			ShapeKind.StrikerPyramid => "Striker Pyramid",
			ShapeKind.BulwarkSphere => "Bulwark Sphere",
			_ => "Core Cube",
		};
	}

	private LevelChoiceKind RollChoiceKind()
	{
		float r = _rng.Randf();
		if (r < 0.60f)
		{
			return LevelChoiceKind.Item;
		}

		if (r < 0.80f)
		{
			return LevelChoiceKind.Shape;
		}

		return LevelChoiceKind.Weapon;
	}

	private RarityTier RollRarity(float luckBias)
	{
		float luck = Mathf.Clamp(luckBias, 0f, 1.2f);
		float mythic = 0.004f + luck * 0.003f;
		float legendary = 0.018f + luck * 0.010f;
		float epic = 0.070f + luck * 0.030f;
		float rare = 0.170f + luck * 0.050f;
		float uncommon = 0.330f + luck * 0.060f;

		float r = _rng.Randf();
		if (r < mythic) return RarityTier.Mythic;
		r -= mythic;
		if (r < legendary) return RarityTier.Legendary;
		r -= legendary;
		if (r < epic) return RarityTier.Epic;
		r -= epic;
		if (r < rare) return RarityTier.Rare;
		r -= rare;
		if (r < uncommon) return RarityTier.Uncommon;
		return RarityTier.Common;
	}

	private static float GetRarityMultiplier(RarityTier rarity)
	{
		return rarity switch
		{
			RarityTier.Uncommon => 1.16f,
			RarityTier.Rare => 1.35f,
			RarityTier.Epic => 1.60f,
			RarityTier.Legendary => 1.95f,
			RarityTier.Mythic => 2.35f,
			_ => 1f,
		};
	}

	private static string GetRarityName(RarityTier rarity)
	{
		return rarity switch
		{
			RarityTier.Uncommon => "UNCOMMON",
			RarityTier.Rare => "RARE",
			RarityTier.Epic => "EPIC",
			RarityTier.Legendary => "LEGENDARY",
			RarityTier.Mythic => "MYTHIC",
			_ => "COMMON",
		};
	}

	private ModifierRoll[] RollChoiceModifiers(RarityTier rarity)
	{
		int count = rarity switch
		{
			RarityTier.Common => _rng.Randf() < 0.12f ? 1 : 0,
			RarityTier.Uncommon => _rng.Randf() < 0.30f ? 1 : 0,
			RarityTier.Rare => 1,
			RarityTier.Epic => 1 + (_rng.Randf() < 0.45f ? 1 : 0),
			RarityTier.Legendary => 2,
			RarityTier.Mythic => 3,
			_ => 0,
		};

		if (count <= 0)
		{
			return Array.Empty<ModifierRoll>();
		}

		List<ModifierRoll> rolls = new();
		HashSet<ModifierKind> used = new();
		for (int i = 0; i < count; i++)
		{
			ModifierKind kind = (ModifierKind)_rng.RandiRange(0, (int)ModifierKind.ExtraSplitShot);
			int guard = 0;
			while (used.Contains(kind) && guard < 20)
			{
				kind = (ModifierKind)_rng.RandiRange(0, (int)ModifierKind.ExtraSplitShot);
				guard++;
			}
			used.Add(kind);

			float tier = GetRarityMultiplier(rarity);
			float value = kind switch
			{
				ModifierKind.ExtraSplitShot => rarity >= RarityTier.Epic ? 1f : 0f,
				ModifierKind.DamagePct => 0.03f * tier,
				ModifierKind.FireRatePct => 0.03f * tier,
				ModifierKind.MoveSpeedPct => 0.025f * tier,
				ModifierKind.MaxHpPct => 0.04f * tier,
				ModifierKind.ProjectileSizePct => 0.035f * tier,
				ModifierKind.XpGainPct => 0.05f * tier,
				ModifierKind.CoinGainPct => 0.06f * tier,
				ModifierKind.BubbleStrengthPct => 0.08f * tier,
				_ => 0f,
			};

			if (value > 0f)
			{
				rolls.Add(new ModifierRoll(kind, value, rarity));
			}
		}

		return rolls.ToArray();
	}

	private string BuildModifierSuffix(ModifierRoll[] mods)
	{
		if (mods.Length == 0)
		{
			return "";
		}

		List<string> parts = new();
		int max = Mathf.Min(2, mods.Length);
		for (int i = 0; i < max; i++)
		{
			parts.Add(FormatModifier(mods[i]));
		}

		if (mods.Length > max)
		{
			parts.Add("+");
		}

		return " | " + string.Join(", ", parts);
	}

	private static string FormatModifier(ModifierRoll mod)
	{
		if (mod.Kind == ModifierKind.ExtraSplitShot)
		{
			return "+1 Split";
		}

		return mod.Kind switch
		{
			ModifierKind.DamagePct => $"+{mod.Value * 100f:0}% Dmg",
			ModifierKind.FireRatePct => $"+{mod.Value * 100f:0}% Fire",
			ModifierKind.MoveSpeedPct => $"+{mod.Value * 100f:0}% Move",
			ModifierKind.MaxHpPct => $"+{mod.Value * 100f:0}% HP",
			ModifierKind.ProjectileSizePct => $"+{mod.Value * 100f:0}% Size",
			ModifierKind.XpGainPct => $"+{mod.Value * 100f:0}% XP",
			ModifierKind.CoinGainPct => $"+{mod.Value * 100f:0}% Coin",
			ModifierKind.BubbleStrengthPct => $"+{mod.Value * 100f:0}% Bubble",
			_ => "",
		};
	}

	private static RarityTier MaxRarity(RarityTier a, RarityTier b)
	{
		return (int)b > (int)a ? b : a;
	}

	private float GetModifierTotal(ModifierKind kind)
	{
		float v = 0f;
		for (int i = 0; i < _activeModifiers.Count; i++)
		{
			if (_activeModifiers[i].Kind == kind)
			{
				v += _activeModifiers[i].Value;
			}
		}

		return v;
	}

	private int GetModifierFlat(ModifierKind kind)
	{
		int v = 0;
		for (int i = 0; i < _activeModifiers.Count; i++)
		{
			if (_activeModifiers[i].Kind == kind)
			{
				v += Mathf.RoundToInt(_activeModifiers[i].Value);
			}
		}

		return v;
	}

	private void ApplyShapeProfile(ref float moveMult, ref float dmgMult, ref float hpMult, float shapeTier)
	{
		switch (_shapeKind)
		{
			case ShapeKind.StrikerPyramid:
				moveMult *= 1.08f;
				dmgMult *= 1.12f;
				hpMult *= 0.92f;
				break;
			case ShapeKind.BulwarkSphere:
				moveMult *= 0.92f;
				dmgMult *= 0.90f;
				hpMult *= 1.20f;
				break;
			default:
				break;
		}

		float tierBoost = 1f + (shapeTier - 1f) * 0.55f;
		hpMult *= tierBoost;
		moveMult *= 1f + (shapeTier - 1f) * 0.18f;
		dmgMult *= 1f + (shapeTier - 1f) * 0.22f;
	}

	private void ApplyWeaponProfile(ref float fireMult, ref float dmgMult, ref float moveMult, float weaponTier)
	{
		fireMult *= GetWeaponBaseFireProfileMultiplier(_weaponKind);

		switch (_weaponKind)
		{
			case WeaponKind.Circle:
				dmgMult *= 1.12f;
				break;
			case WeaponKind.Triangle:
				dmgMult *= 1.55f;
				moveMult *= 0.98f;
				break;
			case WeaponKind.Hexagon:
				dmgMult *= 1.60f;
				break;
			case WeaponKind.Sphere:
				dmgMult *= 1.85f;
				moveMult *= 0.97f;
				break;
			case WeaponKind.Cube:
				dmgMult *= 1.25f;
				break;
			case WeaponKind.Pyramid:
				dmgMult *= 1.10f;
				moveMult *= 1.03f;
				break;
			case WeaponKind.Cone:
				dmgMult *= 1.35f;
				break;
			case WeaponKind.Tesseract:
				dmgMult *= 2.00f;
				moveMult *= 0.96f;
				break;
			case WeaponKind.Hyperphere:
				dmgMult *= 1.75f;
				break;
			case WeaponKind.Pentachoron:
				dmgMult *= 1.45f;
				break;
			case WeaponKind.Hypercone:
				dmgMult *= 1.65f;
				break;
			case WeaponKind.ImpossibleCube:
				dmgMult *= 2.30f;
				moveMult *= 0.94f;
				break;
			case WeaponKind.Mobius:
				dmgMult *= 1.05f;
				moveMult *= 1.05f;
				break;
			case WeaponKind.Klein:
				dmgMult *= 1.90f;
				break;
			case WeaponKind.BoysSurface:
				dmgMult *= 1.80f;
				moveMult *= 1.02f;
				break;
			default:
				break;
		}

		float tierBoost = GetWeaponFireTierMultiplier(weaponTier);
		fireMult *= tierBoost;
		dmgMult *= tierBoost;
	}

	private static float GetWeaponBaseFireProfileMultiplier(WeaponKind kind)
	{
		return kind switch
		{
			WeaponKind.Circle => 0.72f,
			WeaponKind.Triangle => 0.65f,
			WeaponKind.Hexagon => 0.55f,
			WeaponKind.Sphere => 0.50f,
			WeaponKind.Cube => 0.65f,
			WeaponKind.Pyramid => 0.90f,
			WeaponKind.Cone => 0.78f,
			WeaponKind.Tesseract => 0.48f,
			WeaponKind.Hyperphere => 0.55f,
			WeaponKind.Pentachoron => 0.68f,
			WeaponKind.Hypercone => 0.60f,
			WeaponKind.ImpossibleCube => 0.42f,
			WeaponKind.Mobius => 0.78f,
			WeaponKind.Klein => 0.50f,
			WeaponKind.BoysSurface => 0.58f,
			_ => 1f,
		};
	}

	private static float GetWeaponFireTierMultiplier(float weaponTier)
	{
		return 1f + (weaponTier - 1f) * 0.52f;
	}

	private float GetWeaponSpeedMultiplier(WeaponKind kind, float tier)
	{
		float baseMult = kind switch
		{
			WeaponKind.Circle => 0.78f,
			WeaponKind.Triangle => 1.18f,
			WeaponKind.Hexagon => 0.68f,
			WeaponKind.Sphere => 0.62f,
			WeaponKind.Cube => 0.84f,
			WeaponKind.Pyramid => 1.10f,
			WeaponKind.Cone => 0.92f,
			WeaponKind.Tesseract => 0.72f,
			WeaponKind.Hyperphere => 0.82f,
			WeaponKind.Pentachoron => 1.22f,
			WeaponKind.Hypercone => 0.86f,
			WeaponKind.ImpossibleCube => 0.58f,
			WeaponKind.Mobius => 1.05f,
			WeaponKind.Klein => 0.70f,
			WeaponKind.BoysSurface => 0.76f,
			_ => 1f,
		};

		return baseMult * (1f + (tier - 1f) * 0.30f);
	}

	private string BuildItemSummaryText()
	{
		List<string> parts = new();
		parts.Add($"Weapon: {GetWeaponName(_weaponKind)} {GetRarityName(_weaponRarity)}");
		parts.Add($"Shape: {GetShapeName(_shapeKind)} {GetRarityName(_shapeRarity)}");
		string equipmentSummary = BuildEquipmentModifierSummaryText();
		if (!string.IsNullOrWhiteSpace(equipmentSummary))
		{
			parts.Add(equipmentSummary);
		}
		foreach (RunItemId item in ItemPool)
		{
			int stack = GetItemStack(item);
			if (stack > 0)
			{
				parts.Add($"{GetItemShortName(item)} x{stack}");
			}
		}

		if (_activeModifiers.Count > 0)
		{
			parts.Add($"Mods {_activeModifiers.Count}");
		}

		return string.Join(" | ", parts);
	}

	private bool IsPointBlockedByPillar(Vector2 point, float expand)
	{
		return IsPointBlockedByPillar(point, expand, false);
	}

	private bool IsPointBlockedByPillar(Vector2 point, float expand, bool ignoreCubeCrushable)
	{
		for (int i = 0; i < _pillars.Count; i++)
		{
			if (ignoreCubeCrushable && IsCubeCrushablePillar(i))
			{
				continue;
			}

			if (ExpandRect(_pillars[i], expand).HasPoint(point))
			{
				return true;
			}
		}

		return false;
	}

	private bool IsBlockedByPillar(Vector2 from, Vector2 to)
	{
		return IsBlockedByPillar(from, to, false);
	}

	private bool IsBlockedByPillar(Vector2 from, Vector2 to, bool ignoreCubeCrushable)
	{
		for (int i = 0; i < _pillars.Count; i++)
		{
			if (ignoreCubeCrushable && IsCubeCrushablePillar(i))
			{
				continue;
			}

			if (SegmentIntersectsExpandedAabb(from, to, _pillars[i], 10f, out _))
			{
				return true;
			}
		}

		return false;
	}

	private float RaycastPillarDistance(Vector2 from, float ang, float maxLen, float expand)
	{
		return RaycastPillarDistance(from, ang, maxLen, expand, false);
	}

	private float RaycastPillarDistance(Vector2 from, float ang, float maxLen, float expand, bool ignoreCubeCrushable)
	{
		Vector2 dir = AngleToDir(ang);
		Vector2 to = from + dir * maxLen;
		float hitT = 1f;

		for (int i = 0; i < _pillars.Count; i++)
		{
			if (ignoreCubeCrushable && IsCubeCrushablePillar(i))
			{
				continue;
			}

			if (SegmentIntersectsExpandedAabb(from, to, _pillars[i], expand, out float tEnter))
			{
				hitT = Mathf.Min(hitT, tEnter);
			}
		}

		return maxLen * hitT;
	}

	private Vector2 ResolvePillarCollision(Vector2 pos, float radius)
	{
		return ResolvePillarCollision(pos, radius, false);
	}

	private Vector2 ResolvePillarCollision(Vector2 pos, float radius, bool ignoreCubeCrushable)
	{
		for (int i = 0; i < _pillars.Count; i++)
		{
			if (ignoreCubeCrushable && IsCubeCrushablePillar(i))
			{
				continue;
			}

			Rect2 expanded = ExpandRect(_pillars[i], radius);
			if (!expanded.HasPoint(pos))
			{
				continue;
			}

			float minX = expanded.Position.X;
			float minY = expanded.Position.Y;
			float maxX = expanded.Position.X + expanded.Size.X;
			float maxY = expanded.Position.Y + expanded.Size.Y;

			float pushL = pos.X - minX;
			float pushR = maxX - pos.X;
			float pushT = pos.Y - minY;
			float pushB = maxY - pos.Y;

			float minPush = Mathf.Min(Mathf.Min(pushL, pushR), Mathf.Min(pushT, pushB));
			if (minPush == pushL)
			{
				pos.X = minX;
			}
			else if (minPush == pushR)
			{
				pos.X = maxX;
			}
			else if (minPush == pushT)
			{
				pos.Y = minY;
			}
			else
			{
				pos.Y = maxY;
			}
		}

		return pos;
	}

	private static Rect2 ExpandRect(Rect2 rect, float amount)
	{
		return new Rect2(
			new Vector2(rect.Position.X - amount, rect.Position.Y - amount),
			new Vector2(rect.Size.X + amount * 2f, rect.Size.Y + amount * 2f));
	}

	private static bool SegmentIntersectsExpandedAabb(Vector2 from, Vector2 to, Rect2 rect, float expand, out float tEnter)
	{
		tEnter = 0f;
		Rect2 expanded = ExpandRect(rect, expand);

		float minX = expanded.Position.X;
		float minY = expanded.Position.Y;
		float maxX = expanded.Position.X + expanded.Size.X;
		float maxY = expanded.Position.Y + expanded.Size.Y;

		Vector2 d = to - from;
		float tMin = 0f;
		float tMax = 1f;

		if (Mathf.Abs(d.X) < 0.0001f)
		{
			if (from.X < minX || from.X > maxX)
			{
				return false;
			}
		}
		else
		{
			float invDx = 1f / d.X;
			float tx1 = (minX - from.X) * invDx;
			float tx2 = (maxX - from.X) * invDx;
			if (tx1 > tx2)
			{
				(tx1, tx2) = (tx2, tx1);
			}

			tMin = Mathf.Max(tMin, tx1);
			tMax = Mathf.Min(tMax, tx2);
			if (tMin > tMax)
			{
				return false;
			}
		}

		if (Mathf.Abs(d.Y) < 0.0001f)
		{
			if (from.Y < minY || from.Y > maxY)
			{
				return false;
			}
		}
		else
		{
			float invDy = 1f / d.Y;
			float ty1 = (minY - from.Y) * invDy;
			float ty2 = (maxY - from.Y) * invDy;
			if (ty1 > ty2)
			{
				(ty1, ty2) = (ty2, ty1);
			}

			tMin = Mathf.Max(tMin, ty1);
			tMax = Mathf.Min(tMax, ty2);
			if (tMin > tMax)
			{
				return false;
			}
		}

		tEnter = tMin;
		return true;
	}

	private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float lenSq = ab.LengthSquared();
		if (lenSq <= 0.0001f)
		{
			return (p - a).Length();
		}

		float t = Mathf.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f);
		Vector2 proj = a + ab * t;
		return (p - proj).Length();
	}

	private static Vector2 AngleToDir(float ang) => new(Mathf.Cos(ang), Mathf.Sin(ang));

	private static BossKind GetBossKindForWave(int wave)
	{
		int bossIndex = ((wave / BossWaveInterval) - 1) % 3;
		return bossIndex switch
		{
			1 => BossKind.Overseer,
			2 => BossKind.Cube,
			_ => BossKind.Monolith,
		};
	}

	private static string GetBossDisplayName(BossKind kind)
	{
		return kind switch
		{
			BossKind.Overseer => "THE OVERSEER",
			BossKind.Cube => "THE CUBE",
			_ => "THE MONOLITH",
		};
	}

	private static Color GetBossHudColor(BossKind kind)
	{
		return kind switch
		{
			BossKind.Overseer => new Color(0.48f, 0.92f, 1f, 1f),
			BossKind.Cube => new Color(0.96f, 0.72f, 0.2f, 1f),
			_ => new Color(0.72f, 0.32f, 0.92f, 1f),
		};
	}

	private static Color GetBossWaypointColor(BossKind kind)
	{
		return kind switch
		{
			BossKind.Overseer => new Color(0f, 0.9f, 1f, 1f),
			BossKind.Cube => new Color(1f, 0.78f, 0.28f, 1f),
			_ => new Color(0.75f, 0.52f, 0.98f, 1f),
		};
	}

	public ArenaDebugStateSnapshot GetArenaDebugState()
	{
		if (!IsInstanceValid(_player))
		{
			return ArenaDebugStateSnapshot.Empty;
		}

		int extraProjectiles = (_arenaDebugOverridesActive
			? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponProjectileCountFlat))
			: 0)
			+ Mathf.Max(0, Mathf.RoundToInt(_equipmentProjectileCountFlat));
		int splitProjectiles = Mathf.FloorToInt(GetItemPower(RunItemId.SplitShot)) + GetModifierFlat(ModifierKind.ExtraSplitShot);
		int projectileCount = Mathf.Max(1, 1 + extraProjectiles + splitProjectiles);

		int ricochet = Mathf.Max(
			0,
			Mathf.RoundToInt(_equipmentRicochetFlat)
				+ (_arenaDebugOverridesActive ? Mathf.RoundToInt(_arenaDebugOverrides.WeaponRicochetFlat) : 0));
		int bounce = Mathf.Max(
			0,
			Mathf.RoundToInt(_equipmentBounceTimesFlat)
				+ (_arenaDebugOverridesActive ? Mathf.RoundToInt(_arenaDebugOverrides.WeaponBounceTimesFlat) : 0));

		float moveSpeedPercent = _player.MoveSpeed / Mathf.Max(1f, BaseMoveSpeed) * 100f;
		float weaponSizePercent = Mathf.Max(0f, _bulletRadiusScale) * 100f;

		return new ArenaDebugStateSnapshot(
			hasRuntime: true,
			overridesActive: _arenaDebugOverridesActive,
			overrides: _arenaDebugOverrides,
			moveSpeedPercent: moveSpeedPercent,
			maxHp: _player.MaxHp,
			dodgeChancePercent: GetTotalDodgeChancePercent(),
			lifeStealPercent: GetTotalLifeStealPercent(),
			healthRegenPerSecond: GetTotalRegenPerSecond(),
			critDamagePercent: GetDisplayedCritDamagePercent(),
			critChancePercent: GetTotalCritChancePercent(),
			weaponRange: GetCurrentWeaponTargetRange() * RangeDisplayScale,
			weaponSizePercent: weaponSizePercent,
			weaponAttackSpeed: _player.FireRate,
			weaponDamage: _player.BulletDamage,
			weaponRicochet: ricochet,
			weaponBounceTimes: bounce,
			weaponProjectileCount: projectileCount);
	}

	public void ApplyLiveOverrides(ArenaDebugOverrides overrides)
	{
		_arenaDebugOverrides = overrides;
		_arenaDebugOverridesActive = true;
		ApplyDerivedPlayerStats(false);
	}

	public void ResetLiveOverrides()
	{
		_arenaDebugOverrides = default;
		_arenaDebugOverridesActive = false;
		ApplyDerivedPlayerStats(false);
	}

	public ArenaDebugLoadoutSlotSnapshot GetArenaLoadoutSlotSnapshot(int slotIndex)
	{
		if (!IsArenaLoadoutSlotIndexValid(slotIndex))
		{
			return ArenaDebugLoadoutSlotSnapshot.Empty;
		}

		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		Godot.Collections.Array<string> installedModulators = new();
		if (item != null)
		{
			for (int i = 0; i < item.Modulators.Count; i++)
			{
				WeaponModulatorData modulator = item.Modulators[i];
				string tierPrefix = modulator.UsesTierPresentation ? $"{modulator.Tier} " : string.Empty;
				installedModulators.Add($"{i + 1}. {tierPrefix}{modulator.DisplayName}");
			}
		}

		return new ArenaDebugLoadoutSlotSnapshot(
			hasRuntime: true,
			slotIndex: slotIndex,
			hasItem: item != null,
			slotLabel: GetArenaLoadoutSlotLabel(slotIndex),
			itemLabel: item == null
				? "Empty"
				: $"{item.Shape?.DisplayName ?? "Weapon"} | {item.Rarity?.DisplayName ?? "Common"} | Lv {item.Level}",
			shapeKey: item?.Shape?.Key ?? string.Empty,
			rarityKey: item?.Rarity?.Key ?? string.Empty,
			level: item?.Level ?? 0,
			unlockedModulatorSlots: item?.UnlockedModulatorSlots ?? 0,
			availableModulatorSlots: item?.AvailableModulatorSlots ?? 0,
			installedModulatorLabels: installedModulators);
	}

	public bool TrySetArenaDebugLoadoutItem(int slotIndex, string shapeKey, string rarityKey, int level)
	{
		if (!IsArenaLoadoutSlotIndexValid(slotIndex) || string.IsNullOrWhiteSpace(shapeKey))
		{
			return false;
		}

		ShapeData shape = EquipmentCatalogDefaults.CreateShapeByKey(shapeKey);
		RarityData rarity = EquipmentCatalogDefaults.CreateRarityByKey(rarityKey);
		ModifierData normalEssence = EquipmentCatalogDefaults.CreateModifierByKey(ModifierKey.Normal, includeBaselineNormal: true);

		StatGenerator generator = new(_rng);
		EquipmentItem item = generator.BuildItem(shape, rarity, normalEssence, StatGenerator.ClampWeaponLevel(level));
		item.Essence = null;
		item.WeaponXpProgress = 0f;
		EnsureRunLoadoutItemCoreData(item);

		SetRunLoadoutSlotItem(slotIndex, item);
		SyncArenaLoadoutAfterDebugRoomEdit();
		return true;
	}

	public bool TryClearArenaDebugLoadoutSlot(int slotIndex)
	{
		if (!IsArenaLoadoutSlotIndexValid(slotIndex))
		{
			return false;
		}

		if (_equippedLoadout[slotIndex] == null)
		{
			return false;
		}

		SetRunLoadoutSlotItem(slotIndex, null);
		SyncArenaLoadoutAfterDebugRoomEdit();
		return true;
	}

	public bool TryAddArenaDebugModulator(int slotIndex, string modulatorKey, string tierKey)
	{
		if (!TryCreateDebugLabModulator(modulatorKey, tierKey, out WeaponModulatorData modulator))
		{
			return false;
		}

		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || item.AvailableModulatorSlots <= 0 || !CanInstallRunModulator(item, modulator))
		{
			return false;
		}

		item.Modulators.Add(modulator);
		EnsureRunLoadoutItemCoreData(item);
		SyncArenaLoadoutAfterDebugRoomEdit();
		return true;
	}

	public bool TryReplaceArenaDebugModulator(int slotIndex, int modulatorIndex, string modulatorKey, string tierKey)
	{
		if (!TryCreateDebugLabModulator(modulatorKey, tierKey, out WeaponModulatorData modulator))
		{
			return false;
		}

		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count)
		{
			return false;
		}

		if (!CanInstallRunModulator(item, modulator, modulatorIndex))
		{
			return false;
		}

		item.Modulators[modulatorIndex] = modulator;
		EnsureRunLoadoutItemCoreData(item);
		SyncArenaLoadoutAfterDebugRoomEdit();
		return true;
	}

	public bool TryRemoveArenaDebugModulator(int slotIndex, int modulatorIndex)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count)
		{
			return false;
		}

		item.Modulators.RemoveAt(modulatorIndex);
		EnsureRunLoadoutItemCoreData(item);
		SyncArenaLoadoutAfterDebugRoomEdit();
		return true;
	}

	public void SpawnEnemy(EnemyKind kind, int count, float hpMultiplier)
	{
		int spawnCount = Mathf.Max(1, count);
		for (int i = 0; i < spawnCount; i++)
		{
			SpawnDebugEnemy(kind, hpMultiplier);
		}
	}

	public void SpawnEnemies(int count, float hpMultiplier)
	{
		SpawnEnemy(EnemyKind.Grunt, count, hpMultiplier);
	}

	public void SpawnInterceptor(int count, float hpMultiplier)
	{
		SpawnEnemy(EnemyKind.Interceptor, count, hpMultiplier);
	}

	public void KillAllEnemies()
	{
		for (int i = _enemies.Count - 1; i >= 0; i--)
		{
			EnemyActor enemy = _enemies[i];
			if (IsInstanceValid(enemy))
			{
				enemy.QueueFree();
			}
		}

		_enemies.Clear();
	}

	public void SpawnMonolith()
	{
		ForceSpawnBoss(BossKind.Monolith);
	}

	public void SpawnOverseer()
	{
		ForceSpawnBoss(BossKind.Overseer);
	}

	public void SpawnCube()
	{
		ForceSpawnBoss(BossKind.Cube);
	}

	private void SpawnDebugEnemy(EnemyKind kind, float hpMultiplier)
	{
		Vector2 pos = GetEnemySpawnPosition();

		EnemyActor enemy = new();
		AddChild(enemy);
		enemy.Configure(kind, _enemyHpScale * Mathf.Max(0.1f, hpMultiplier), _enemyDmgScale, pos, _player.Position);
		_enemies.Add(enemy);
	}

	private bool IsArenaLoadoutSlotIndexValid(int slotIndex)
	{
		return slotIndex >= 0 && slotIndex < _equippedLoadout.Length;
	}

	private void SetRunLoadoutSlotItem(int slotIndex, EquipmentItem? item)
	{
		if (!IsArenaLoadoutSlotIndexValid(slotIndex))
		{
			return;
		}

		if (item != null)
		{
			EnsureRunLoadoutItemCoreData(item);
		}

		_equippedLoadout[slotIndex] = item;
		_runLoadoutSourceInstanceIds[slotIndex] = null;
	}

	private static bool TryCreateDebugLabModulator(string modulatorKey, string tierKey, out WeaponModulatorData modulator)
	{
		modulator = null!;
		if (string.IsNullOrWhiteSpace(modulatorKey))
		{
			return false;
		}

		WeaponModulatorTier tier = WeaponModulatorTier.Baseline;
		if (!string.IsNullOrWhiteSpace(tierKey))
		{
			_ = WeaponModulatorData.TryParseTierKey(tierKey, out tier);
		}

		return EquipmentCatalogDefaults.TryCreateModulatorByKey(modulatorKey, tier, out modulator);
	}

	private void SyncArenaLoadoutAfterDebugRoomEdit()
	{
		RefreshEquipmentLoadout();
		ApplyEquippedPrimaryToRun();
		ApplyDerivedPlayerStats(false);
		UpdateArenaPauseOverlayState();
	}

	private void ApplyArenaDebugOverridesToPlayer()
	{
		if (!_arenaDebugOverridesActive)
		{
			return;
		}

		float hpRatio = _player.MaxHp > 0f ? _player.Hp / _player.MaxHp : 1f;

		_player.MoveSpeed *= 1f + _arenaDebugOverrides.MoveSpeedPercent / 100f;
		_player.FireRate *= 1f + _arenaDebugOverrides.WeaponAttackSpeedPercent / 100f;
		_player.BulletDamage *= 1f + _arenaDebugOverrides.WeaponDamagePercentBonus / 100f;
		_bulletRadiusScale *= 1f + _arenaDebugOverrides.WeaponSizePercent / 100f;

		_player.MaxHp = Mathf.Max(1f, _player.MaxHp + _arenaDebugOverrides.MaxHpFlat);
		_player.Hp = Mathf.Clamp(_player.MaxHp * hpRatio, 1f, _player.MaxHp);
	}

	private float GetDisplayedCritDamagePercent()
	{
		return GetCritDamageMultiplier(GetTotalCritDamagePercent()) * 100f;
	}

	private void ApplyArenaDebugRegen(float dt)
	{
		if (!_arenaDebugOverridesActive)
		{
			return;
		}

		if (_arenaDebugOverrides.HealthRegenPerSecond <= 0f)
		{
			return;
		}

		_player.Hp = Mathf.Min(_player.MaxHp, _player.Hp + _arenaDebugOverrides.HealthRegenPerSecond * dt);
	}

	private void ApplyArenaDebugLifeSteal(float dealtDamage)
	{
		if (!_arenaDebugOverridesActive)
		{
			return;
		}

		if (_arenaDebugOverrides.LifeStealPercent <= 0f || dealtDamage <= 0f)
		{
			return;
		}

		float heal = dealtDamage * (_arenaDebugOverrides.LifeStealPercent / 100f);
		_player.Hp = Mathf.Min(_player.MaxHp, _player.Hp + heal);
	}

	public ArenaRunOutcome GetRunOutcomeForHub()
	{
		PersistRunLoadoutWeaponProgress();

		int levelsEarned = 0;
		int coinsEarned = Mathf.Max(0, _coins);
		int kills = Mathf.Max(0, _kills);
		int highestWave = Mathf.Max(1, _waveNum);

		return new ArenaRunOutcome(coinsEarned, levelsEarned, kills, highestWave);
	}

	private static float WrapAngle(float ang)
	{
		while (ang > Mathf.Pi) ang -= Mathf.Tau;
		while (ang < -Mathf.Pi) ang += Mathf.Tau;
		return ang;
	}
}
