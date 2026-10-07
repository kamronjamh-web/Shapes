using Godot;

namespace SuperGatchaRebuild;

public partial class BossActor : Node3D
{
	private const float CubeMeshSize = 3.4f;
	private const float BossBaseWorldHeight = 1.3f;
	private const float CubeGroundClearance = 0.18f;
	private const float CubeGroundEffectHeight = -BossBaseWorldHeight + 0.06f;
	private const float CubeTelegraphLeanRadians = 0.22f;
	private const float CubeLandingDip = 0.16f;
	private const float CubeRecoverLift = 0.10f;
	private const float CubeTeleportHeight = 118f;
	private const float CubeSeamThickness = 0.06f;
	private const float CubeSeamOutset = 0.03f;
	private const float CubeSeamFaceHeight = 0.58f;
	private const float CubeSeamLengthPadding = 0.18f;
	private const float CubeLandingRingDuration = 0.48f;
	private const float CubeDustBurstDuration = 0.95f;
	private const int CubeDustBurstCount = 14;
	private const float MonolithVisualHeight = 2.2f;
	private static readonly Vector3 MonolithScale = new Vector3(1.45f, 1.45f, 1.45f);
	private static readonly Vector3 CubeScale = new Vector3(3.78f, 3.78f, 3.78f);
	private static readonly Vector3 OverseerScale = new Vector3(1.25f, 1.25f, 1.25f);
	private static readonly float CubeSideLength = CubeMeshSize * CubeScale.X;
	private static readonly float CubeVisualHeight = (CubeSideLength * 0.5f) - BossBaseWorldHeight + CubeGroundClearance;
	private static readonly float CubeRollLift = CubeSideLength * (Mathf.Sqrt(0.5f) - 0.5f);

	public BossKind Kind = BossKind.Monolith;
	public BossPhase Phase = BossPhase.None;

	public float MaxHp = 1000f;
	public float Hp = 1000f;
	public float Radius = 4f;
	public float MoveSpeed = 3.5f;

	public float FlashTimer = 0f;
	public float AimAngle = 0f;

	public float BurstCd = 2f;
	public float AnnihilateCd = 12f;
	public float PhaseTimer = 0f;

	public float ShootCd = 1f;
	public float LaserCd = 11f;
	public float LaserAng = 0f;
	public float OrbitAng = 0f;
	public Vector2 OrbitPlayerVel = Vector2.Zero;
	public float ContactDps = 0f;
	public float RollDistance = 10.5f;
	public float RollDuration = 0.44f;
	public float LandingRadius = 15f;
	public float LandingDamage = 22f;
	public float LandingShakeRadius = 28f;
	public float LandingShakeAmount = 1.35f;
	public int ComboRollsRemaining = 0;
	public int FaceIndex = 0;
	public Vector2 RollStart = Vector2.Zero;
	public Vector2 RollTarget = Vector2.Zero;
	public Vector2 RollDirection = Vector2.Right;
	public Basis CubeVisualBasis = Basis.Identity;
	public Basis CubeRollStartBasis = Basis.Identity;
	public Vector3 CubeRollAxis = new Vector3(1f, 0f, 0f);
	public float CubeRollAngleRadians = 0f;
	public Vector3 CubePivotLocalMesh = Vector3.Zero;

	private Vector2 _position2D = Vector2.Zero;
	private MeshInstance3D _mesh = null!;
	private StandardMaterial3D _mat = null!;
	private MeshInstance3D? _cubeSeam;
	private StandardMaterial3D? _cubeSeamMat;
	private MeshInstance3D? _cubeLandingRing;
	private StandardMaterial3D? _cubeLandingRingMat;
	private readonly MeshInstance3D?[] _cubeDustMeshes = new MeshInstance3D?[CubeDustBurstCount];
	private readonly StandardMaterial3D?[] _cubeDustMaterials = new StandardMaterial3D?[CubeDustBurstCount];
	private float _cubeSeamStrength = 0f;
	private float _cubeLandingRingTimer = 0f;
	private float _cubeDustTimer = 0f;
	private float _cubeEffectPulse = 0f;

	public new Vector2 Position
	{
		get => _position2D;
		set
		{
			if (!RuntimeMath.IsFinite(value))
			{
				return;
			}

			_position2D = value;
			base.Position = new Vector3(value.X, BossBaseWorldHeight, value.Y);
		}
	}

	public bool IsAlive => Hp > 0f;

	public void Configure(BossKind kind, Vector2 startPos, Vector2 playerPos, float waveScale)
	{
		Kind = kind;
		Position = startPos;
		FlashTimer = 0f;

		AimAngle = Mathf.Atan2(playerPos.Y - startPos.Y, playerPos.X - startPos.X);
		Rotation = kind == BossKind.Cube ? Vector3.Zero : new Vector3(0f, -AimAngle, 0f);

		ContactDps = 0f;
		RollStart = startPos;
		RollTarget = startPos;
		RollDirection = Vector2.Right;
		ComboRollsRemaining = 0;
		FaceIndex = 0;
		CubeVisualBasis = Basis.Identity;
		CubeRollStartBasis = Basis.Identity;
		CubeRollAxis = new Vector3(1f, 0f, 0f);
		CubeRollAngleRadians = 0f;
		ResetCubeEffects();

		if (kind == BossKind.Overseer)
		{
			MaxHp = 2400f * waveScale;
			Radius = 4f;
			MoveSpeed = 3.5f;
			Phase = BossPhase.Orbit;
			ShootCd = 1f;
			LaserCd = 11f;
			LaserAng = AimAngle;
			OrbitAng = AimAngle + Mathf.Pi;
			OrbitPlayerVel = Vector2.Zero;
		}
		else if (kind == BossKind.Cube)
		{
			MaxHp = 3600f * waveScale;
			Radius = CubeSideLength * 0.77f;
			MoveSpeed = 0f;
			ContactDps = 34f;
			Phase = BossPhase.RollTelegraph;
			RollDistance = CubeSideLength;
			RollDuration = 0.84f;
			LandingRadius = 15f;
			LandingDamage = 22f;
			LandingShakeRadius = 38f;
			LandingShakeAmount = 1.55f;
		}
		else
		{
			MaxHp = 3000f * waveScale;
			Radius = 4f;
			MoveSpeed = 3.5f;
			Phase = BossPhase.Move;
			BurstCd = 2f;
			AnnihilateCd = 11.5f;
		}

		Hp = MaxHp;
		BuildVisualForKind();
		Position = startPos;
		if (kind == BossKind.Cube)
		{
			ResetCubePose();
		}

		UpdateVisuals();
	}

	public void TakeDamage(float dmg)
	{
		Hp = Mathf.Max(0f, Hp - dmg);
		FlashTimer = 0.1f;
	}

	public void UpdateAim(double delta, Vector2 targetPos, bool slowTurn)
	{
		if (Kind == BossKind.Cube)
		{
			UpdateVisuals();
			return;
		}

		float turnRate = slowTurn ? 0.8f : 4f;
		Vector2 toTarget = targetPos - Position;
		if (!RuntimeMath.IsFinite(toTarget) || toTarget.LengthSquared() <= 0.000001f)
		{
			UpdateVisuals();
			return;
		}

		float target = Mathf.Atan2(toTarget.Y, toTarget.X);
		float deltaAng = WrapAngle(target - AimAngle);
		AimAngle += deltaAng * Mathf.Min(1f, (float)delta * turnRate);
		Rotation = new Vector3(0f, -AimAngle, 0f);
		UpdateVisuals();
	}

	public override void _Process(double delta)
	{
		if (FlashTimer > 0f)
		{
			FlashTimer -= (float)delta;
		}

		if (Kind == BossKind.Cube)
		{
			UpdateCubeEffects((float)delta);
		}

		UpdateVisuals();
	}

	public void PrepareCubeRoll(Vector2 direction)
	{
		if (Kind != BossKind.Cube)
		{
			return;
		}

		RollDirection = direction;
		GetCubeRollAxisAngle(direction, out Vector3 axis, out float angle);
		CubeRollAxis = axis;
		CubeRollAngleRadians = angle;
		ConfigureCubePivot(direction);
		ConfigureCubeSeam();
		SetCubeSeamState(0.25f, true);
	}

	public void BeginCubeRoll()
	{
		if (Kind != BossKind.Cube)
		{
			return;
		}

		SetCubeSeamState(1f, true);
	}

	public void ResetCubePose()
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		ApplyCubeTransform(CubeVisualBasis, CubeScale, new Vector3(0f, CubeVisualHeight, 0f));
	}

	public void SetCubeTelegraphVisual(float progress)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float leanAngle = CubeTelegraphLeanRadians * clamped;
		ApplyCubeTransform(
			GetCubeDisplayBasis(leanAngle),
			CubeScale,
			GetCubeDisplayPosition(leanAngle));
		SetCubeSeamState(0.45f + clamped * 0.55f, true);
	}

	public void SetCubeRollVisual(float progress, float rootTravelDistance)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float rollAngle = Mathf.Lerp(CubeTelegraphLeanRadians, Mathf.Pi * 0.5f, clamped);
		ApplyCubeTransform(
			GetCubeDisplayBasis(rollAngle),
			CubeScale,
			GetCubeDisplayPosition(rollAngle, rootTravelDistance));
		SetCubeSeamState(1f, true);
	}

	public void FinishCubeRollVisual()
	{
		if (Kind != BossKind.Cube)
		{
			return;
		}

		CubeVisualBasis = GetCubeDisplayBasis(Mathf.Pi * 0.5f).Orthonormalized();
		FaceIndex = (FaceIndex + 1) % 6;
		ResetCubePose();
	}

	public void SetCubeLandingVisual(float progress)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float dip = Mathf.Sin(clamped * Mathf.Pi) * CubeLandingDip;
		ApplyCubeTransform(
			CubeVisualBasis,
			CubeScale,
			new Vector3(0f, CubeVisualHeight - dip, 0f));
		SetCubeSeamState(1f - clamped, clamped < 0.98f);
	}

	public void SetCubeRecoverVisual(float progress)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float lift = Mathf.Sin(clamped * Mathf.Pi) * CubeRecoverLift * (1f - clamped * 0.35f);
		ApplyCubeTransform(
			CubeVisualBasis,
			CubeScale,
			new Vector3(0f, CubeVisualHeight + lift, 0f));
		SetCubeSeamState(0f, false);
	}

	public void SetCubeTeleportAscendVisual(float progress)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float height = Mathf.Lerp(CubeVisualHeight, CubeTeleportHeight, Mathf.Pow(clamped, 0.72f));
		ApplyCubeTransform(CubeVisualBasis, CubeScale, new Vector3(0f, height, 0f));
		SetCubeSeamState(0f, false);
	}

	public void SetCubeTeleportDescendVisual(float progress)
	{
		if (_mesh == null || Kind != BossKind.Cube)
		{
			return;
		}

		float clamped = Mathf.Clamp(progress, 0f, 1f);
		float height = Mathf.Lerp(CubeTeleportHeight, CubeVisualHeight, 1f - Mathf.Pow(1f - clamped, 2.2f));
		ApplyCubeTransform(CubeVisualBasis, CubeScale, new Vector3(0f, height, 0f));
		SetCubeSeamState(0f, false);
	}

	public void TriggerCubeLandingEffects()
	{
		if (Kind != BossKind.Cube)
		{
			return;
		}

		_cubeLandingRingTimer = CubeLandingRingDuration;
		_cubeDustTimer = 0f;
	}

	private void BuildVisualForKind()
	{
		if (_mesh != null)
		{
			_mesh.QueueFree();
		}

		_mat = new StandardMaterial3D
		{
			Metallic = 0.45f,
			Roughness = 0.35f,
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 1.2f,
		};

		_mesh = new MeshInstance3D();

		if (Kind == BossKind.Overseer)
		{
			_mesh.Position = new Vector3(0f, 2.0f, 0f);
			_mesh.Mesh = new CylinderMesh
			{
				TopRadius = 0.01f,
				BottomRadius = 1.3f,
				Height = 3.8f,
				RadialSegments = 6,
			};
			_mesh.RotationDegrees = new Vector3(0f, 0f, -90f);
		}
		else
		{
			_mesh.Position = new Vector3(0f, Kind == BossKind.Cube ? CubeVisualHeight : MonolithVisualHeight, 0f);
			_mesh.Mesh = new BoxMesh
			{
				Size = Kind == BossKind.Cube
					? new Vector3(CubeMeshSize, CubeMeshSize, CubeMeshSize)
					: new Vector3(3.0f, 3.0f, 3.0f),
			};
			_mesh.RotationDegrees = Vector3.Zero;
		}

		_mesh.MaterialOverride = _mat;
		AddChild(_mesh);

		_mesh.Scale = Kind switch
		{
			BossKind.Overseer => OverseerScale,
			BossKind.Cube => CubeScale,
			_ => MonolithScale,
		};

		if (Kind == BossKind.Cube)
		{
			EnsureCubeEffects();
			ResetCubeEffects();
			ResetCubePose();
		}
	}

	private void UpdateVisuals()
	{
		if (_mat == null)
		{
			return;
		}

		bool flash = FlashTimer > 0f;
		if (Kind == BossKind.Overseer)
		{
			_mat.AlbedoColor = flash ? Colors.White : new Color(0.88f, 0.15f, 0.2f);
			_mat.Emission = new Color(0.95f, 0.18f, 0.15f, 1f);
		}
		else if (Kind == BossKind.Cube)
		{
			_mat.AlbedoColor = flash ? Colors.White : new Color(0.86f, 0.58f, 0.16f);
			_mat.Emission = new Color(0.95f, 0.74f, 0.22f, 1f);
		}
		else
		{
			_mat.AlbedoColor = flash ? Colors.White : new Color(0.42f, 0.16f, 0.68f);
			_mat.Emission = new Color(0.45f, 0.2f, 0.75f, 1f);
		}
	}

	private void ApplyCubeTransform(Basis basis, Vector3 scale, Vector3 localPosition)
	{
		_mesh.Transform = new Transform3D(basis.Scaled(scale), localPosition);
	}

	private Basis GetCubeDisplayBasis(float angleMagnitude)
	{
		float signedAngle = Mathf.Sign(CubeRollAngleRadians) * Mathf.Clamp(angleMagnitude, 0f, Mathf.Pi * 0.5f);
		Basis rollBasis = new(CubeRollAxis, signedAngle);
		return (rollBasis * CubeVisualBasis).Orthonormalized();
	}

	private static void GetCubeRollAxisAngle(Vector2 direction, out Vector3 axis, out float angle)
	{
		if (Mathf.Abs(direction.X) >= Mathf.Abs(direction.Y))
		{
			axis = new Vector3(0f, 0f, 1f);
			angle = direction.X >= 0f ? -Mathf.Pi * 0.5f : Mathf.Pi * 0.5f;
			return;
		}

		axis = new Vector3(1f, 0f, 0f);
		angle = direction.Y >= 0f ? Mathf.Pi * 0.5f : -Mathf.Pi * 0.5f;
	}

	private void EnsureCubeEffects()
	{
		if (_cubeSeam != null && GodotObject.IsInstanceValid(_cubeSeam))
		{
			_cubeSeam.QueueFree();
		}

		_cubeSeamMat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = new Color(1f, 0.78f, 0.22f, 1f),
			EmissionEnergyMultiplier = 0f,
			AlbedoColor = new Color(1f, 0.84f, 0.32f, 0f),
		};
		_cubeSeam = new MeshInstance3D
		{
			MaterialOverride = _cubeSeamMat,
			Visible = false,
		};
		_mesh.AddChild(_cubeSeam);

		if (_cubeLandingRing == null || !GodotObject.IsInstanceValid(_cubeLandingRing))
		{
			_cubeLandingRingMat = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				EmissionEnabled = true,
				Emission = new Color(0.95f, 0.68f, 0.18f, 1f),
				EmissionEnergyMultiplier = 0f,
				AlbedoColor = new Color(0.14f, 0.08f, 0.02f, 0f),
			};
			_cubeLandingRing = new MeshInstance3D
			{
				Mesh = new TorusMesh
				{
					InnerRadius = 0.88f,
					OuterRadius = 1f,
					Rings = 28,
					RingSegments = 18,
				},
				MaterialOverride = _cubeLandingRingMat,
				RotationDegrees = new Vector3(90f, 0f, 0f),
				Visible = false,
			};
			AddChild(_cubeLandingRing);
		}

		for (int i = 0; i < CubeDustBurstCount; i++)
		{
			if (_cubeDustMeshes[i] != null && GodotObject.IsInstanceValid(_cubeDustMeshes[i]))
			{
				continue;
			}

			StandardMaterial3D mat = new()
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				EmissionEnabled = true,
				Emission = new Color(0.62f, 0.42f, 0.18f, 1f),
				EmissionEnergyMultiplier = 0f,
				AlbedoColor = new Color(0.30f, 0.22f, 0.13f, 0f),
				Roughness = 1f,
			};

			MeshInstance3D dust = new()
			{
				Mesh = new SphereMesh
				{
					Radius = 0.62f,
					Height = 1.24f,
				},
				MaterialOverride = mat,
				Visible = false,
			};
			AddChild(dust);
			_cubeDustMaterials[i] = mat;
			_cubeDustMeshes[i] = dust;
		}
	}

	private void ResetCubeEffects()
	{
		_cubeSeamStrength = 0f;
		_cubeLandingRingTimer = 0f;
		_cubeDustTimer = 0f;
		_cubeEffectPulse = 0f;

		if (_cubeSeam != null)
		{
			_cubeSeam.Visible = false;
		}

		if (_cubeLandingRing != null)
		{
			_cubeLandingRing.Visible = false;
		}

		for (int i = 0; i < CubeDustBurstCount; i++)
		{
			if (_cubeDustMeshes[i] != null)
			{
				_cubeDustMeshes[i]!.Visible = false;
			}
		}
	}

	private void ConfigureCubePivot(Vector2 direction)
	{
		Vector3 moveLocal = ToCubeLocalDirection(new Vector3(direction.X, 0f, direction.Y));
		Vector3 downLocal = ToCubeLocalDirection(Vector3.Down);
		int moveAxis = GetDominantAxisIndex(moveLocal);
		int downAxis = GetDominantAxisIndex(downLocal);
		float half = CubeMeshSize * 0.5f;

		CubePivotLocalMesh = Vector3.Zero;
		SetAxis(ref CubePivotLocalMesh, moveAxis, GetSignedHalf(moveLocal, moveAxis, half));
		SetAxis(ref CubePivotLocalMesh, downAxis, GetSignedHalf(downLocal, downAxis, half));
	}

	private void ConfigureCubeSeam()
	{
		if (_cubeSeam == null)
		{
			return;
		}

		int moveAxis = GetDominantAxisIndex(ToCubeLocalDirection(new Vector3(RollDirection.X, 0f, RollDirection.Y)));
		int downAxis = GetDominantAxisIndex(ToCubeLocalDirection(Vector3.Down));
		int freeAxis = 3 - moveAxis - downAxis;
		float half = CubeMeshSize * 0.5f;
		float moveSign = Mathf.Sign(GetAxis(CubePivotLocalMesh, moveAxis));
		float downSign = Mathf.Sign(GetAxis(CubePivotLocalMesh, downAxis));
		float seamHeight = CubeMeshSize * CubeSeamFaceHeight;

		Vector3 size = Vector3.Zero;
		SetAxis(ref size, moveAxis, CubeSeamThickness);
		SetAxis(ref size, downAxis, seamHeight);
		SetAxis(ref size, freeAxis, CubeMeshSize + CubeSeamLengthPadding);

		Vector3 seamPos = Vector3.Zero;
		SetAxis(ref seamPos, moveAxis, moveSign * (half + CubeSeamOutset));
		SetAxis(ref seamPos, downAxis, downSign * (half - seamHeight * 0.5f));

		_cubeSeam.Mesh = new BoxMesh
		{
			Size = size,
		};
		_cubeSeam.Position = seamPos;
	}

	private void SetCubeSeamState(float strength, bool visible)
	{
		_cubeSeamStrength = visible ? Mathf.Clamp(strength, 0f, 1f) : 0f;
		if (_cubeSeam != null && !visible)
		{
			_cubeSeam.Visible = false;
		}
	}

	private void UpdateCubeEffects(float delta)
	{
		_cubeEffectPulse += delta;

		if (_cubeSeam != null && _cubeSeamMat != null)
		{
			if (_cubeSeamStrength > 0.001f)
			{
				float pulse = 0.76f + 0.24f * Mathf.Sin(_cubeEffectPulse * 12f);
				float alpha = 0.18f + 0.74f * _cubeSeamStrength;
				_cubeSeam.Visible = true;
				_cubeSeamMat.AlbedoColor = new Color(1f, 0.84f, 0.32f, alpha);
				_cubeSeamMat.EmissionEnergyMultiplier = 1.4f + pulse * 2.1f * _cubeSeamStrength;
			}
			else
			{
				_cubeSeam.Visible = false;
			}
		}

		if (_cubeLandingRing != null && _cubeLandingRingMat != null)
		{
			if (_cubeLandingRingTimer > 0f)
			{
				_cubeLandingRingTimer = Mathf.Max(0f, _cubeLandingRingTimer - delta);
				float progress = 1f - (_cubeLandingRingTimer / CubeLandingRingDuration);
				float radius = Mathf.Lerp(LandingRadius * 0.18f, LandingRadius * 1.08f, progress);
				float alpha = Mathf.Pow(1f - progress, 2f);
				_cubeLandingRing.Visible = true;
				_cubeLandingRing.Position = new Vector3(0f, CubeGroundEffectHeight, 0f);
				_cubeLandingRing.Scale = new Vector3(radius, 1f, radius);
				_cubeLandingRingMat.AlbedoColor = new Color(0.14f, 0.08f, 0.02f, alpha * 0.9f);
				_cubeLandingRingMat.EmissionEnergyMultiplier = alpha * 1.6f;
			}
			else
			{
				_cubeLandingRing.Visible = false;
			}
		}

		float dustProgress = 1f;
		if (_cubeDustTimer > 0f)
		{
			_cubeDustTimer = Mathf.Max(0f, _cubeDustTimer - delta);
			dustProgress = 1f - (_cubeDustTimer / CubeDustBurstDuration);
		}

		Vector2 burstForward = RollDirection.LengthSquared() > 0.0001f
			? RollDirection.Normalized()
			: Vector2.Right;
		Vector2 burstSide = new Vector2(-burstForward.Y, burstForward.X);
		int directionalBurstCount = CubeDustBurstCount / 2;

		for (int i = 0; i < CubeDustBurstCount; i++)
		{
			MeshInstance3D? dust = _cubeDustMeshes[i];
			StandardMaterial3D? mat = _cubeDustMaterials[i];
			if (dust == null || mat == null)
			{
				continue;
			}

			if (_cubeDustTimer <= 0f)
			{
				dust.Visible = false;
				continue;
			}

			dust.Visible = true;

			if (i < directionalBurstCount)
			{
				float spread = directionalBurstCount <= 1
					? 0f
					: Mathf.Lerp(-1f, 1f, i / (float)(directionalBurstCount - 1));
				Vector2 offset = burstForward * Mathf.Lerp(CubeSideLength * 0.22f, LandingRadius * 0.92f, dustProgress)
					+ burstSide * (spread * Mathf.Lerp(CubeSideLength * 0.15f, LandingRadius * 0.56f, dustProgress));
				float height = CubeGroundEffectHeight + 0.08f + Mathf.Sin(dustProgress * Mathf.Pi) * 1.08f;
				float scale = Mathf.Lerp(0.88f, 3.35f, dustProgress);
				float alpha = Mathf.Pow(1f - dustProgress, 1.04f);

				dust.Position = new Vector3(offset.X, height, offset.Y);
				dust.Scale = new Vector3(scale * 1.72f, scale * 0.68f, scale * 1.72f);
				mat.AlbedoColor = new Color(0.38f, 0.28f, 0.16f, alpha);
				mat.EmissionEnergyMultiplier = alpha * 1.28f;
				continue;
			}

			int cloudIndex = i - directionalBurstCount;
			int cloudCount = CubeDustBurstCount - directionalBurstCount;
			float angle = (Mathf.Tau * cloudIndex / Mathf.Max(1, cloudCount)) + FaceIndex * 0.19f;
			float distance = Mathf.Lerp(CubeSideLength * 0.16f, LandingRadius * 1.32f, dustProgress);
			float scaleCloud = Mathf.Lerp(1.05f, 4.10f, dustProgress);
			float alphaCloud = Mathf.Pow(1f - dustProgress, 0.86f) * 0.72f;

			dust.Position = new Vector3(Mathf.Cos(angle) * distance, CubeGroundEffectHeight + 0.05f + dustProgress * 0.12f, Mathf.Sin(angle) * distance);
			dust.Scale = new Vector3(scaleCloud * 1.95f, scaleCloud * 0.34f, scaleCloud * 1.95f);
			mat.AlbedoColor = new Color(0.30f, 0.22f, 0.13f, alphaCloud);
			mat.EmissionEnergyMultiplier = alphaCloud * 0.58f;
		}
	}

	private static Vector3 TransformLocalOffset(Basis basis, Vector3 scale, Vector3 localPoint)
	{
		return basis.X * (localPoint.X * scale.X)
			+ basis.Y * (localPoint.Y * scale.Y)
			+ basis.Z * (localPoint.Z * scale.Z);
	}

	private Vector3 GetCubeDisplayPosition(float angleMagnitude, float rootTravelDistance = 0f)
	{
		Vector3 restOrigin = new Vector3(0f, CubeVisualHeight, 0f);
		Basis displayBasis = GetCubeDisplayBasis(angleMagnitude);
		Vector3 pivotAnchor = restOrigin + TransformLocalOffset(CubeVisualBasis, CubeScale, CubePivotLocalMesh);
		Vector3 localPosition = pivotAnchor - TransformLocalOffset(displayBasis, CubeScale, CubePivotLocalMesh);
		if (rootTravelDistance > 0.0001f)
		{
			localPosition -= new Vector3(RollDirection.X * rootTravelDistance, 0f, RollDirection.Y * rootTravelDistance);
		}

		return localPosition;
	}

	private Vector3 ToCubeLocalDirection(Vector3 worldDirection)
	{
		return new Vector3(
			CubeVisualBasis.X.Dot(worldDirection),
			CubeVisualBasis.Y.Dot(worldDirection),
			CubeVisualBasis.Z.Dot(worldDirection));
	}

	private static int GetDominantAxisIndex(Vector3 value)
	{
		float absX = Mathf.Abs(value.X);
		float absY = Mathf.Abs(value.Y);
		float absZ = Mathf.Abs(value.Z);
		if (absX >= absY && absX >= absZ)
		{
			return 0;
		}

		return absY >= absZ ? 1 : 2;
	}

	private static float GetSignedHalf(Vector3 value, int axis, float half)
	{
		return GetAxis(value, axis) >= 0f ? half : -half;
	}

	private static float GetAxis(Vector3 value, int axis)
	{
		return axis switch
		{
			0 => value.X,
			1 => value.Y,
			_ => value.Z,
		};
	}

	private static void SetAxis(ref Vector3 value, int axis, float component)
	{
		if (axis == 0)
		{
			value.X = component;
		}
		else if (axis == 1)
		{
			value.Y = component;
		}
		else
		{
			value.Z = component;
		}
	}

	private static float WrapAngle(float ang)
	{
		while (ang > Mathf.Pi)
		{
			ang -= Mathf.Tau;
		}

		while (ang < -Mathf.Pi)
		{
			ang += Mathf.Tau;
		}

		return ang;
	}
}
