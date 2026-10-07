using System;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.Visuals;

namespace SuperGatchaRebuild.Fps;

public partial class FpsPlayerController : CharacterBody3D
{
	private static readonly Vector3 ViewModelBasePosition = new(0.34f, -0.28f, -0.72f);

	[Export] public float MouseSensitivity = 0.0018f;
	[Export] public float MoveSpeed = 9.6f;
	[Export] public float AirControl = 0.28f;
	[Export] public float JumpVelocity = 5.2f;
	[Export] public float GravityStrength = 18f;
	[Export] public float MaxHealth = 100f;
	[Export(PropertyHint.Range, "0,95,0.1")] public float DodgeChancePercent { get; set; }
	[Export] public float FireRate = 7.5f;
	[Export] public float ShotDamage = 22f;
	[Export] public float ShotRange = 160f;
	[Export] public float HipFov = 78f;
	[Export] public float AimFov = 63f;
	[Export] public float AimLerpSpeed = 12f;
	[Export] public float HipFireSpreadDegrees = 1.8f;
	[Export] public float AimSpreadDegrees = 0.35f;
	[Export] public float HeadBobStrength = 0.03f;
	[Export] public float HeadBobSpeed = 11.5f;
	[Export] public float RecoilKick = 0.085f;
	[Export] public float RecoilReturnSpeed = 1.6f;

	public event Action<float, float>? HealthChanged;
	public event Action? Died;

	public Camera3D Camera => _camera;
	public float CurrentHealth { get; private set; }
	public bool IsAlive => CurrentHealth > 0.001f;
	public bool IsAiming => _isAiming;
	public Vector3 HeadPosition => _camera?.GlobalPosition ?? (GlobalPosition + new Vector3(0f, 1.55f, 0f));

	private Node3D _pitchPivot = null!;
	private Camera3D _camera = null!;
	private Node3D _viewModelRoot = null!;
	private MeshInstance3D _weaponBody = null!;
	private MeshInstance3D _weaponCore = null!;
	private Node3D _weaponVisualFrame = null!;
	private StandardMaterial3D _weaponBodyMaterial = null!;
	private StandardMaterial3D _weaponCoreMaterial = null!;
	private Node3D? _weaponItemVisual;
	private Node3D? _weaponSocketVisuals;
	private ModifierVisualController? _weaponModifierVisual;
	private readonly RandomNumberGenerator _rng = new();

	private bool _combatEnabled = true;
	private bool _isAiming;
	private float _pitch;
	private float _fireCooldown;
	private float _headBobT;
	private float _recoilOffset;

	public override void _Ready()
	{
		_rng.Randomize();
		BuildRig();
		CurrentHealth = MaxHealth;
		HealthChanged?.Invoke(CurrentHealth, MaxHealth);
		CaptureMouse();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_combatEnabled || !IsAlive || Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			return;
		}

		if (@event is not InputEventMouseMotion mouseMotion)
		{
			return;
		}

		RotateY(-mouseMotion.Relative.X * MouseSensitivity);
		_pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -1.35f, 1.15f);
		_pitchPivot.Rotation = new Vector3(_pitch, 0f, 0f);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_combatEnabled || !IsAlive)
		{
			Velocity = Vector3.Zero;
			return;
		}

		float dt = (float)delta;

		Vector2 input = GetMovementInput();
		Vector3 forward = -Transform.Basis.Z;
		Vector3 right = Transform.Basis.X;
		forward.Y = 0f;
		right.Y = 0f;
		forward = forward.Normalized();
		right = right.Normalized();

		Vector3 desiredVelocity = (right * input.X) + (forward * input.Y);
		if (desiredVelocity.LengthSquared() > 1f)
		{
			desiredVelocity = desiredVelocity.Normalized();
		}

		desiredVelocity *= MoveSpeed;

		Vector3 horizontalVelocity = new(Velocity.X, 0f, Velocity.Z);
		if (IsOnFloor())
		{
			horizontalVelocity = desiredVelocity;
			if (Input.IsKeyPressed(Key.Space))
			{
				Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
			}
			else
			{
				Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
			}
		}
		else
		{
			horizontalVelocity = horizontalVelocity.Lerp(desiredVelocity, Mathf.Clamp(AirControl * dt * 8f, 0f, 1f));
			Velocity += Vector3.Down * GravityStrength * dt;
		}

		Velocity = new Vector3(horizontalVelocity.X, Velocity.Y, horizontalVelocity.Z);
		MoveAndSlide();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (_fireCooldown > 0f)
		{
			_fireCooldown -= dt;
		}

		if (_combatEnabled && IsAlive)
		{
			_isAiming = Input.IsMouseButtonPressed(MouseButton.Right);
		}
		else
		{
			_isAiming = false;
		}

		UpdateViewEffects(dt);
	}

	public void SetCombatEnabled(bool enabled)
	{
		_combatEnabled = enabled && IsAlive;
		if (_combatEnabled)
		{
			CaptureMouse();
		}
		else
		{
			ReleaseMouse();
			_isAiming = false;
		}
	}

	public bool TryConsumeFireRequest(float cooldownSeconds, out Vector3 origin, out Vector3 direction)
	{
		origin = _camera.GlobalPosition;
		direction = -_camera.GlobalTransform.Basis.Z;

		if (!_combatEnabled || !IsAlive || !Input.IsMouseButtonPressed(MouseButton.Left) || _fireCooldown > 0f)
		{
			return false;
		}

		_fireCooldown = Mathf.Max(0.01f, cooldownSeconds);
		_recoilOffset = Mathf.Clamp(_recoilOffset + RecoilKick, 0f, 0.22f);
		direction = BuildShotDirection();
		return true;
	}

	public void ApplyDamage(float damage)
	{
		if (damage <= 0f || !float.IsFinite(damage) || !IsAlive)
		{
			return;
		}
		if (DodgeChancePercent > 0f && _rng.Randf() * 100f < Mathf.Clamp(DodgeChancePercent, 0f, 95f)) return;

		CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
		HealthChanged?.Invoke(CurrentHealth, MaxHealth);
		if (!IsAlive)
		{
			_combatEnabled = false;
			ReleaseMouse();
			Died?.Invoke();
		}
	}

	/// <summary>Preserve health fraction when switching equipment; repeated retunes never refill health.</summary>
	public void RetuneCombatStats(float maxHealth, float moveSpeed, float dodgeChancePercent)
	{
		bool wasAlive = IsAlive;
		float healthRatio = MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
		MaxHealth = float.IsFinite(maxHealth) ? Mathf.Max(1f, maxHealth) : 100f;
		MoveSpeed = float.IsFinite(moveSpeed) ? Mathf.Max(0.1f, moveSpeed) : 9.6f;
		DodgeChancePercent = float.IsFinite(dodgeChancePercent) ? Mathf.Clamp(dodgeChancePercent, 0f, 95f) : 0f;
		CurrentHealth = Mathf.Clamp(MaxHealth * healthRatio, 0f, MaxHealth);
		if (wasAlive) CurrentHealth = Mathf.Max(0.002f, CurrentHealth);
		HealthChanged?.Invoke(CurrentHealth, MaxHealth);
	}

	public void Heal(float amount)
	{
		if (!IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
		float next = Mathf.Min(MaxHealth, CurrentHealth + amount);
		if (Mathf.IsEqualApprox(next, CurrentHealth)) return;
		CurrentHealth = next;
		HealthChanged?.Invoke(CurrentHealth, MaxHealth);
	}

	public void SetDebugHealth(float maxHealth, float currentHealth)
	{
		float clampedMax = Mathf.Max(1f, maxHealth);
		float clampedCurrent = Mathf.Clamp(currentHealth, 0f, clampedMax);

		MaxHealth = clampedMax;
		CurrentHealth = clampedCurrent;
		HealthChanged?.Invoke(CurrentHealth, MaxHealth);

		if (CurrentHealth <= 0.001f)
		{
			_combatEnabled = false;
			ReleaseMouse();
			Died?.Invoke();
		}
	}

	public void ApplyWeaponCardVisual(ShapeData shape, Color accentColor)
	{
		if (_weaponBody == null || _weaponCore == null)
		{
			return;
		}

		ClearWeaponItemVisual();
		_weaponBody.Visible = true;
		_weaponCore.Visible = true;

		_weaponBody.Mesh = BuildWeaponBodyMesh(shape.AttackType);
		_weaponBody.Position = GetWeaponBodyPosition(shape.AttackType);
		_weaponBody.RotationDegrees = GetWeaponBodyRotation(shape.AttackType);
		_weaponCore.Mesh = BuildWeaponCoreMesh(shape.AttackType);
		_weaponCore.Position = GetWeaponCorePosition(shape.AttackType);
		_weaponCore.Scale = Vector3.One;

		_weaponBodyMaterial.AlbedoColor = accentColor.Lerp(new Color(0.12f, 0.15f, 0.20f, 1f), 0.68f);
		_weaponBodyMaterial.Metallic = 0.28f;
		_weaponBodyMaterial.Roughness = 0.30f;
		_weaponCoreMaterial.AlbedoColor = accentColor;
		_weaponCoreMaterial.Emission = accentColor;
	}

	public void ApplyWeaponItemVisual(EquipmentItem weapon)
	{
		if (weapon.Shape == null)
		{
			return;
		}

		ClearWeaponItemVisual();
		_weaponBody.Visible = false;
		_weaponCore.Visible = false;

		float visualScale = GetWeaponVisualScale(weapon);
		_weaponVisualFrame.Visible = true;
		_weaponVisualFrame.Position = GetWeaponVisualPosition(weapon.Shape.AttackType);
		_weaponVisualFrame.RotationDegrees = GetWeaponVisualRotation(weapon.Shape.AttackType);
		_weaponVisualFrame.Scale = new Vector3(visualScale, visualScale, visualScale);

		Color tint = ResolveWeaponTint(weapon);
		_weaponItemVisual = ShapeVisualFactory.CreateVisual(weapon.Shape, tint);
		_weaponItemVisual.Name = "HubWeaponModel";
		ConfigureViewModelRendering(_weaponItemVisual);
		_weaponVisualFrame.AddChild(_weaponItemVisual);

		_weaponSocketVisuals = new Node3D { Name = "HubWeaponSockets" };
		_weaponVisualFrame.AddChild(_weaponSocketVisuals);
		WeaponModulatorVisualFactory.PopulateSocketVisuals(
			_weaponSocketVisuals,
			weapon,
			layoutScale: 1f,
			options: new SocketVisualOptions
			{
				EnableOrbitStyle = true,
			});
		ConfigureViewModelRendering(_weaponSocketVisuals);

		if (ModifierVisualTheme.HasSpecialVisuals(weapon))
		{
			_weaponModifierVisual = new ModifierVisualController
			{
				Name = "HubWeaponModifierVisual",
				Intensity = 1.08f,
			};
			_weaponVisualFrame.AddChild(_weaponModifierVisual);
			_weaponModifierVisual.Configure(weapon, _weaponItemVisual, weapon.Shape, 1f, glowLight: null, intensity: 1.08f);
		}
	}

	private void BuildRig()
	{
		CollisionShape3D collision = new()
		{
			Name = "CollisionShape3D",
			Shape = new CapsuleShape3D
			{
				Radius = 0.42f,
				Height = 1.1f,
			},
			Position = new Vector3(0f, 0.97f, 0f),
		};
		AddChild(collision);

		_pitchPivot = new Node3D
		{
			Name = "PitchPivot",
			Position = new Vector3(0f, 1.55f, 0f),
		};
		AddChild(_pitchPivot);

		_camera = new Camera3D
		{
			Name = "Camera3D",
			Current = true,
			Near = 0.05f,
			Far = 400f,
			Fov = HipFov,
		};
		_pitchPivot.AddChild(_camera);

		_viewModelRoot = new Node3D
		{
			Name = "ViewModelRoot",
			Position = ViewModelBasePosition,
		};
		_camera.AddChild(_viewModelRoot);

		_weaponBodyMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.18f, 0.24f, 0.31f, 1f),
			Metallic = 0.35f,
			Roughness = 0.32f,
		};
		_weaponCoreMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.36f, 0.88f, 1f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0.28f, 0.80f, 1f, 1f),
			EmissionEnergyMultiplier = 1.6f,
			Metallic = 0.08f,
			Roughness = 0.18f,
		};

		_weaponBody = new MeshInstance3D
		{
			Name = "WeaponBody",
			Mesh = new BoxMesh
			{
				Size = new Vector3(0.24f, 0.18f, 0.84f),
			},
			Position = new Vector3(0.04f, -0.03f, -0.08f),
			MaterialOverride = _weaponBodyMaterial,
		};
		_viewModelRoot.AddChild(_weaponBody);

		_weaponCore = new MeshInstance3D
		{
			Name = "WeaponCore",
			Mesh = new SphereMesh
			{
				Radius = 0.08f,
				Height = 0.16f,
			},
			Position = new Vector3(-0.06f, 0.02f, -0.24f),
			MaterialOverride = _weaponCoreMaterial,
		};
		_viewModelRoot.AddChild(_weaponCore);

		_weaponVisualFrame = new Node3D
		{
			Name = "WeaponVisualFrame",
			Visible = false,
		};
		_viewModelRoot.AddChild(_weaponVisualFrame);
	}

	private Vector2 GetMovementInput()
	{
		Vector2 input = Vector2.Zero;

		if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A))
		{
			input.X -= 1f;
		}

		if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D))
		{
			input.X += 1f;
		}

		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W))
		{
			input.Y += 1f;
		}

		if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S))
		{
			input.Y -= 1f;
		}

		if (input.LengthSquared() > 1f)
		{
			input = input.Normalized();
		}

		return input;
	}

	private void UpdateViewEffects(float dt)
	{
		float bobBlend = 0f;
		Vector3 horizontalVelocity = new(Velocity.X, 0f, Velocity.Z);
		if (_combatEnabled && IsOnFloor())
		{
			float speedFraction = Mathf.Clamp(horizontalVelocity.Length() / Mathf.Max(0.01f, MoveSpeed), 0f, 1.25f);
			bobBlend = Mathf.Clamp(speedFraction, 0f, 1f);
			_headBobT += dt * HeadBobSpeed * Mathf.Max(0.25f, speedFraction);
		}

		float bobX = Mathf.Cos(_headBobT * 0.5f) * HeadBobStrength * bobBlend;
		float bobY = Mathf.Abs(Mathf.Sin(_headBobT)) * HeadBobStrength * bobBlend;

		_camera.Position = new Vector3(bobX, bobY, 0f);
		_recoilOffset = Mathf.MoveToward(_recoilOffset, 0f, RecoilReturnSpeed * dt);
		_viewModelRoot.Position = ViewModelBasePosition + new Vector3(bobX * 1.15f, bobY * 0.55f - (_recoilOffset * 0.18f), _recoilOffset);

		float targetFov = _isAiming ? AimFov : HipFov;
		_camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, Mathf.Clamp(dt * AimLerpSpeed, 0f, 1f));
		_weaponCoreMaterial.EmissionEnergyMultiplier = _fireCooldown > 0f ? 2.3f : 1.6f;
		if (_weaponModifierVisual != null && IsInstanceValid(_weaponModifierVisual))
		{
			_weaponModifierVisual.Intensity = _fireCooldown > 0f ? 1.16f : 1.08f;
		}
	}

	private Vector3 BuildShotDirection()
	{
		Basis basis = _camera.GlobalTransform.Basis;
		Vector3 direction = -basis.Z;
		float spreadScale = Mathf.Tan(Mathf.DegToRad(_isAiming ? AimSpreadDegrees : HipFireSpreadDegrees));
		direction += basis.X * _rng.RandfRange(-spreadScale, spreadScale);
		direction += basis.Y * _rng.RandfRange(-spreadScale, spreadScale);
		return direction.Normalized();
	}

	private static void CaptureMouse()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private static void ReleaseMouse()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void ClearWeaponItemVisual()
	{
		if (_weaponModifierVisual != null)
		{
			if (IsInstanceValid(_weaponModifierVisual))
			{
				_weaponModifierVisual.QueueFree();
			}

			_weaponModifierVisual = null;
		}

		if (_weaponSocketVisuals != null)
		{
			if (IsInstanceValid(_weaponSocketVisuals))
			{
				_weaponSocketVisuals.QueueFree();
			}

			_weaponSocketVisuals = null;
		}

		if (_weaponItemVisual != null)
		{
			if (IsInstanceValid(_weaponItemVisual))
			{
				_weaponItemVisual.QueueFree();
			}

			_weaponItemVisual = null;
		}

		if (_weaponVisualFrame != null)
		{
			_weaponVisualFrame.Visible = false;
			_weaponVisualFrame.Position = Vector3.Zero;
			_weaponVisualFrame.Rotation = Vector3.Zero;
			_weaponVisualFrame.Scale = Vector3.One;
		}
	}

	private static void ConfigureViewModelRendering(Node node)
	{
		if (node is GeometryInstance3D geometry)
		{
			geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		}

		foreach (Node child in node.GetChildren())
		{
			ConfigureViewModelRendering(child);
		}
	}

	private static Color ResolveWeaponTint(EquipmentItem weapon)
	{
		return weapon.Rarity?.UiColor ?? new Color(0.75f, 0.78f, 0.9f, 1f);
	}

	private static Vector3 GetWeaponVisualPosition(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Nova => new Vector3(0.08f, -0.10f, -0.12f),
			WeaponAttackType.WrathAura => new Vector3(0.06f, -0.11f, -0.10f),
			WeaponAttackType.Orbital => new Vector3(0.10f, -0.09f, -0.12f),
			WeaponAttackType.VoidSingularity => new Vector3(0.08f, -0.11f, -0.10f),
			WeaponAttackType.Laser => new Vector3(0.12f, -0.11f, -0.18f),
			WeaponAttackType.Mortar => new Vector3(0.12f, -0.12f, -0.18f),
			WeaponAttackType.FlameCone => new Vector3(0.12f, -0.11f, -0.18f),
			_ => new Vector3(0.10f, -0.11f, -0.16f),
		};
	}

	private static Vector3 GetWeaponVisualRotation(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Sawblade => new Vector3(18f, -92f, 24f),
			WeaponAttackType.Nova => new Vector3(8f, -88f, 12f),
			WeaponAttackType.Orbital => new Vector3(14f, -88f, 16f),
			WeaponAttackType.VoidSingularity => new Vector3(10f, -86f, 10f),
			WeaponAttackType.Laser => new Vector3(12f, -102f, 6f),
			WeaponAttackType.Mortar => new Vector3(16f, -100f, 10f),
			WeaponAttackType.FlameCone => new Vector3(14f, -100f, 10f),
			_ => new Vector3(14f, -94f, 8f),
		};
	}

	private static float GetWeaponVisualScale(EquipmentItem weapon)
	{
		float baseScale = weapon.Shape?.AttackType switch
		{
			WeaponAttackType.Nova => 0.16f,
			WeaponAttackType.WrathAura => 0.15f,
			WeaponAttackType.VoidSingularity => 0.16f,
			WeaponAttackType.Orbital => 0.18f,
			WeaponAttackType.Sawblade => 0.19f,
			WeaponAttackType.Laser => 0.22f,
			WeaponAttackType.Mortar => 0.20f,
			WeaponAttackType.FlameCone => 0.19f,
			_ => 0.18f,
		};

		Vector3 shapeScale = weapon.Shape?.VisualScale ?? Vector3.One;
		float averageShapeScale = (Mathf.Abs(shapeScale.X) + Mathf.Abs(shapeScale.Y) + Mathf.Abs(shapeScale.Z)) / 3f;
		float normalizedScale = Mathf.Clamp(averageShapeScale, 0.55f, 2.40f);
		return baseScale / normalizedScale;
	}

	private static Mesh BuildWeaponBodyMesh(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Sawblade => new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.25f, Height = 0.42f, RadialSegments = 18 },
			WeaponAttackType.Laser => new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.15f, Height = 1.00f, RadialSegments = 3 },
			WeaponAttackType.Mine => new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.48f, RadialSegments = 6 },
			WeaponAttackType.Nova => new SphereMesh { Radius = 0.21f, Height = 0.42f },
			WeaponAttackType.Orbital => new BoxMesh { Size = new Vector3(0.26f, 0.26f, 0.74f) },
			WeaponAttackType.Homing => new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.18f, Height = 0.82f, RadialSegments = 6 },
			WeaponAttackType.FlameCone => new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.20f, Height = 0.82f, RadialSegments = 18 },
			WeaponAttackType.Rift => new BoxMesh { Size = new Vector3(0.30f, 0.24f, 0.66f) },
			WeaponAttackType.Mortar => new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.18f, Height = 0.72f, RadialSegments = 10 },
			WeaponAttackType.ChainLightning => new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 0.94f, RadialSegments = 4 },
			WeaponAttackType.Drill => new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.15f, Height = 0.96f, RadialSegments = 18 },
			WeaponAttackType.VoidSingularity => new BoxMesh { Size = new Vector3(0.28f, 0.22f, 0.62f) },
			WeaponAttackType.TemporalEcho => new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.14f, Height = 0.64f, RadialSegments = 12 },
			WeaponAttackType.WrathAura => new SphereMesh { Radius = 0.18f, Height = 0.36f },
			WeaponAttackType.ShadowClone => new BoxMesh { Size = new Vector3(0.24f, 0.18f, 0.76f) },
			_ => new BoxMesh { Size = new Vector3(0.24f, 0.18f, 0.84f) },
		};
	}

	private static Mesh BuildWeaponCoreMesh(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Orbital => new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) },
			WeaponAttackType.Rift => new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) },
			WeaponAttackType.VoidSingularity => new SphereMesh { Radius = 0.10f, Height = 0.20f },
			WeaponAttackType.ShadowClone => new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.20f, RadialSegments = 6 },
			_ => new SphereMesh { Radius = 0.08f, Height = 0.16f },
		};
	}

	private static Vector3 GetWeaponBodyPosition(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Sawblade => new Vector3(0.07f, -0.09f, -0.18f),
			WeaponAttackType.Nova => new Vector3(0.02f, -0.08f, -0.20f),
			WeaponAttackType.Orbital => new Vector3(0.06f, -0.04f, -0.08f),
			WeaponAttackType.FlameCone => new Vector3(0.05f, -0.03f, -0.16f),
			WeaponAttackType.Mortar => new Vector3(0.08f, -0.05f, -0.14f),
			WeaponAttackType.VoidSingularity => new Vector3(0.05f, -0.04f, -0.10f),
			_ => new Vector3(0.04f, -0.03f, -0.08f),
		};
	}

	private static Vector3 GetWeaponCorePosition(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Sawblade => new Vector3(-0.01f, 0.02f, -0.04f),
			WeaponAttackType.Nova => new Vector3(0f, 0f, -0.02f),
			WeaponAttackType.Rift => new Vector3(-0.06f, 0.05f, -0.18f),
			WeaponAttackType.VoidSingularity => new Vector3(-0.04f, 0.02f, -0.14f),
			WeaponAttackType.WrathAura => new Vector3(0f, 0f, -0.04f),
			_ => new Vector3(-0.06f, 0.02f, -0.24f),
		};
	}

	private static Vector3 GetWeaponBodyRotation(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Sawblade => new Vector3(90f, 0f, 0f),
			WeaponAttackType.Laser => new Vector3(0f, 0f, 90f),
			WeaponAttackType.Homing => new Vector3(0f, 0f, 90f),
			WeaponAttackType.FlameCone => new Vector3(0f, 0f, 90f),
			WeaponAttackType.Mortar => new Vector3(0f, 0f, 90f),
			WeaponAttackType.ChainLightning => new Vector3(0f, 0f, 90f),
			WeaponAttackType.Drill => new Vector3(0f, 0f, 90f),
			WeaponAttackType.TemporalEcho => new Vector3(90f, 0f, 0f),
			_ => Vector3.Zero,
		};
	}
}
