using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public enum FpsEnemyRole
{
	Bruiser,
	Striker,
}

public partial class FpsSliceEnemy : CharacterBody3D, IFpsCombatEnemy
{
	private const float GravityStrength = 16f;

	[Export] public FpsEnemyRole Role = FpsEnemyRole.Bruiser;
	[Export] public FpsEnemyArchetypeSettings? BruiserSettings { get; set; }
	[Export] public FpsEnemyArchetypeSettings? StrikerSettings { get; set; }

	public event Action<FpsSliceEnemy>? Died;

	public bool IsAlive => _currentHealth > 0.001f;
	public float CurrentHealth => _currentHealth;
	public float MaxHealth => _maxHealth;
	public Vector3 HeadPosition => GlobalPosition + new Vector3(0f, _headHeight, 0f);
	public float CollisionRadius { get; private set; }
	public float CollisionHeight => Mathf.Max(CollisionRadius * 2f, _collisionHeight);
	public Vector3 CollisionCenter => GlobalPosition + Vector3.Up * 0.95f;
	public bool IsValid => GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion();
	FpsEnemyRole IFpsCombatEnemy.Role => Role;

	private FpsPlayerController? _player;
	private FpsVerticalSlice? _director;
	private MeshInstance3D _bodyMesh = null!;
	private MeshInstance3D _coreMesh = null!;
	private StandardMaterial3D _bodyMaterial = null!;
	private StandardMaterial3D _coreMaterial = null!;
	private readonly RandomNumberGenerator _rng = new();

	private bool _combatEnabled = true;
	private float _maxHealth;
	private float _currentHealth;
	private float _moveSpeed;
	private float _attackRange;
	private float _attackDamage;
	private float _attackCooldownSeconds;
	private float _attackCooldown;
	private float _projectileDamage;
	private float _projectileSpeed;
	private float _desiredRange;
	private float _collisionHeight;
	private float _headHeight;
	private Color _baseBodyColor = new(0.82f, 0.28f, 0.22f, 1f);
	private Color _baseCoreColor = new(1f, 0.62f, 0.20f, 1f);
	private float _flashTimer;
	private float _strafeTimer;
	private float _strafeDirection = 1f;
	private float _waveHealthMultiplier = 1f;
	private float _waveDamageMultiplier = 1f;
	private float _waveMoveSpeedMultiplier = 1f;
	private float _waveAttackCadenceMultiplier = 1f;
	private bool _targetingDefenseCore;
	private Vector3 _targetPosition;

	public override void _Ready()
	{
		_rng.Randomize();
		EnsureArchetypeDefaults();
		ApplyArchetypeSettings(resetHealth: true);
		BuildVisuals();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_combatEnabled || !IsAlive || _player == null || _director == null)
		{
			Velocity = Vector3.Zero;
			return;
		}

		float dt = (float)delta;
		if (_attackCooldown > 0f)
		{
			_attackCooldown -= dt;
		}

		_targetPosition = _player.HeadPosition;
		_targetingDefenseCore = false;
		if (_director.TryGetDefenseTarget(GlobalPosition, out Vector3 defenseTarget, out bool targetIsCore))
		{
			_targetPosition = defenseTarget;
			_targetingDefenseCore = targetIsCore;
		}
		Vector3 toPlayer = _targetPosition - GlobalPosition;
		toPlayer.Y = 0f;
		float distance = Mathf.Max(0.001f, toPlayer.Length());
		Vector3 direction = toPlayer / distance;

		Vector3 moveVelocity = Role == FpsEnemyRole.Bruiser
			? TickBruiser(direction, distance)
			: TickStriker(direction, distance, dt);

		float verticalVelocity = IsOnFloor() ? 0f : Velocity.Y - (GravityStrength * dt);
		Velocity = new Vector3(moveVelocity.X, verticalVelocity, moveVelocity.Z);
		MoveAndSlide();

		Vector3 facingDirection = moveVelocity.LengthSquared() > 0.01f ? moveVelocity : direction;
		if (facingDirection.LengthSquared() > 0.001f)
		{
			float targetYaw = Mathf.Atan2(facingDirection.X, facingDirection.Z);
			Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, targetYaw, dt * 6f), 0f);
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_flashTimer > 0f)
		{
			_flashTimer -= dt;
		}

		_bodyMaterial.AlbedoColor = _flashTimer > 0f ? Colors.White : _baseBodyColor;
		_coreMaterial.Emission = _flashTimer > 0f ? Colors.White : _baseCoreColor;
	}

	public void SetArchetypeSettings(FpsEnemyArchetypeSettings? bruiserSettings, FpsEnemyArchetypeSettings? strikerSettings)
	{
		BruiserSettings = bruiserSettings;
		StrikerSettings = strikerSettings;

		if (IsNodeReady())
		{
			EnsureArchetypeDefaults();
			ApplyArchetypeSettings(resetHealth: false);
			BuildVisuals();
		}
	}

	public void SetWaveScaling(float healthMultiplier, float damageMultiplier, float moveSpeedMultiplier, float attackCadenceMultiplier)
	{
		_waveHealthMultiplier = Mathf.Max(0.1f, healthMultiplier);
		_waveDamageMultiplier = Mathf.Max(0.1f, damageMultiplier);
		_waveMoveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
		_waveAttackCadenceMultiplier = Mathf.Max(0.1f, attackCadenceMultiplier);

		if (IsNodeReady())
		{
			ApplyArchetypeSettings(resetHealth: false);
		}
	}

	public void Initialize(FpsPlayerController player, FpsVerticalSlice director)
	{
		_player = player;
		_director = director;
	}

	public void SetCombatEnabled(bool enabled)
	{
		_combatEnabled = enabled && IsAlive;
		if (!_combatEnabled)
		{
			Velocity = Vector3.Zero;
		}
	}

	public void ApplyDamage(float damage)
	{
		if (damage <= 0f || !IsAlive)
		{
			return;
		}

		_currentHealth = Mathf.Max(0f, _currentHealth - damage);
		_flashTimer = 0.09f;
		if (!IsAlive)
		{
			_combatEnabled = false;
			Died?.Invoke(this);
			QueueFree();
		}
	}

	public void MoveBy(Vector3 displacement)
	{
		if (!IsAlive || !IsInsideTree() || !float.IsFinite(displacement.X) || !float.IsFinite(displacement.Z)) return;
		displacement.Y = 0f;
		if (displacement.LengthSquared() > 4096f) displacement = displacement.Normalized() * 64f;
		int steps = Mathf.Clamp(Mathf.CeilToInt(displacement.Length() / 0.25f), 1, 256);
		for (int i = 0; i < steps; i++) MoveAndCollide(displacement / steps);
	}

	private void EnsureArchetypeDefaults()
	{
		BruiserSettings ??= FpsEnemyArchetypeSettings.CreateBruiserDefaults();
		StrikerSettings ??= FpsEnemyArchetypeSettings.CreateStrikerDefaults();
	}

	private void ApplyArchetypeSettings(bool resetHealth)
	{
		FpsEnemyArchetypeSettings settings = Role == FpsEnemyRole.Bruiser
			? BruiserSettings!
			: StrikerSettings!;

		float previousMaxHealth = Mathf.Max(_maxHealth, 0.001f);
		float nextMaxHealth = Mathf.Max(1f, settings.MaxHealth * _waveHealthMultiplier);
		if (resetHealth || _maxHealth <= 0.001f)
		{
			_currentHealth = nextMaxHealth;
		}
		else
		{
			float healthRatio = Mathf.Clamp(_currentHealth / previousMaxHealth, 0f, 1f);
			_currentHealth = nextMaxHealth * healthRatio;
		}

		_maxHealth = nextMaxHealth;
		_moveSpeed = Mathf.Max(0f, settings.MoveSpeed * _waveMoveSpeedMultiplier);
		_attackRange = Mathf.Max(0f, settings.AttackRange);
		_attackDamage = Mathf.Max(0f, settings.AttackDamage * _waveDamageMultiplier);
		_attackCooldownSeconds = Mathf.Max(0.05f, settings.AttackCooldownSeconds / _waveAttackCadenceMultiplier);
		_projectileDamage = Mathf.Max(0f, settings.ProjectileDamage * _waveDamageMultiplier);
		_projectileSpeed = Mathf.Max(0f, settings.ProjectileSpeed);
		_desiredRange = Mathf.Max(0f, settings.DesiredRange);
		CollisionRadius = Mathf.Max(0.1f, settings.CollisionRadius);
		_collisionHeight = Mathf.Max(0.1f, settings.CollisionHeight);

		_baseBodyColor = settings.BodyColor;
		_baseCoreColor = settings.CoreColor;
		_headHeight = Role == FpsEnemyRole.Bruiser ? 1.25f : 1.18f;
	}

	private void BuildVisuals()
	{
		if (_bodyMesh != null && GodotObject.IsInstanceValid(_bodyMesh))
		{
			_bodyMesh.QueueFree();
		}

		if (_coreMesh != null && GodotObject.IsInstanceValid(_coreMesh))
		{
			_coreMesh.QueueFree();
		}

		for (int childIndex = GetChildCount() - 1; childIndex >= 0; childIndex--)
		{
			if (GetChild(childIndex) is CollisionShape3D collisionShape)
			{
				collisionShape.QueueFree();
			}
		}

		CollisionShape3D collision = new()
		{
			Shape = new CapsuleShape3D
			{
				Radius = CollisionRadius,
				Height = _collisionHeight,
			},
			Position = new Vector3(0f, 0.95f, 0f),
		};
		AddChild(collision);

		_bodyMaterial = new StandardMaterial3D
		{
			Metallic = 0.14f,
			Roughness = 0.34f,
		};
		_coreMaterial = new StandardMaterial3D
		{
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 1.4f,
			Metallic = 0.04f,
			Roughness = 0.22f,
		};

		_bodyMesh = new MeshInstance3D
		{
			MaterialOverride = _bodyMaterial,
			Position = new Vector3(0f, Role == FpsEnemyRole.Bruiser ? 1.0f : 0.96f, 0f),
		};
		if (Role == FpsEnemyRole.Bruiser)
		{
			_bodyMesh.Mesh = new BoxMesh
			{
				Size = new Vector3(1.25f, 1.35f, 1.25f),
			};
		}
		else
		{
			_bodyMesh.Mesh = new CylinderMesh
			{
				TopRadius = _director?.IsDefenseMap == true ? 0f : 0.12f,
				BottomRadius = 0.72f,
				Height = 1.85f,
				RadialSegments = _director?.IsDefenseMap == true ? 4 : 6,
			};
			_bodyMesh.RotationDegrees = _director?.IsDefenseMap == true ? Vector3.Zero : new Vector3(0f, 0f, 180f);
		}
		AddChild(_bodyMesh);

		_coreMesh = new MeshInstance3D
		{
			Mesh = new SphereMesh
			{
				Radius = 0.16f,
				Height = 0.32f,
			},
			Position = new Vector3(0f, 1.32f, Role == FpsEnemyRole.Bruiser ? 0.46f : 0.38f),
			MaterialOverride = _coreMaterial,
		};
		AddChild(_coreMesh);
	}

	private Vector3 TickBruiser(Vector3 direction, float distance)
	{
		if (distance <= _attackRange)
		{
			if (_attackCooldown <= 0f)
			{
				_attackCooldown = _attackCooldownSeconds;
				if (_targetingDefenseCore) _director?.ApplyDefenseCoreDamage(_attackDamage);
				else _player?.ApplyDamage(_attackDamage);
			}

			return Vector3.Zero;
		}

		return direction * _moveSpeed;
	}

	private Vector3 TickStriker(Vector3 direction, float distance, float dt)
	{
		_strafeTimer -= dt;
		if (_strafeTimer <= 0f)
		{
			_strafeTimer = _rng.RandfRange(1.2f, 2.8f);
			_strafeDirection = _rng.Randf() < 0.5f ? -1f : 1f;
		}

		Vector3 right = new(-direction.Z, 0f, direction.X);
		Vector3 moveVelocity = right * (_moveSpeed * 0.85f * _strafeDirection);
		bool hasLineOfSight = HasLineOfSight();
		if (!hasLineOfSight || distance > _desiredRange + 2f)
		{
			moveVelocity += direction * (_moveSpeed * 0.75f);
		}
		else if (distance < _desiredRange - 2f)
		{
			moveVelocity -= direction * (_moveSpeed * 0.55f);
		}

		if (hasLineOfSight && _attackCooldown <= 0f)
		{
			_attackCooldown = _attackCooldownSeconds;
			Vector3 projectileDirection = (_targetPosition - HeadPosition).Normalized();
			_director?.SpawnEnemyProjectile(HeadPosition, projectileDirection, _projectileSpeed, _projectileDamage, GetRid());
		}

		if (moveVelocity.LengthSquared() > _moveSpeed * _moveSpeed)
		{
			moveVelocity = moveVelocity.Normalized() * _moveSpeed;
		}

		return moveVelocity;
	}

	private bool HasLineOfSight()
	{
		if (_player == null)
		{
			return false;
		}

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(HeadPosition, _targetPosition);
		query.CollideWithAreas = false;
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
		{
			return true;
		}

		GodotObject? collider = result["collider"].AsGodotObject();
		return _targetingDefenseCore ? _director!.IsDefenseCoreCollider(collider) : collider == _player;
	}
}
