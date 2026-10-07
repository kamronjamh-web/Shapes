using Godot;

namespace SuperGatchaRebuild;

public partial class Main
{
	private void TickBoss(float dt)
	{
		if (_boss == null || !IsInstanceValid(_boss))
		{
			_boss = null;
			_bossLaserActive = false;
			_bossBeam.Disable();
			return;
		}

		if (!_boss.IsAlive)
		{
			CleanupDefeatedBoss();
			return;
		}

		_bossLaserTick = Mathf.Max(0f, _bossLaserTick - dt);

		if (_boss.Kind == BossKind.Overseer)
		{
			TickOverseer(dt);
		}
		else if (_boss.Kind == BossKind.Cube)
		{
			TickCube(dt);
		}
		else
		{
			TickMonolith(dt);
		}

		bool slowTurn = _boss.Kind == BossKind.Overseer && (_boss.Phase == BossPhase.LaserTelegraph || _boss.Phase == BossPhase.LaserTrack);
		if (_boss.Kind != BossKind.Cube)
		{
			_boss.UpdateAim(dt, _player.Position, slowTurn);
		}

		if (_boss.Hp <= 0f)
		{
			CleanupDefeatedBoss();
		}
	}

	private void CleanupDefeatedBoss()
	{
		if (_boss == null)
		{
			return;
		}

		Vector2 rewardPosition = _boss.Position;
		GrantBossDropRewards(rewardPosition);
		if (IsInstanceValid(_boss))
		{
			_boss.QueueFree();
		}

		_boss = null;
		_bossLaserActive = false;
		_bossBeam.Disable();
	}

	private void TickMonolith(float dt)
	{
		if (_boss == null)
		{
			return;
		}

		if (_boss.Phase == BossPhase.Move)
		{
			Vector2 dir = RuntimeMath.SafeDirection(_player.Position - _boss.Position, Vector2.Right);
			_boss.Position += dir * _boss.MoveSpeed * dt;
			_boss.Position = ClampResolve(_boss.Position, _boss.Radius);

			_boss.BurstCd -= dt;
			if (_boss.BurstCd <= 0f)
			{
				_boss.BurstCd = 2f;
				int count = 18;
				float burstDmg = (15f + _waveNum * 1.1f) * _bossDmgScale;
				for (int i = 0; i < count; i++)
				{
					float a = (float)i / count * Mathf.Tau;
					Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
					SpawnBullet(_boss.Position, d * 35f, burstDmg, TeamId.Monster, new Color(0.9f, 0.22f, 0.95f), 1.1f, 4f);
				}
			}

			_boss.AnnihilateCd -= dt;
			if (_boss.AnnihilateCd <= 0f)
			{
				_boss.Phase = BossPhase.Charging;
				_boss.PhaseTimer = 0f;
			}
		}
		else if (_boss.Phase == BossPhase.Charging)
		{
			_boss.PhaseTimer += dt;
			if (_boss.PhaseTimer >= 3.0f)
			{
				_boss.Phase = BossPhase.Annihilate;
				_boss.PhaseTimer = 0f;
			}
		}
		else if (_boss.Phase == BossPhase.Annihilate)
		{
			_boss.PhaseTimer += dt;
			float dps = _player.MaxHp * 0.26f * Mathf.Clamp(_bossDmgScale, 1f, 2.3f);
			if (_bossLaserTick <= 0f)
			{
				_bossLaserTick = 0.08f;
				if (!IsBlockedByPillar(_boss.Position, _player.Position))
				{
					ApplyPlayerDamage(dps * 0.08f);
				}
			}

			if (_boss.PhaseTimer >= 2.5f)
			{
				_boss.Phase = BossPhase.Move;
				_boss.PhaseTimer = 0f;
				_boss.AnnihilateCd = 20f;
			}
		}
	}

	private void TickOverseer(float dt)
	{
		if (_boss == null)
		{
			return;
		}

		const float orbitRange = 55f;
		const float laserTelegraph = 1.8f;
		const float laserTrackDur = 3.0f;
		const float laserHalfWidth = 3.5f;
		const float laserLen = 360f;
		const float laserTrackRate = 2.2f;
		float laserDpsPct = Mathf.Clamp(0.50f + (_bossDmgScale - 1f) * 0.05f, 0.50f, 0.72f);

		Vector2 toPlayer = _player.Position - _boss.Position;
		float dist = Mathf.Max(0.001f, toPlayer.Length());
		Vector2 n = toPlayer / dist;

		if (_boss.Phase == BossPhase.Orbit)
		{
			_boss.OrbitPlayerVel = _boss.OrbitPlayerVel.Lerp(_playerVel, Mathf.Clamp(dt * 2.5f, 0f, 1f));

			float radialErr = dist - orbitRange;
			float radialSpd = Mathf.Clamp(radialErr * 0.8f, -_boss.MoveSpeed * 1.5f, _boss.MoveSpeed * 1.5f);

			float dot = _boss.OrbitPlayerVel.Dot(n);
			Vector2 lateralPlayerVel = _boss.OrbitPlayerVel - n * dot;

			_boss.OrbitAng += 0.28f * dt;
			Vector2 natural = new Vector2(Mathf.Cos(_boss.OrbitAng), Mathf.Sin(_boss.OrbitAng)) * (_boss.MoveSpeed * 0.5f);

			Vector2 moveVel = n * radialSpd + lateralPlayerVel * 0.9f + natural;
			float maxSpd = _boss.MoveSpeed * 1.8f;
			if (moveVel.Length() > maxSpd)
			{
				moveVel = moveVel.Normalized() * maxSpd;
			}

			_boss.Position += moveVel * dt;
			_boss.Position = ClampResolve(_boss.Position, _boss.Radius);

			_boss.ShootCd -= dt;
			if (_boss.ShootCd <= 0f)
			{
				_boss.ShootCd = 1.8f;
				Vector2 lead = _player.Position + _boss.OrbitPlayerVel * 0.6f;
				Vector2 dir = RuntimeMath.SafeDirection(lead - _boss.Position, n);
				float shotDmg = (17f + _waveNum * 1.25f) * _bossDmgScale;
				SpawnBullet(_boss.Position, dir * 26f, shotDmg, TeamId.Monster, new Color(1f, 0.42f, 0.18f), 1.1f, 4f);
			}

			_boss.LaserCd -= dt;
			if (_boss.LaserCd <= 0f && dist < orbitRange + 50f)
			{
				_boss.Phase = BossPhase.LaserTelegraph;
				_boss.PhaseTimer = 0f;
				_boss.LaserAng = Mathf.Atan2(_player.Position.Y - _boss.Position.Y, _player.Position.X - _boss.Position.X);
			}

			_bossBeam.Disable();
			_bossLaserActive = false;
		}
		else if (_boss.Phase == BossPhase.LaserTelegraph)
		{
			_boss.PhaseTimer += dt;

			float target = Mathf.Atan2(_player.Position.Y - _boss.Position.Y, _player.Position.X - _boss.Position.X);
			_boss.LaserAng += WrapAngle(target - _boss.LaserAng) * Mathf.Min(1f, dt * 2f);

			float hitLen = RaycastPillarDistance(_boss.Position, _boss.LaserAng, laserLen, 10f);
			Vector2 end = _boss.Position + AngleToDir(_boss.LaserAng) * hitLen;
			_bossBeam.SetBeam(_boss.Position, end, laserHalfWidth, new Color(1f, 0.55f, 0.1f), true);
			_bossLaserActive = true;

			if (_boss.PhaseTimer >= laserTelegraph)
			{
				_boss.Phase = BossPhase.LaserTrack;
				_boss.PhaseTimer = 0f;
			}
		}
		else if (_boss.Phase == BossPhase.LaserTrack)
		{
			_boss.PhaseTimer += dt;

			float target = Mathf.Atan2(_player.Position.Y - _boss.Position.Y, _player.Position.X - _boss.Position.X);
			_boss.LaserAng += WrapAngle(target - _boss.LaserAng) * Mathf.Min(1f, dt * laserTrackRate);

			float hitLen = RaycastPillarDistance(_boss.Position, _boss.LaserAng, laserLen, 10f);
			Vector2 end = _boss.Position + AngleToDir(_boss.LaserAng) * hitLen;
			_bossBeam.SetBeam(_boss.Position, end, laserHalfWidth, new Color(1f, 0.1f, 0.05f), false);
			_bossLaserActive = true;

			float perpDist = DistancePointToSegment(_player.Position, _boss.Position, end);
			Vector2 laserDirection = RuntimeMath.SafeDirection(end - _boss.Position, Vector2.Right);
			float proj = ((_player.Position - _boss.Position).Dot(laserDirection));
			if (proj > 0f && proj < hitLen && perpDist < laserHalfWidth + _player.Radius && !IsBlockedByPillar(_boss.Position, _player.Position))
			{
				ApplyPlayerDamage(_player.MaxHp * laserDpsPct * dt);
			}

			if (_boss.PhaseTimer >= laserTrackDur)
			{
				_boss.Phase = BossPhase.LaserCooldown;
				_boss.PhaseTimer = 0f;
				_bossLaserActive = false;
				_bossBeam.Disable();
			}
		}
		else if (_boss.Phase == BossPhase.LaserCooldown)
		{
			_boss.PhaseTimer += dt;
			if (_boss.PhaseTimer >= 0.8f)
			{
				_boss.Phase = BossPhase.Orbit;
				_boss.PhaseTimer = 0f;
				_boss.LaserCd = 11f;
				_boss.OrbitAng = Mathf.Atan2(_boss.Position.Y - _player.Position.Y, _boss.Position.X - _player.Position.X);
			}
		}
	}

	private void TickCube(float dt)
	{
		if (_boss == null)
		{
			return;
		}

		if (_boss.Phase == BossPhase.RollTelegraph)
		{
			if (_boss.ComboRollsRemaining <= 0)
			{
				_boss.ComboRollsRemaining = GetCubeRollComboCount(_boss);
			}

			if (_boss.PhaseTimer <= 0f)
			{
				_boss.PrepareCubeRoll(ChooseCubeRollDirection());
			}

			_boss.PhaseTimer += dt;
			_boss.SetCubeTelegraphVisual(_boss.PhaseTimer / CubeRollTelegraphDuration);
			if (_boss.PhaseTimer >= CubeRollTelegraphDuration)
			{
				StartCubeRoll();
			}

			return;
		}

		if (_boss.Phase == BossPhase.Rolling)
		{
			_boss.PhaseTimer += dt;
			float progress = Mathf.Clamp(_boss.PhaseTimer / Mathf.Max(0.01f, _boss.RollDuration), 0f, 1f);
			float travelProgress = GetCubeRollTravelProgress(progress);
			_boss.Position = _boss.RollStart.Lerp(_boss.RollTarget, travelProgress);
			_boss.SetCubeRollVisual(progress, travelProgress * _boss.RollDistance);
			if (progress >= 1f)
			{
				_boss.Position = _boss.RollTarget;
				_boss.FinishCubeRollVisual();
				_boss.Phase = BossPhase.Landing;
				_boss.PhaseTimer = 0f;
			}

			return;
		}

		if (_boss.Phase == BossPhase.Landing)
		{
			if (_boss.PhaseTimer <= 0f)
			{
				if (_cubeLandingFromTeleport)
				{
					TriggerCubeTeleportLanding(_boss);
				}
				else
				{
					TriggerCubeLanding(_boss);
					_boss.TriggerCubeLandingEffects();
				}
			}

			_boss.PhaseTimer += dt;
			_boss.SetCubeLandingVisual(_boss.PhaseTimer / CubeLandingDuration);

			if (_cubeLandingFromTeleport
				&& !_cubeTeleportLandingAftershockTriggered
				&& _boss.PhaseTimer >= CubeTeleportLandingSecondShakeDelay)
			{
				TriggerCubeTeleportLandingAftershock(_boss);
				_cubeTeleportLandingAftershockTriggered = true;
			}

			float landingDuration = _cubeLandingFromTeleport
				? CubeTeleportLandingPauseDuration
				: CubeLandingDuration;
			if (_boss.PhaseTimer >= landingDuration)
			{
				if (_cubeLandingFromTeleport)
				{
					_cubeLandingFromTeleport = false;
					_cubeTeleportLandingAftershockTriggered = false;
					_boss.ResetCubePose();
					_boss.Phase = BossPhase.RollTelegraph;
				}
				else
				{
					_boss.Phase = BossPhase.Recover;
				}
				_boss.PhaseTimer = 0f;
			}

			return;
		}

		if (_boss.Phase == BossPhase.Recover)
		{
			float recoverDuration = GetCubeRecoverDuration(_boss);
			_boss.PhaseTimer += dt;
			_boss.SetCubeRecoverVisual(_boss.PhaseTimer / recoverDuration);
			if (_boss.PhaseTimer >= recoverDuration)
			{
				_boss.ResetCubePose();
				if (_boss.ComboRollsRemaining > 0)
				{
					_boss.Phase = BossPhase.RollTelegraph;
				}
				else
				{
					_boss.Phase = ShouldCubeTeleport() ? BossPhase.TeleportAscend : BossPhase.RollTelegraph;
				}
				_boss.PhaseTimer = 0f;
			}

			return;
		}

		if (_boss.Phase == BossPhase.TeleportAscend)
		{
			_boss.PhaseTimer += dt;
			_boss.SetCubeTeleportAscendVisual(_boss.PhaseTimer / CubeTeleportAscendDuration);
			if (_boss.PhaseTimer >= CubeTeleportAscendDuration)
			{
				Vector2 destination = FindCubeTeleportDestination();
				_boss.Position = destination;
				TriggerArenaTeleportDestinationCue(destination);
				_boss.Phase = BossPhase.TeleportDescend;
				_boss.PhaseTimer = 0f;
			}

			return;
		}

		if (_boss.Phase == BossPhase.TeleportDescend)
		{
			_boss.PhaseTimer += dt;
			_boss.SetCubeTeleportDescendVisual(_boss.PhaseTimer / CubeTeleportDescendDuration);
			if (_boss.PhaseTimer >= CubeTeleportDescendDuration)
			{
				_boss.ResetCubePose();
				_cubeLandingFromTeleport = true;
				_cubeTeleportLandingAftershockTriggered = false;
				_cubePendingRollCrushCount = 0;
				_boss.Phase = BossPhase.Landing;
				_boss.PhaseTimer = 0f;
			}
		}
	}

	private void StartCubeRoll()
	{
		if (_boss == null)
		{
			return;
		}

		Vector2 direction = _boss.RollDirection.LengthSquared() > 0.0001f
			? _boss.RollDirection
			: ChooseCubeRollDirection();
		int crushedCoverCount = CrushCubeRollCover(_boss.Position, direction);
		_cubePendingRollCrushCount = crushedCoverCount;
		float clearance = GetCubeRollClearance(direction);
		if (clearance + CubeRollClearanceTolerance < _boss.RollDistance)
		{
			_cubePendingRollCrushCount = 0;
			_boss.ComboRollsRemaining = 0;
			_boss.Phase = BossPhase.Recover;
			_boss.PhaseTimer = 0f;
			_boss.ResetCubePose();
			return;
		}

		_boss.PrepareCubeRoll(direction);
		_boss.BeginCubeRoll();
		_boss.RollStart = _boss.Position;
		_boss.RollTarget = _boss.Position + direction * _boss.RollDistance;
		_boss.ComboRollsRemaining = Mathf.Max(0, _boss.ComboRollsRemaining - 1);
		_boss.Phase = BossPhase.Rolling;
		_boss.PhaseTimer = 0f;
	}

	private Vector2 ChooseCubeRollDirection()
	{
		if (_boss == null)
		{
			return Vector2.Right;
		}

		Vector2 toPlayer = _player.Position - _boss.Position;
		Vector2 chaseDirection = RuntimeMath.SafeDirection(toPlayer, Vector2.Right);
		Vector2[] candidates = { Vector2.Right, Vector2.Left, Vector2.Down, Vector2.Up };
		Vector2 bestDirection = Vector2.Right;
		float bestScore = float.NegativeInfinity;

		foreach (Vector2 candidate in candidates)
		{
			float clearance = GetCubeRollClearance(candidate);
			Vector2 projected = _boss.Position + candidate * Mathf.Min(clearance, _boss.RollDistance);
			float alignment = chaseDirection.Dot(candidate);
			float distanceScore = -(_player.Position - projected).LengthSquared() / Mathf.Max(1f, _boss.RollDistance * _boss.RollDistance);
			float clearanceScore = Mathf.Min(clearance / Mathf.Max(0.001f, _boss.RollDistance), 1f);
			float score = alignment * 2.8f + clearanceScore * 1.6f + distanceScore;

			if (clearance + CubeRollClearanceTolerance < _boss.RollDistance)
			{
				score -= 4f;
			}

			if (score > bestScore)
			{
				bestScore = score;
				bestDirection = candidate;
			}
		}

		return bestDirection;
	}

	private float GetCubeRollClearance(Vector2 direction)
	{
		if (_boss == null)
		{
			return 0f;
		}

		float maxDistance = _boss.RollDistance + _boss.Radius + CubeRollStopPadding;
		float angle = Mathf.Atan2(direction.Y, direction.X);
		float hitDistance = RaycastPillarDistance(_boss.Position, angle, maxDistance, _boss.Radius, ignoreCubeCrushable: true);
		float clearance = hitDistance - (_boss.Radius + CubeRollStopPadding);
		return Mathf.Max(0.5f, clearance);
	}

	private void TriggerCubeLanding(BossActor boss)
	{
		int crushedCoverCount = _cubePendingRollCrushCount;
		_cubePendingRollCrushCount = 0;
		PlayWorldScreenSpaceSfx(
			_cubeFaceLandingSfx,
			new Vector3(boss.Position.X, 0f, boss.Position.Y),
			panningStrength: CubeFaceLandingPanStrength);
		float playerDistance = (_player.Position - boss.Position).Length();
		float damageRadius = boss.LandingRadius + _player.Radius;
		if (playerDistance <= damageRadius && !IsBlockedByPillar(boss.Position, _player.Position))
		{
			float proximity = 1f - Mathf.Clamp(playerDistance / Mathf.Max(1f, damageRadius), 0f, 1f);
			float damage = boss.LandingDamage * (0.7f + proximity * 0.55f) * _bossDmgScale;
			ApplyPlayerDamage(damage);
		}

		if (playerDistance <= boss.LandingShakeRadius)
		{
			ApplyCubeScreenShake(
				boss.Position,
				boss.LandingShakeRadius,
				boss.LandingShakeAmount + CubeTurnShakeAmount + crushedCoverCount * 0.18f,
				0.18f + crushedCoverCount * 0.03f);
		}

		TriggerCubeArenaImpact(
			boss.Position,
			1f,
			CubeArenaCoverChance,
			new Color(1f, 0.68f, 0.24f, 1f));
	}

	private void TriggerCubeTeleportLanding(BossActor boss)
	{
		boss.TriggerCubeLandingEffects();
		CrushCubeOverlapPillars(boss.Position, boss.Radius + CubeRollStopPadding);
		ApplyCubeScreenShake(
			boss.Position,
			boss.LandingShakeRadius * 1.2f,
			boss.LandingShakeAmount * CubeTeleportLandingInitialShakeScale,
			0.28f);
		TriggerCubeArenaImpact(
			boss.Position,
			CubeTeleportLandingImpactStrength,
			CubeArenaCoverTeleportChance,
			new Color(0.84f, 0.97f, 1f, 1f));
		SpawnCubeTeleportRuptureCover(boss.Position);
	}

	private void TriggerCubeTeleportLandingAftershock(BossActor boss)
	{
		ApplyCubeScreenShake(
			boss.Position,
			boss.LandingShakeRadius * 1.3f,
			boss.LandingShakeAmount * CubeTeleportLandingShakeScale,
			0.44f);
	}

	private void ApplyCubeScreenShake(Vector2 source, float radius, float amount, float minimumAmount)
	{
		if (!IsInstanceValid(_camera))
		{
			return;
		}

		float distance = (_player.Position - source).Length();
		float arenaReach = Mathf.Sqrt(ArenaHalfW * ArenaHalfW + ArenaHalfH * ArenaHalfH);
		float effectiveRadius = Mathf.Max(radius, arenaReach * 0.95f);
		float falloff = 1f - Mathf.Clamp(distance / Mathf.Max(1f, effectiveRadius), 0f, 1f);
		falloff = Mathf.Pow(falloff, 0.58f);
		_camera.AddScreenShake(Mathf.Max(minimumAmount, amount * falloff));
	}

	private int CrushCubeRollCover(Vector2 start, Vector2 direction)
	{
		if (_boss == null || direction.LengthSquared() <= 0.0001f)
		{
			return 0;
		}

		float maxDistance = _boss.RollDistance + _boss.Radius + CubeRollStopPadding;
		Vector2 end = start + direction.Normalized() * maxDistance;
		float crushRadius = _boss.Radius + CubeRollStopPadding;
		int crushedCount = 0;

		for (int i = _pillars.Count - 1; i >= 0; i--)
		{
			if (!IsCubeCrushablePillar(i))
			{
				continue;
			}

			if (!SegmentIntersectsExpandedAabb(start, end, _pillars[i], crushRadius, out _))
			{
				continue;
			}

			DestroyArenaPillar(i);
			crushedCount++;
		}

		return crushedCount;
	}

	private int CrushCubeOverlapPillars(Vector2 center, float radius)
	{
		int crushedCount = 0;

		for (int i = _pillars.Count - 1; i >= 0; i--)
		{
			if (!IsCubeCrushablePillar(i))
			{
				continue;
			}

			if (!ExpandRect(_pillars[i], radius).HasPoint(center))
			{
				continue;
			}

			DestroyArenaPillar(i);
			crushedCount++;
		}

		return crushedCount;
	}

	private bool IsCubeCrushablePillar(int index)
	{
		return index >= 0
			&& index < _arenaPillarVisuals.Count;
	}

	private void DestroyArenaPillar(int index)
	{
		if (index < 0 || index >= _pillars.Count)
		{
			return;
		}

		_pillars.RemoveAt(index);

		if (index < _pillarHeights.Count)
		{
			_pillarHeights.RemoveAt(index);
		}

		if (index >= _arenaPillarVisuals.Count)
		{
			return;
		}

		ArenaPillarVisual pillarVisual = _arenaPillarVisuals[index];
		if (pillarVisual.DynamicCover)
		{
			_cubeDynamicCoverCount = Mathf.Max(0, _cubeDynamicCoverCount - 1);
		}

		if (IsInstanceValid(pillarVisual.Mesh))
		{
			pillarVisual.Mesh.QueueFree();
		}

		_arenaPillarVisuals.RemoveAt(index);
	}

	private void TriggerCubeArenaImpact(Vector2 origin, float strength, float coverChance, Color? pulseColor = null)
	{
		float furthestTravel = Mathf.Max(
			Mathf.Max(Mathf.Abs(origin.X + ArenaHalfW), Mathf.Abs(origin.X - ArenaHalfW)),
			Mathf.Max(Mathf.Abs(origin.Y + ArenaHalfH), Mathf.Abs(origin.Y - ArenaHalfH)));

		_cubeArenaImpactWaves.Add(new CubeArenaImpactWave
		{
			Origin = origin,
			Age = 0f,
			Speed = CubeArenaWaveSpeed,
			Width = CubeArenaWaveWidth,
			Duration = (furthestTravel / CubeArenaWaveSpeed) + CubeArenaWaveTail,
			Strength = strength,
			Tint = pulseColor ?? new Color(1f, 0.68f, 0.24f, 1f),
		});

		TrySpawnCubeCover(origin, coverChance);
	}

	private static int GetCubeRollComboCount(BossActor boss)
	{
		float hpPct = boss.Hp / Mathf.Max(1f, boss.MaxHp);
		if (hpPct <= 0.35f)
		{
			return 3;
		}

		if (hpPct <= 0.70f)
		{
			return 2;
		}

		return 1;
	}

	private static float GetCubeRecoverDuration(BossActor boss)
	{
		return boss.ComboRollsRemaining > 0 ? CubeComboRecoverDuration : CubeChainRecoverDuration;
	}

	private static float GetCubeRollTravelProgress(float progress)
	{
		float angle = Mathf.Clamp(progress, 0f, 1f) * Mathf.Pi * 0.5f;
		return 0.5f * (1f - Mathf.Cos(angle) + Mathf.Sin(angle));
	}

	private bool ShouldCubeTeleport()
	{
		if (_boss == null)
		{
			return false;
		}

		if (_camera == null || !GodotObject.IsInstanceValid(_camera))
		{
			return (_boss.Position - _player.Position).Length() > CubeTeleportMaxDistance * 1.2f;
		}

		Vector3 bossWorldPosition = _boss.GlobalPosition;
		if (_camera.IsPositionBehind(bossWorldPosition))
		{
			return true;
		}

		Rect2 expandedVisibleRect = GetViewport().GetVisibleRect().Grow(CubeTeleportScreenMargin);
		Vector2 screenPosition = _camera.UnprojectPosition(bossWorldPosition);
		return !expandedVisibleRect.HasPoint(screenPosition);
	}

	private Vector2 FindCubeTeleportDestination()
	{
		if (_boss == null)
		{
			return _player.Position;
		}

		float minDistance = Mathf.Max(CubeTeleportMinDistance, _boss.Radius + _player.Radius + 8f);
		float maxDistance = Mathf.Max(minDistance + 4f, CubeTeleportMaxDistance);
		Vector2 fallbackDirection = RuntimeMath.SafeDirection(_boss.Position - _player.Position, Vector2.Right);
		Vector2 fallback = ClampResolve(_player.Position + fallbackDirection * maxDistance, _boss.Radius, ignoreCubeCrushable: true);
		Vector2 best = fallback;
		float bestScore = float.NegativeInfinity;

		for (int i = 0; i < 14; i++)
		{
			float angle = _rng.RandfRange(-Mathf.Pi, Mathf.Pi);
			float distance = _rng.RandfRange(minDistance, maxDistance);
			Vector2 candidate = ClampResolve(_player.Position + AngleToDir(angle) * distance, _boss.Radius, ignoreCubeCrushable: true);
			if (IsPointBlockedByPillar(candidate, _boss.Radius * 0.8f, ignoreCubeCrushable: true))
			{
				continue;
			}

			float playerDistance = (candidate - _player.Position).Length();
			float score = -Mathf.Abs(playerDistance - ((minDistance + maxDistance) * 0.5f));
			if (!IsBlockedByPillar(candidate, _player.Position, ignoreCubeCrushable: true))
			{
				score += 0.9f;
			}

			if (score > bestScore)
			{
				bestScore = score;
				best = candidate;
			}
		}

		return best;
	}
}