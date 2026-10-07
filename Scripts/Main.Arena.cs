using Godot;

namespace SuperGatchaRebuild;

public partial class Main
{
	private void BuildArenaBackdrop()
	{
		_arenaGridLines.Clear();
		_arenaWalls.Clear();
		_arenaPillarVisuals.Clear();
		_cubeArenaImpactWaves.Clear();
		_cubeDynamicCoverCount = 0;

		_arenaEnvironmentNode = new WorldEnvironment();
		_arenaEnvironment = new Godot.Environment()
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.012f, 0.016f, 0.036f, 1f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.28f, 0.33f, 0.42f, 1f),
			AmbientLightEnergy = 0.18f,
		};
		_arenaEnvironmentNode.Environment = _arenaEnvironment;
		AddChild(_arenaEnvironmentNode);

		MeshInstance3D floor = new();
		floor.Mesh = new BoxMesh { Size = new Vector3(ArenaLayout.ArenaWidth, 2f, ArenaLayout.ArenaHeight) };
		floor.Position = new Vector3(0f, -1f, 0f);
		floor.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		floor.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.055f, 0.064f, 0.095f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0.010f, 0.016f, 0.028f, 1f),
			EmissionEnergyMultiplier = 0.10f,
			Metallic = 0.14f,
			Roughness = 0.88f,
		};
		AddChild(floor);

		const float gridStep = ArenaLayout.ArenaWidth / 80f;
		const float gridLineThickness = 0.14f;
		for (float x = -ArenaHalfW; x <= ArenaHalfW; x += gridStep)
		{
			StandardMaterial3D gridMat = CreateArenaGridMaterial();
			MeshInstance3D mesh = CreateBackdropBlock(
				new Vector3(x, -0.15f, 0f),
				new Vector3(gridLineThickness, 0.35f, ArenaLayout.ArenaHeight),
				gridMat,
				GeometryInstance3D.ShadowCastingSetting.Off);
			StandardMaterial3D pulseMat = CreateArenaGridShockwaveMaterial();
			MeshInstance3D pulseMesh = CreateBackdropBlock(
				new Vector3(x, -0.05f, 0f),
				new Vector3(gridLineThickness * 1.35f, 0.40f, ArenaLayout.ArenaHeight),
				pulseMat,
				GeometryInstance3D.ShadowCastingSetting.Off);
			pulseMesh.Visible = false;
			_arenaGridLines.Add(new ArenaGridLineVisual
			{
				Mesh = mesh,
				Material = gridMat,
				PulseMesh = pulseMesh,
				PulseMaterial = pulseMat,
				BasePulsePosition = pulseMesh.Position,
				IsVertical = true,
				AxisCoord = x,
			});
		}

		for (float y = -ArenaHalfH; y <= ArenaHalfH; y += gridStep)
		{
			StandardMaterial3D gridMat = CreateArenaGridMaterial();
			MeshInstance3D mesh = CreateBackdropBlock(
				new Vector3(0f, -0.15f, y),
				new Vector3(ArenaLayout.ArenaWidth, 0.35f, gridLineThickness),
				gridMat,
				GeometryInstance3D.ShadowCastingSetting.Off);
			StandardMaterial3D pulseMat = CreateArenaGridShockwaveMaterial();
			MeshInstance3D pulseMesh = CreateBackdropBlock(
				new Vector3(0f, -0.05f, y),
				new Vector3(ArenaLayout.ArenaWidth, 0.40f, gridLineThickness * 1.35f),
				pulseMat,
				GeometryInstance3D.ShadowCastingSetting.Off);
			pulseMesh.Visible = false;
			_arenaGridLines.Add(new ArenaGridLineVisual
			{
				Mesh = mesh,
				Material = gridMat,
				PulseMesh = pulseMesh,
				PulseMaterial = pulseMat,
				BasePulsePosition = pulseMesh.Position,
				IsVertical = false,
				AxisCoord = y,
			});
		}

		float wallThickness = ArenaLayout.WallThickness;
		float wallHeight = ArenaLayout.WallHeight;
		StandardMaterial3D northMat = CreateArenaWallMaterial();
		MeshInstance3D northWall = CreateBackdropBlock(
			new Vector3(0f, wallHeight * 0.5f, -ArenaHalfH - wallThickness * 0.5f),
			new Vector3(ArenaLayout.ArenaWidth + wallThickness * 2f, wallHeight, wallThickness),
			northMat);
		_arenaWalls.Add(new ArenaWallVisual { Mesh = northWall, Material = northMat, UsesXAxis = false, Boundary = -ArenaHalfH });

		StandardMaterial3D southMat = CreateArenaWallMaterial();
		MeshInstance3D southWall = CreateBackdropBlock(
			new Vector3(0f, wallHeight * 0.5f, ArenaHalfH + wallThickness * 0.5f),
			new Vector3(ArenaLayout.ArenaWidth + wallThickness * 2f, wallHeight, wallThickness),
			southMat);
		_arenaWalls.Add(new ArenaWallVisual { Mesh = southWall, Material = southMat, UsesXAxis = false, Boundary = ArenaHalfH });

		StandardMaterial3D westMat = CreateArenaWallMaterial();
		MeshInstance3D westWall = CreateBackdropBlock(
			new Vector3(-ArenaHalfW - wallThickness * 0.5f, wallHeight * 0.5f, 0f),
			new Vector3(wallThickness, wallHeight, ArenaLayout.ArenaHeight),
			westMat);
		_arenaWalls.Add(new ArenaWallVisual { Mesh = westWall, Material = westMat, UsesXAxis = true, Boundary = -ArenaHalfW });

		StandardMaterial3D eastMat = CreateArenaWallMaterial();
		MeshInstance3D eastWall = CreateBackdropBlock(
			new Vector3(ArenaHalfW + wallThickness * 0.5f, wallHeight * 0.5f, 0f),
			new Vector3(wallThickness, wallHeight, ArenaLayout.ArenaHeight),
			eastMat);
		_arenaWalls.Add(new ArenaWallVisual { Mesh = eastWall, Material = eastMat, UsesXAxis = true, Boundary = ArenaHalfW });

		_arenaSun = new DirectionalLight3D
		{
			Name = "ArenaKeyLight",
			RotationDegrees = new Vector3(-58f, -34f, 0f),
			LightColor = new Color(0.62f, 0.72f, 0.90f, 1f),
			LightEnergy = 0.90f,
			ShadowEnabled = true,
		};
		AddChild(_arenaSun);

		_arenaPlayerLight = CreateArenaOmniLight(
			"ArenaPlayerFill",
			new Color(0.76f, 0.86f, 1f, 1f),
			22f,
			1.15f);
		_arenaBossLight = CreateArenaOmniLight(
			"ArenaBossAccent",
			new Color(1f, 0.58f, 0.22f, 1f),
			24f,
			0f);
		_arenaBossLight.Visible = false;
		_arenaImpactLights.Clear();
		for (int i = 0; i < ArenaImpactLightCount; i++)
		{
			OmniLight3D impactLight = CreateArenaOmniLight(
				$"ArenaImpactLight{i}",
				new Color(1f, 0.62f, 0.24f, 1f),
				18f,
				0f);
			impactLight.Visible = false;
			_arenaImpactLights.Add(impactLight);
		}

		_arenaPerimeterLights.Clear();
		Vector3[] perimeterLightPositions =
		{
			new Vector3(0f, wallHeight * 0.72f, -ArenaHalfH + wallThickness * 0.35f),
			new Vector3(0f, wallHeight * 0.72f, ArenaHalfH - wallThickness * 0.35f),
			new Vector3(-ArenaHalfW + wallThickness * 0.35f, wallHeight * 0.72f, 0f),
			new Vector3(ArenaHalfW - wallThickness * 0.35f, wallHeight * 0.72f, 0f),
		};
		for (int i = 0; i < perimeterLightPositions.Length; i++)
		{
			OmniLight3D perimeterLight = CreateArenaOmniLight(
				$"ArenaPerimeterLight{i}",
				new Color(0.62f, 0.78f, 1f, 1f),
				54f,
				0f);
			perimeterLight.Position = perimeterLightPositions[i];
			perimeterLight.Visible = false;
			_arenaPerimeterLights.Add(perimeterLight);
		}

		for (int i = 0; i < _pillars.Count; i++)
		{
			Rect2 pillar = _pillars[i];
			float pillarHeight = i < _pillarHeights.Count ? _pillarHeights[i] : ArenaLayout.PillarMinHeight;
			CreateArenaPillarVisual(pillar, pillarHeight, false);
		}

		_camera = new ArenaCamera();
		AddChild(_camera);
		_camera.Current = true;
		_camera.Near = 0.1f;
		_camera.Far = 1200f;
		_camera.Fov = 50f;
		_camera.Position = new Vector3(0f, 90f, 45f);
		_camera.LookAt(Vector3.Zero, Vector3.Up);
	}

	private MeshInstance3D CreateBackdropBlock(
		Vector3 position,
		Vector3 size,
		Material material,
		GeometryInstance3D.ShadowCastingSetting shadowCasting = GeometryInstance3D.ShadowCastingSetting.On)
	{
		MeshInstance3D block = new();
		block.Mesh = new BoxMesh { Size = size };
		block.Position = position;
		block.MaterialOverride = material;
		block.CastShadow = shadowCasting;
		AddChild(block);
		return block;
	}

	private OmniLight3D CreateArenaOmniLight(string name, Color lightColor, float range, float energy)
	{
		OmniLight3D light = new()
		{
			Name = name,
			Position = Vector3.Zero,
			OmniRange = range,
			LightColor = lightColor,
			LightEnergy = energy,
			ShadowEnabled = false,
		};
		AddChild(light);
		return light;
	}

	private StandardMaterial3D CreateArenaGridMaterial()
	{
		return new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.08f, 0.14f, 0.28f, 0.16f),
			EmissionEnabled = true,
			Emission = new Color(0.020f, 0.050f, 0.120f, 1f),
			EmissionEnergyMultiplier = 0.14f,
			Metallic = 0.02f,
			Roughness = 1.0f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
	}

	private StandardMaterial3D CreateArenaGridShockwaveMaterial()
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(1f, 0.82f, 0.24f, 0f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.76f, 0.20f, 1f),
			EmissionEnergyMultiplier = 0f,
		};
	}

	private StandardMaterial3D CreateArenaWallMaterial()
	{
		return new StandardMaterial3D
		{
			AlbedoColor = new Color(0.090f, 0.100f, 0.155f, 0.68f),
			EmissionEnabled = true,
			Emission = new Color(0.016f, 0.026f, 0.054f, 1f),
			EmissionEnergyMultiplier = 0.16f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Metallic = 0.05f,
			Roughness = 0.78f,
		};
	}

	private StandardMaterial3D CreateArenaPillarMaterial(bool dynamicCover)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = dynamicCover
				? new Color(0.100f, 0.100f, 0.150f, 1f)
				: new Color(0.074f, 0.084f, 0.128f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0.018f, 0.028f, 0.060f, 1f),
			EmissionEnergyMultiplier = dynamicCover ? 0.16f : 0.10f,
			Metallic = 0.06f,
			Roughness = 0.82f,
		};
	}

	private void CreateArenaPillarVisual(Rect2 pillar, float pillarHeight, bool dynamicCover)
	{
		StandardMaterial3D pillarMat = CreateArenaPillarMaterial(dynamicCover);
		MeshInstance3D mesh = new();
		mesh.Mesh = new BoxMesh { Size = new Vector3(pillar.Size.X, pillarHeight, pillar.Size.Y) };
		mesh.Position = new Vector3(
			pillar.Position.X + pillar.Size.X * 0.5f,
			pillarHeight * 0.5f,
			pillar.Position.Y + pillar.Size.Y * 0.5f);
		mesh.MaterialOverride = pillarMat;
		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		OmniLight3D surgeLight = new()
		{
			Name = "Surge",
			Position = new Vector3(0f, -pillarHeight * 0.30f, 0f),
			OmniRange = dynamicCover ? 18f : 14f,
			LightColor = new Color(1f, 0.72f, 0.24f, 1f),
			LightEnergy = 0f,
			ShadowEnabled = false,
			Visible = false,
		};
		AddChild(mesh);
		mesh.AddChild(surgeLight);
		_arenaPillarVisuals.Add(new ArenaPillarVisual
		{
			Mesh = mesh,
			Material = pillarMat,
			BasePosition = mesh.Position,
			SurgeLight = surgeLight,
			Height = pillarHeight,
			DynamicCover = dynamicCover,
		});
	}

	private void RefreshChallengePillarForWave()
	{
		ClearChallengePillar();

		if (TryStartMerchantEncounterForWave(_waveNum))
		{
			return;
		}

		if (!ShouldSpawnChallengePillarForWave(_waveNum))
		{
			return;
		}

		Vector2 pos = GetChallengePillarSpawnPosition();
		_challengePillar = new ArenaChallengePillarActor();
		AddChild(_challengePillar);
		_challengePillar.Configure(pos);
		TriggerArenaInteractableSpawnCue(pos);
	}

	private bool ShouldSpawnChallengePillarForWave(int wave)
	{
		return wave >= ChallengePillarWaveInterval
			&& wave % BossWaveInterval != 0
			&& wave % ChallengePillarWaveInterval == 0;
	}

	private Vector2 GetChallengePillarSpawnPosition()
	{
		float maxDistance = (Mathf.Min(ArenaHalfW, ArenaHalfH) - ArenaLayout.InteractableSpawnMargin) * 0.78f;
		const float minDistance = 30f;

		for (int attempt = 0; attempt < 18; attempt++)
		{
			float angle = _rng.RandfRange(0f, Mathf.Tau);
			float distance = _rng.RandfRange(minDistance, maxDistance);
			Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
			Vector2 pos = ClampResolve(_player.Position + offset, ArenaChallengePillarActor.ActivationRadius);

			if ((pos - _player.Position).LengthSquared() < minDistance * minDistance)
			{
				continue;
			}

			if (IsPointBlockedByPillar(pos, ArenaChallengePillarActor.ActivationRadius * 0.75f))
			{
				continue;
			}

			return pos;
		}

		return ClampResolve(new Vector2(0f, -ArenaHalfH * 0.28f), ArenaChallengePillarActor.ActivationRadius);
	}

	private void ActivateChallengePillar()
	{
		if (_challengePillar == null || !IsInstanceValid(_challengePillar) || _challengePillar.IsActivated)
		{
			return;
		}

		_challengePillar.Activate();

		int surgeCount = GetChallengePillarSurgeCount(_waveNum);
		_waveLeft += surgeCount;

		int instantSpawns = Mathf.Min(surgeCount, 2 + Mathf.FloorToInt(_waveNum / 6f));
		for (int i = 0; i < instantSpawns; i++)
		{
			SpawnEnemyForWave(_waveNum);
			_waveLeft = Mathf.Max(0, _waveLeft - 1);
		}

		_spawnCd = Mathf.Min(_spawnCd, 0.12f);
		ShowStatusBanner("CHALLENGE PILLAR AWAKENED");
	}

	private int GetChallengePillarSurgeCount(int wave)
	{
		return Mathf.Clamp(3 + Mathf.FloorToInt((wave - 1) * 0.35f), 3, 9);
	}

	private void CompleteChallengePillar()
	{
		if (_challengePillar == null || !IsInstanceValid(_challengePillar))
		{
			return;
		}

		Vector2 rewardPos = _challengePillar.Position;
		_challengePillar.Complete();

		SpawnEssencePickup(CreateRandomEssenceDrop(), rewardPos + new Vector2(-10f, 0f));
		SpawnModulatorPickup(CreateRandomModulatorDrop(), rewardPos + new Vector2(10f, 0f));

		if (_waveNum >= ChallengePillarWaveInterval * 2 && _waveNum % (ChallengePillarWaveInterval * 2) == 0)
		{
			SpawnShapeItemPickup(CreateRandomShapeDropItem(), rewardPos + new Vector2(0f, 12f));
		}

		ShowStatusBanner("CHALLENGE CACHE SECURED");
		ClearChallengePillar();
	}

	private void ClearChallengePillar()
	{
		if (_challengePillar != null && IsInstanceValid(_challengePillar))
		{
			_challengePillar.QueueFree();
		}

		_challengePillar = null;
	}

	private void UpdateArenaVisualEffects(float dt)
	{
		TrackArenaBossLightingState();
		UpdateCubeArenaImpactVisuals(dt);
		UpdateArenaLighting(dt);

		if (IsInstanceValid(_waveBanner) && _waveBannerTimer > 0f)
		{
			const float waveBannerLife = 1.25f;
			_waveBannerTimer = Mathf.Max(0f, _waveBannerTimer - dt);
			float t = 1f - (_waveBannerTimer / waveBannerLife);
			float alpha = t < 0.18f
				? t / 0.18f
				: Mathf.Clamp(1f - ((t - 0.18f) / 0.82f), 0f, 1f);

			_waveBanner.Modulate = new Color(1f, 0.96f, 0.80f, alpha);
			_waveBanner.Scale = Vector2.One * (0.82f + Mathf.Clamp(t * 1.3f, 0f, 1f) * 0.22f);
			if (_waveBannerTimer <= 0f)
			{
				_waveBanner.Visible = false;
			}
		}

		if (!IsInstanceValid(_hurtFlashOverlay))
		{
			return;
		}

		if (_hurtFlashTimer > 0f)
		{
			_hurtFlashTimer = Mathf.Max(0f, _hurtFlashTimer - dt);
		}

		float flashOpacityScale = _flashReductionEnabled ? 0.35f : 1f;
		float alphaTarget = _hurtFlashTimer > 0f
			? Mathf.Clamp(_hurtFlashTimer / 0.22f, 0f, 1f) * 0.45f * flashOpacityScale
			: 0f;
		Color c = _hurtFlashOverlay.Color;
		c.A = Mathf.Lerp(c.A, alphaTarget, Mathf.Clamp(dt * 16f, 0f, 1f));
		_hurtFlashOverlay.Color = c;

		if (!IsInstanceValid(_lowHpOverlay))
		{
			return;
		}

		float hpPct = _player.Hp / Mathf.Max(1f, _player.MaxHp);
		float targetLowHp = hpPct < 0.35f
			? 1f - Mathf.Max((hpPct - 0.10f) / 0.25f, 0f)
			: 0f;

		Color low = _lowHpOverlay.Color;
		float lowAlpha = targetLowHp * 0.16f * (_flashReductionEnabled ? 0.55f : 1f);
		low.A = Mathf.Lerp(low.A, lowAlpha, Mathf.Clamp(dt * 3f, 0f, 1f));
		_lowHpOverlay.Color = low;
	}

	private void UpdateCubeArenaImpactVisuals(float dt)
	{
		for (int i = _cubeArenaImpactWaves.Count - 1; i >= 0; i--)
		{
			CubeArenaImpactWave wave = _cubeArenaImpactWaves[i];
			wave.Age += dt;
			if (wave.Age >= wave.Duration)
			{
				_cubeArenaImpactWaves.RemoveAt(i);
			}
		}

		for (int i = _arenaPerimeterSweeps.Count - 1; i >= 0; i--)
		{
			ArenaPerimeterSweep sweep = _arenaPerimeterSweeps[i];
			sweep.Age += dt;
			if (sweep.Age >= sweep.Duration)
			{
				_arenaPerimeterSweeps.RemoveAt(i);
			}
		}

		for (int i = 0; i < _arenaGridLines.Count; i++)
		{
			ArenaGridLineVisual line = _arenaGridLines[i];
			line.Material.AlbedoColor = new Color(0.08f, 0.14f, 0.28f, 0.16f);
			line.Material.Emission = new Color(0.020f, 0.050f, 0.120f, 1f);
			line.Material.EmissionEnergyMultiplier = 0.14f;

			float bestResponse = 0f;
			float bestSegmentLength = 0f;
			float bestSegmentCenter = 0f;
			Color bestPulseColor = new Color(1f, 0.78f, 0.22f, 1f);
			for (int waveIndex = 0; waveIndex < _cubeArenaImpactWaves.Count; waveIndex++)
			{
				CubeArenaImpactWave wave = _cubeArenaImpactWaves[waveIndex];
				float axisDistance = line.IsVertical
					? Mathf.Abs(line.AxisCoord - wave.Origin.X)
					: Mathf.Abs(line.AxisCoord - wave.Origin.Y);
				float response = GetCubeArenaWaveResponse(axisDistance, wave);
				if (response <= bestResponse)
				{
					continue;
				}

				float wavefront = wave.Age * wave.Speed;
				float segmentHalfLength = wavefront + wave.Width * 0.65f;
				float axisHalf = line.IsVertical ? ArenaHalfH : ArenaHalfW;
				float originOnLine = line.IsVertical ? wave.Origin.Y : wave.Origin.X;
				float minAlong = Mathf.Max(-axisHalf, originOnLine - segmentHalfLength);
				float maxAlong = Mathf.Min(axisHalf, originOnLine + segmentHalfLength);
				float segmentLength = Mathf.Max(0f, maxAlong - minAlong);
				if (segmentLength <= 0.05f)
				{
					continue;
				}

				bestResponse = response;
				bestSegmentLength = segmentLength;
				bestSegmentCenter = (minAlong + maxAlong) * 0.5f;
				bestPulseColor = wave.Tint;
			}

			if (bestResponse <= 0.001f || bestSegmentLength <= 0.05f)
			{
				line.PulseMesh.Visible = false;
				continue;
			}

			line.PulseMesh.Visible = true;
			if (line.IsVertical)
			{
				line.PulseMesh.Position = new Vector3(line.AxisCoord, line.BasePulsePosition.Y, bestSegmentCenter);
				line.PulseMesh.Scale = new Vector3(
					1f + bestResponse * 0.45f,
					1f + bestResponse * 0.08f,
					bestSegmentLength / ArenaLayout.ArenaHeight);
			}
			else
			{
				line.PulseMesh.Position = new Vector3(bestSegmentCenter, line.BasePulsePosition.Y, line.AxisCoord);
				line.PulseMesh.Scale = new Vector3(
					bestSegmentLength / ArenaLayout.ArenaWidth,
					1f + bestResponse * 0.08f,
					1f + bestResponse * 0.45f);
			}

			line.PulseMaterial.AlbedoColor = new Color(
				bestPulseColor.R,
				bestPulseColor.G,
				bestPulseColor.B,
				0.18f + bestResponse * 0.78f);
			line.PulseMaterial.Emission = bestPulseColor;
			line.PulseMaterial.EmissionEnergyMultiplier = 1.2f + bestResponse * 4.2f;
		}

		for (int i = 0; i < _arenaWalls.Count; i++)
		{
			ArenaWallVisual wall = _arenaWalls[i];
			float response = 0f;
			Color impactColor = new Color(1f, 0.68f, 0.24f, 1f);
			for (int waveIndex = 0; waveIndex < _cubeArenaImpactWaves.Count; waveIndex++)
			{
				CubeArenaImpactWave wave = _cubeArenaImpactWaves[waveIndex];
				float distance = wall.UsesXAxis
					? Mathf.Abs(wall.Boundary - wave.Origin.X)
					: Mathf.Abs(wall.Boundary - wave.Origin.Y);
				float currentResponse = GetCubeArenaWaveResponse(distance, wave);
				if (currentResponse > response)
				{
					response = currentResponse;
					impactColor = wave.Tint;
				}
			}

			float perimeterResponse = GetArenaPerimeterResponse(i, out Color perimeterColor);
			float totalResponse = Mathf.Clamp(response + perimeterResponse * 0.85f, 0f, 1.25f);
			Color accentColor = response >= perimeterResponse ? impactColor : perimeterColor;
			Color baseWallAlbedo = new Color(0.090f, 0.100f, 0.155f, 0.68f);
			Color baseWallEmission = new Color(0.016f, 0.026f, 0.054f, 1f);
			Color targetWallAlbedo = new Color(
				0.090f + accentColor.R * 0.18f,
				0.100f + accentColor.G * 0.16f,
				0.155f + accentColor.B * 0.14f,
				0.82f);
			wall.Material.AlbedoColor = LerpColor(baseWallAlbedo, targetWallAlbedo, Mathf.Clamp(totalResponse, 0f, 1f));
			wall.Material.Emission = LerpColor(baseWallEmission, accentColor, Mathf.Clamp(totalResponse, 0f, 1f));
			wall.Material.EmissionEnergyMultiplier = 0.16f + response * 2.8f + perimeterResponse * 2.3f;
		}

		int pillarCount = Mathf.Min(_pillars.Count, _arenaPillarVisuals.Count);
		for (int i = 0; i < pillarCount; i++)
		{
			ArenaPillarVisual pillar = _arenaPillarVisuals[i];
			Rect2 pillarBounds = _pillars[i];
			Vector2 center = pillarBounds.Position + pillarBounds.Size * 0.5f;
			float response = 0f;
			float climb = 0f;
			Color impactColor = new Color(1f, 0.72f, 0.24f, 1f);
			for (int waveIndex = 0; waveIndex < _cubeArenaImpactWaves.Count; waveIndex++)
			{
				CubeArenaImpactWave wave = _cubeArenaImpactWaves[waveIndex];
				float distance = Mathf.Max(Mathf.Abs(center.X - wave.Origin.X), Mathf.Abs(center.Y - wave.Origin.Y));
				float currentResponse = GetCubeArenaWaveResponse(distance, wave);
				if (currentResponse > response)
				{
					response = currentResponse;
					impactColor = wave.Tint;
					climb = Mathf.Clamp((wave.Age * wave.Speed + wave.Width - distance) / Mathf.Max(0.01f, wave.Width * 2f), 0f, 1f);
				}
			}

			float lift = Mathf.Pow(response, 0.7f) * (pillar.DynamicCover ? 4.1f : 2.9f);
			float tiltX = Mathf.Sin(i * 0.73f) * response * (pillar.DynamicCover ? 2.3f : 1.6f);
			float tiltZ = Mathf.Cos(i * 0.61f) * response * (pillar.DynamicCover ? 2.3f : 1.6f);
			Color baseAlbedo = pillar.DynamicCover
				? new Color(0.100f, 0.100f, 0.150f, 1f)
				: new Color(0.074f, 0.084f, 0.128f, 1f);
			Color baseEmission = new Color(0.018f, 0.028f, 0.060f, 1f);
			Color targetAlbedo = new Color(
				baseAlbedo.R + impactColor.R * (pillar.DynamicCover ? 0.18f : 0.12f),
				baseAlbedo.G + impactColor.G * (pillar.DynamicCover ? 0.14f : 0.10f),
				baseAlbedo.B + impactColor.B * 0.10f,
				1f);
			pillar.Mesh.Position = pillar.BasePosition + new Vector3(0f, lift, 0f);
			pillar.Mesh.RotationDegrees = new Vector3(tiltX, 0f, tiltZ);
			pillar.Material.AlbedoColor = LerpColor(baseAlbedo, targetAlbedo, Mathf.Clamp(response, 0f, 1f));
			pillar.Material.Emission = LerpColor(baseEmission, impactColor, Mathf.Clamp(response, 0f, 1f));
			pillar.Material.EmissionEnergyMultiplier = (pillar.DynamicCover ? 0.16f : 0.10f)
				+ response * (pillar.DynamicCover ? 3.6f : 2.4f);

			if (IsInstanceValid(pillar.SurgeLight))
			{
				Vector3 targetLightPosition = new(0f, Mathf.Lerp(-pillar.Height * 0.30f, pillar.Height * 0.34f, climb), 0f);
				float surgeBlend = Mathf.Clamp(dt * 10f, 0f, 1f);
				pillar.SurgeLight.Position = pillar.SurgeLight.Position.Lerp(targetLightPosition, surgeBlend);
				pillar.SurgeLight.LightColor = LerpColor(pillar.SurgeLight.LightColor, impactColor, surgeBlend);
				pillar.SurgeLight.LightEnergy = Mathf.Lerp(
					pillar.SurgeLight.LightEnergy,
					response > 0.001f ? (pillar.DynamicCover ? 2.5f : 1.7f) * response : 0f,
					surgeBlend);
				pillar.SurgeLight.Visible = pillar.SurgeLight.LightEnergy > 0.02f;
			}
		}
	}

	private void UpdateArenaLighting(float dt)
	{
		UpdateArenaAmbientState(dt);
		UpdateArenaPlayerLight(dt);
		UpdateArenaBossLight(dt);
		UpdateArenaImpactLights(dt);
		UpdateArenaPerimeterLights(dt);
		UpdateArenaTransientCues(dt);
	}

	private void UpdateArenaAmbientState(float dt)
	{
		if (_arenaEnvironment == null || !IsInstanceValid(_arenaSun) || _player == null)
		{
			return;
		}

		float hpPct = _player.Hp / Mathf.Max(1f, _player.MaxHp);
		float lowHp = hpPct < 0.40f
			? 1f - Mathf.Clamp((hpPct - 0.10f) / 0.30f, 0f, 1f)
			: 0f;
		float blend = Mathf.Clamp(dt * 4f, 0f, 1f);
		Color targetAmbientColor = LerpColor(
			new Color(0.28f, 0.33f, 0.42f, 1f),
			new Color(0.34f, 0.34f, 0.36f, 1f),
			lowHp * 0.75f);
		Color targetSunColor = LerpColor(
			new Color(0.62f, 0.72f, 0.90f, 1f),
			new Color(0.84f, 0.76f, 0.66f, 1f),
			lowHp * 0.55f);

		_arenaEnvironment.AmbientLightColor = LerpColor(_arenaEnvironment.AmbientLightColor, targetAmbientColor, blend);
		_arenaEnvironment.AmbientLightEnergy = Mathf.Lerp(_arenaEnvironment.AmbientLightEnergy, 0.18f + lowHp * 0.02f, blend);
		_arenaSun.LightColor = LerpColor(_arenaSun.LightColor, targetSunColor, blend);
		_arenaSun.LightEnergy = Mathf.Lerp(_arenaSun.LightEnergy, 0.90f + lowHp * 0.06f, blend);
	}

	private void UpdateArenaPlayerLight(float dt)
	{
		if (!IsInstanceValid(_arenaPlayerLight) || _player == null)
		{
			return;
		}

		Vector3 targetPosition = new(_player.Position.X, 8f, _player.Position.Y);
		float followBlend = Mathf.Clamp(dt * 8f, 0f, 1f);
		float hpPct = _player.Hp / Mathf.Max(1f, _player.MaxHp);
		float lowHp = 1f - Mathf.Clamp((hpPct - 0.15f) / 0.85f, 0f, 1f);

		_arenaPlayerLight.Position = _arenaPlayerLight.Position.Lerp(targetPosition, followBlend);
		_arenaPlayerLight.LightColor = new Color(
			Mathf.Lerp(0.76f, 0.92f, lowHp),
			Mathf.Lerp(0.86f, 0.78f, lowHp),
			Mathf.Lerp(1.00f, 0.66f, lowHp),
			1f);
		_arenaPlayerLight.LightEnergy = Mathf.Lerp(
			_arenaPlayerLight.LightEnergy,
			1.15f + lowHp * 0.80f,
			followBlend);
	}

	private void UpdateArenaBossLight(float dt)
	{
		if (!IsInstanceValid(_arenaBossLight))
		{
			return;
		}

		if (_boss == null || !_boss.IsAlive)
		{
			_arenaBossLight.LightEnergy = Mathf.Lerp(
				_arenaBossLight.LightEnergy,
				0f,
				Mathf.Clamp(dt * ArenaLightResponseSpeed, 0f, 1f));
			_arenaBossLight.Visible = _arenaBossLight.LightEnergy > 0.02f;
			return;
		}

		Vector3 targetPosition = new(_boss.Position.X, 10f, _boss.Position.Y);
		float targetEnergy = _boss.Kind == BossKind.Cube ? 1.10f : 1.35f;
		if (_boss.Kind == BossKind.Cube
			&& (_boss.Phase == BossPhase.TeleportAscend || _boss.Phase == BossPhase.TeleportDescend))
		{
			targetEnergy += 0.40f;
		}

		if (_bossLaserActive)
		{
			targetEnergy += 0.55f;
		}

		_arenaBossLight.Position = _arenaBossLight.Position.Lerp(targetPosition, Mathf.Clamp(dt * 6f, 0f, 1f));
		_arenaBossLight.OmniRange = _boss.Kind == BossKind.Cube ? 24f : 28f;
		_arenaBossLight.LightColor = LerpColor(
			_arenaBossLight.LightColor,
			GetArenaBossPhaseCueColor(_boss.Kind, _boss.Phase),
			Mathf.Clamp(dt * 6f, 0f, 1f));
		_arenaBossLight.LightEnergy = Mathf.Lerp(
			_arenaBossLight.LightEnergy,
			targetEnergy,
			Mathf.Clamp(dt * 6f, 0f, 1f));
		_arenaBossLight.Visible = true;
	}

	private void UpdateArenaImpactLights(float dt)
	{
		if (_arenaImpactLights.Count == 0)
		{
			return;
		}

		bool[] claimed = new bool[_cubeArenaImpactWaves.Count];
		for (int lightIndex = 0; lightIndex < _arenaImpactLights.Count; lightIndex++)
		{
			OmniLight3D light = _arenaImpactLights[lightIndex];
			int bestWaveIndex = -1;
			float bestScore = 0f;

			for (int waveIndex = 0; waveIndex < _cubeArenaImpactWaves.Count; waveIndex++)
			{
				if (claimed[waveIndex])
				{
					continue;
				}

				CubeArenaImpactWave candidateWave = _cubeArenaImpactWaves[waveIndex];
				float candidateFade = 1f - Mathf.Clamp(candidateWave.Age / Mathf.Max(0.01f, candidateWave.Duration), 0f, 1f);
				float score = candidateWave.Strength * candidateFade;
				if (score > bestScore)
				{
					bestScore = score;
					bestWaveIndex = waveIndex;
				}
			}

			if (bestWaveIndex == -1 || bestScore < 0.04f)
			{
				light.LightEnergy = Mathf.Lerp(
					light.LightEnergy,
					0f,
					Mathf.Clamp(dt * ArenaLightResponseSpeed, 0f, 1f));
				light.Visible = light.LightEnergy > 0.02f;
				continue;
			}

			claimed[bestWaveIndex] = true;
			CubeArenaImpactWave wave = _cubeArenaImpactWaves[bestWaveIndex];
			float fade = 1f - Mathf.Clamp(wave.Age / Mathf.Max(0.01f, wave.Duration), 0f, 1f);
			float wavefront = wave.Age * wave.Speed;
			Vector3 targetPosition = new(
				wave.Origin.X,
				3.4f + Mathf.Min(wavefront * 0.04f, 8f),
				wave.Origin.Y);

			light.Position = light.Position.Lerp(targetPosition, Mathf.Clamp(dt * 10f, 0f, 1f));
			light.OmniRange = 16f + wave.Strength * 7f + Mathf.Min(wavefront * 0.12f, 12f);
			light.LightColor = LerpColor(wave.Tint, new Color(1f, 1f, 1f, 1f), 0.16f);
			light.LightEnergy = Mathf.Lerp(
				light.LightEnergy,
				(1.1f + wave.Strength * 0.9f) * fade,
				Mathf.Clamp(dt * 10f, 0f, 1f));
			light.Visible = true;
		}
	}

	private void UpdateArenaPerimeterLights(float dt)
	{
		for (int i = 0; i < _arenaPerimeterLights.Count; i++)
		{
			OmniLight3D light = _arenaPerimeterLights[i];
			float response = GetArenaPerimeterResponse(i, out Color perimeterColor);
			float blend = Mathf.Clamp(dt * ArenaLightResponseSpeed, 0f, 1f);
			light.LightColor = LerpColor(light.LightColor, perimeterColor, blend);
			light.OmniRange = 54f + response * 12f;
			light.LightEnergy = Mathf.Lerp(
				light.LightEnergy,
				response > 0.001f ? 0.85f + response * 1.9f : 0f,
				blend);
			light.Visible = light.LightEnergy > 0.02f;
		}
	}

	private void UpdateArenaTransientCues(float dt)
	{
		for (int i = _arenaTransientCues.Count - 1; i >= 0; i--)
		{
			ArenaTransientCue cue = _arenaTransientCues[i];
			cue.Age += dt;
			float t = Mathf.Clamp(cue.Age / Mathf.Max(0.01f, cue.Duration), 0f, 1f);
			float fade = 1f - t;

			cue.Material.AlbedoColor = new Color(
				cue.Color.R,
				cue.Color.G,
				cue.Color.B,
				cue.VerticalBeam ? fade * 0.12f : fade * 0.18f);
			cue.Material.Emission = cue.Color;
			cue.Material.EmissionEnergyMultiplier = cue.VerticalBeam
				? 0.85f + fade * 1.35f
				: 1.10f + fade * 2.10f;

			if (cue.VerticalBeam)
			{
				float widthScale = 1f + Mathf.Sin(t * Mathf.Pi) * 0.12f;
				cue.Mesh.Scale = new Vector3(widthScale, 1f, widthScale);
			}
			else
			{
				float spread = 1f + t * 1.4f;
				cue.Mesh.Scale = new Vector3(spread, 1f, spread);
			}

			cue.Light.LightColor = cue.Color;
			cue.Light.OmniRange = cue.MaxLightRange * (cue.VerticalBeam ? (0.82f + t * 0.22f) : (0.72f + t * 0.55f));
			cue.Light.LightEnergy = cue.MaxLightEnergy * fade * fade;
			cue.Light.Visible = cue.Light.LightEnergy > 0.02f;

			if (cue.Age < cue.Duration)
			{
				continue;
			}

			if (IsInstanceValid(cue.Mesh))
			{
				cue.Mesh.QueueFree();
			}

			if (IsInstanceValid(cue.Light))
			{
				cue.Light.QueueFree();
			}

			_arenaTransientCues.RemoveAt(i);
		}
	}

	private void TrackArenaBossLightingState()
	{
		if (_boss == null || !_boss.IsAlive)
		{
			_arenaTrackedBoss = null;
			_arenaTrackedBossPhase = null;
			return;
		}

		if (!ReferenceEquals(_arenaTrackedBoss, _boss))
		{
			_arenaTrackedBoss = _boss;
			_arenaTrackedBossPhase = _boss.Phase;
			return;
		}

		if (_arenaTrackedBossPhase == _boss.Phase)
		{
			return;
		}

		BossPhase newPhase = _boss.Phase;
		_arenaTrackedBossPhase = newPhase;
		if (!IsArenaHighlightedBossPhase(newPhase))
		{
			return;
		}

		TriggerArenaPerimeterSweep(
			GetArenaBossPhaseCueColor(_boss.Kind, newPhase),
			_boss.Kind == BossKind.Cube ? 0.95f : 0.85f,
			ArenaPerimeterSweepDuration);
	}

	private void TriggerArenaEnemySpawnCue(Vector2 origin, EnemyKind kind)
	{
		bool elite = IsArenaEliteKind(kind);
		Color cueColor = elite
			? new Color(1f, 0.30f, 0.12f, 1f)
			: new Color(1f, 0.44f, 0.18f, 1f);
		SpawnArenaGroundCue(origin, cueColor, elite ? 0.62f : 0.38f, elite ? 4.8f : 3.4f, elite ? 1.35f : 0.80f, elite ? 20f : 14f);
		if (elite)
		{
			TriggerArenaPerimeterSweep(cueColor, 0.48f, 0.90f);
		}
	}

	private void TriggerArenaInteractableSpawnCue(Vector2 origin)
	{
		Color cueColor = new Color(0.24f, 0.95f, 0.70f, 1f);
		SpawnArenaGroundCue(origin, cueColor, 1.45f, 5.2f, 1.0f, 18f);
		SpawnArenaBeaconCue(origin, cueColor, 1.85f, 2.8f, 16f, 1.45f, 22f);
	}

	private void TriggerArenaTeleportDestinationCue(Vector2 origin)
	{
		Color cueColor = new Color(0.84f, 0.97f, 1f, 1f);
		SpawnArenaGroundCue(origin, cueColor, 0.48f, 4.5f, 1.75f, 20f);
		SpawnArenaBeaconCue(origin, cueColor, 0.68f, 2.2f, 14f, 1.35f, 18f);
	}

	private void SpawnArenaGroundCue(Vector2 origin, Color color, float duration, float radius, float lightEnergy, float lightRange)
	{
		AddArenaTransientCue(
			new Vector3(origin.X, 0.10f, origin.Y),
			new Vector3(radius * 2f, 0.14f, radius * 2f),
			color,
			duration,
			lightEnergy,
			lightRange,
			false);
	}

	private void SpawnArenaBeaconCue(Vector2 origin, Color color, float duration, float radius, float height, float lightEnergy, float lightRange)
	{
		AddArenaTransientCue(
			new Vector3(origin.X, height * 0.5f, origin.Y),
			new Vector3(radius * 0.72f, height, radius * 0.72f),
			color,
			duration,
			lightEnergy,
			lightRange,
			true);
	}

	private void AddArenaTransientCue(
		Vector3 position,
		Vector3 size,
		Color color,
		float duration,
		float lightEnergy,
		float lightRange,
		bool verticalBeam)
	{
		StandardMaterial3D material = new()
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(color.R, color.G, color.B, verticalBeam ? 0.12f : 0.18f),
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = verticalBeam ? 1.2f : 1.4f,
		};

		MeshInstance3D mesh = new()
		{
			Mesh = new BoxMesh { Size = size },
			Position = position,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(mesh);

		OmniLight3D light = CreateArenaOmniLight(
			$"ArenaCueLight{_arenaTransientCues.Count}",
			color,
			lightRange,
			0f);
		light.Position = verticalBeam
			? new Vector3(position.X, Mathf.Min(position.Y + size.Y * 0.12f, 16f), position.Z)
			: new Vector3(position.X, 2.4f, position.Z);
		light.Visible = false;

		_arenaTransientCues.Add(new ArenaTransientCue
		{
			Mesh = mesh,
			Material = material,
			Light = light,
			BasePosition = position,
			Color = color,
			Duration = duration,
			MaxLightEnergy = lightEnergy,
			MaxLightRange = lightRange,
			VerticalBeam = verticalBeam,
		});
	}

	private void TriggerArenaPerimeterSweep(Color color, float strength, float duration)
	{
		_arenaPerimeterSweeps.Add(new ArenaPerimeterSweep
		{
			Color = color,
			Age = 0f,
			Duration = duration,
			Strength = strength,
		});
	}

	private float GetArenaPerimeterResponse(int wallIndex, out Color perimeterColor)
	{
		float bestResponse = 0f;
		perimeterColor = new Color(0.62f, 0.78f, 1f, 1f);
		float wallPhase = GetArenaWallSweepPhase(wallIndex);

		for (int i = 0; i < _arenaPerimeterSweeps.Count; i++)
		{
			ArenaPerimeterSweep sweep = _arenaPerimeterSweeps[i];
			float normalizedAge = sweep.Age / Mathf.Max(0.01f, sweep.Duration);
			float phaseDistance = Mathf.Abs(normalizedAge - wallPhase);
			phaseDistance = Mathf.Min(phaseDistance, 1f - phaseDistance);
			float band = 1f - Mathf.Clamp(phaseDistance / 0.18f, 0f, 1f);
			float response = band * band * sweep.Strength;
			if (response <= bestResponse)
			{
				continue;
			}

			bestResponse = response;
			perimeterColor = sweep.Color;
		}

		return bestResponse;
	}

	private static float GetArenaWallSweepPhase(int wallIndex)
	{
		return wallIndex switch
		{
			0 => 0f,
			1 => 0.50f,
			2 => 0.75f,
			3 => 0.25f,
			_ => 0f,
		};
	}

	private static Color GetArenaBossPhaseCueColor(BossKind kind, BossPhase phase)
	{
		if (kind == BossKind.Cube)
		{
			return phase == BossPhase.TeleportAscend || phase == BossPhase.TeleportDescend
				? new Color(0.84f, 0.97f, 1f, 1f)
				: new Color(1f, 0.68f, 0.24f, 1f);
		}

		if (kind == BossKind.Overseer)
		{
			return phase == BossPhase.LaserTrack
				? new Color(1f, 0.34f, 0.08f, 1f)
				: GetBossWaypointColor(kind);
		}

		return phase == BossPhase.Annihilate
			? new Color(1f, 0.36f, 0.86f, 1f)
			: GetBossWaypointColor(kind);
	}

	private static bool IsArenaHighlightedBossPhase(BossPhase phase)
	{
		return phase == BossPhase.Charging
			|| phase == BossPhase.Annihilate
			|| phase == BossPhase.LaserTelegraph
			|| phase == BossPhase.LaserTrack
			|| phase == BossPhase.RollTelegraph
			|| phase == BossPhase.TeleportAscend
			|| phase == BossPhase.TeleportDescend
			|| phase == BossPhase.Landing;
	}

	private static bool IsArenaEliteKind(EnemyKind kind)
	{
		return kind != EnemyKind.Grunt;
	}

	private static Color LerpColor(Color from, Color to, float t)
	{
		t = Mathf.Clamp(t, 0f, 1f);
		return new Color(
			Mathf.Lerp(from.R, to.R, t),
			Mathf.Lerp(from.G, to.G, t),
			Mathf.Lerp(from.B, to.B, t),
			Mathf.Lerp(from.A, to.A, t));
	}

	private static float GetCubeArenaWaveResponse(float distance, CubeArenaImpactWave wave)
	{
		float wavefront = wave.Age * wave.Speed;
		float bandDistance = Mathf.Abs(distance - wavefront);
		float band = 1f - Mathf.Clamp(bandDistance / Mathf.Max(0.01f, wave.Width), 0f, 1f);
		float fade = 1f - Mathf.Clamp(wave.Age / Mathf.Max(0.01f, wave.Duration), 0f, 1f);
		return band * band * fade * wave.Strength;
	}

	private void BuildPillars()
	{
		_pillars.Clear();
		_pillarHeights.Clear();
		_cubeDynamicCoverCount = 0;

		for (int i = 0; i < ArenaLayout.PillarPositions.Length; i++)
		{
			float pillarSize = _rng.RandfRange(ArenaLayout.PillarMinSize, ArenaLayout.PillarMaxSize);
			float pillarHeight = _rng.RandfRange(ArenaLayout.PillarMinHeight, ArenaLayout.PillarMaxHeight);
			Vector2 size = new Vector2(pillarSize, pillarSize);
			Vector2 halfSize = size * 0.5f;
			Vector2 center = ArenaLayout.PillarPositions[i];
			_pillars.Add(new Rect2(center - halfSize, size));
			_pillarHeights.Add(pillarHeight);
		}
	}

	private void TrySpawnCubeCover(Vector2 impactOrigin, float spawnChance)
	{
		TrySpawnCubeCover(impactOrigin, spawnChance, 1, CubeArenaCoverMinDistance, CubeArenaCoverMaxDistance);
	}

	private void TrySpawnCubeCover(Vector2 impactOrigin, float spawnChance, int spawnCount, float minDistance, float maxDistance)
	{
		if (_cubeDynamicCoverCount >= CubeArenaCoverMaxCount || _rng.Randf() > spawnChance)
		{
			return;
		}

		for (int spawnIndex = 0; spawnIndex < spawnCount && _cubeDynamicCoverCount < CubeArenaCoverMaxCount; spawnIndex++)
		{
			for (int attempt = 0; attempt < 16; attempt++)
			{
				float width = _rng.RandfRange(ArenaLayout.PillarMinSize * 0.9f, ArenaLayout.PillarMaxSize * 1.25f);
				float depth = _rng.RandfRange(ArenaLayout.PillarMinSize * 0.65f, ArenaLayout.PillarMaxSize * 0.95f);
				if (_rng.Randf() < 0.5f)
				{
					(width, depth) = (depth, width);
				}

				Vector2 size = new Vector2(width, depth);
				Vector2 half = size * 0.5f;
				float height = _rng.RandfRange(ArenaLayout.PillarMinHeight * 0.72f, ArenaLayout.PillarMaxHeight * 1.06f);
				float angle = _rng.RandfRange(0f, Mathf.Tau);
				float distance = _rng.RandfRange(minDistance, maxDistance);
				Vector2 center = ClampResolve(impactOrigin + AngleToDir(angle) * distance, Mathf.Max(size.X, size.Y) * 0.6f);
				Rect2 candidate = new Rect2(center - half, size);

				if ((center - _player.Position).LengthSquared() < Mathf.Pow(_player.Radius + 16f, 2f))
				{
					continue;
				}

				if (_boss != null && (center - _boss.Position).LengthSquared() < Mathf.Pow(_boss.Radius + 14f, 2f))
				{
					continue;
				}

				if (_challengePillar != null
					&& IsInstanceValid(_challengePillar)
					&& (center - _challengePillar.Position).LengthSquared() < Mathf.Pow(ArenaChallengePillarActor.ActivationRadius + 10f, 2f))
				{
					continue;
				}

				bool overlaps = false;
				for (int i = 0; i < _pillars.Count; i++)
				{
					if (ExpandRect(_pillars[i], CubeArenaCoverSeparation).Intersects(candidate))
					{
						overlaps = true;
						break;
					}
				}

				if (overlaps)
				{
					continue;
				}

				_pillars.Add(candidate);
				_pillarHeights.Add(height);
				CreateArenaPillarVisual(candidate, height, true);
				_cubeDynamicCoverCount++;
				break;
			}
		}
	}

	private void SpawnCubeTeleportRuptureCover(Vector2 impactOrigin)
	{
		int spawnCount = _rng.RandiRange(CubeTeleportRuptureCoverMinCount, CubeTeleportRuptureCoverMaxCount);
		TrySpawnCubeCover(
			impactOrigin,
			1f,
			spawnCount,
			CubeTeleportRuptureCoverMinDistance,
			CubeTeleportRuptureCoverMaxDistance);
	}
}