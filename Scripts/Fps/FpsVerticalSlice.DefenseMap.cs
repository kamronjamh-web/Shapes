using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	[ExportGroup("Horde Defense")]
	[Export] public FpsDefenseSettings? DefenseSettings { get; set; }
	[Export] public FpsHillMapSettings? HillMapSettings { get; set; }
	[Export] public bool LiveApplyDefenseChanges { get; set; } = true;
	private FpsDefenseSettings? _observedDefenseSettings;
	private FpsHillMapSettings? _observedHillMapSettings;
	private readonly System.Collections.Generic.List<FpsDefenseLaneSettings> _observedDefenseLanes = new();
	private bool _defenseMapDirty;
	private FpsDefenseCore? _defenseCore;
	private Node3D? _shopMarker;
	private Label? _defenseHud;
	public bool IsDefenseMap => DefenseSettings != null;
	public bool IsHillMap => IsDefenseMap && HillMapSettings != null;

	private void BuildDefenseMap()
	{
		if (IsHillMap)
		{
			BuildHillMap();
			BuildDefenseObjectives();
			return;
		}
		FpsDefenseSettings settings = DefenseSettings!;
		float half = Mathf.Max(14f, settings.CourtyardHalfSize);
		Color floor = new(0.09f, 0.13f, 0.18f);
		Color wall = new(0.12f, 0.18f, 0.25f);
		CreateArenaBlock(_arenaRoot, "CourtyardFloor", new Vector3(half * 2f, 1f, half * 2f), new Vector3(0f, -0.5f, 0f), floor);

		for (int side = 0; side < 4; side++)
		{
			FpsLaneDirection direction = (FpsLaneDirection)side;
			FpsDefenseLaneSettings? lane = null;
			foreach (FpsDefenseLaneSettings candidate in settings.Lanes)
				if (candidate != null && candidate.Direction == direction) { lane = candidate; break; }
			Vector3 outward = direction switch
			{
				FpsLaneDirection.East => Vector3.Right, FpsLaneDirection.South => Vector3.Back,
				FpsLaneDirection.West => Vector3.Left, _ => Vector3.Forward,
			};
			Vector3 across = new(-outward.Z, 0f, outward.X);
			float yaw = direction is FpsLaneDirection.East or FpsLaneDirection.West ? 90f : 0f;
			string name = direction.ToString();
			if (lane == null || !lane.Enabled)
			{
				AddDefenseBlock($"{name}SealedWall", new Vector3(half * 2f, 6f, 1.5f), outward * half + Vector3.Up * 3f, wall, yaw);
				AddDefenseBlock($"{name}Seal", new Vector3(8f, 3.8f, 0.12f), outward * (half - 0.8f) + Vector3.Up * 2f, new Color(0.19f, 0.25f, 0.31f), yaw);
				AddMapLabel($"{name.ToUpperInvariant()} / SEALED", outward * (half - 1.1f) + Vector3.Up * 4.8f, new Color(0.45f, 0.58f, 0.65f), 42);
				continue;
			}

			float width = Mathf.Clamp(lane.Width, 8f, half * 2f - 6f);
			float length = Mathf.Max(16f, lane.Length);
			float sideWidth = half - width * 0.5f;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				AddDefenseBlock($"{name}GateWall{sign}", new Vector3(sideWidth, 6f, 1.5f), outward * half + across * ((half + width * 0.5f) * 0.5f * sign) + Vector3.Up * 3f, wall, yaw);
				AddDefenseBlock($"{name}LaneWall{sign}", new Vector3(1.5f, 4f, length), outward * (half + length * 0.5f) + across * ((width * 0.5f + 0.75f) * sign) + Vector3.Up * 2f, wall, yaw);
				AddDefenseBlock($"{name}GatePillar{sign}", new Vector3(1.5f, 7f, 2.8f), outward * half + across * ((width * 0.5f + 0.9f) * sign) + Vector3.Up * 3.5f, new Color(0.22f, 0.29f, 0.36f), yaw);
				AddDefenseVisual(new BoxMesh { Size = new Vector3(0.15f, 3.8f, 0.25f) }, outward * (half - 1.5f) + across * ((width * 0.5f + 0.9f) * sign) + Vector3.Up * 3.4f, lane.AccentColor, yaw);
			}
			AddDefenseBlock($"{name}LaneFloor", new Vector3(width, 1f, length), outward * (half + length * 0.5f) + Vector3.Down * 0.5f, floor.Lerp(wall, 0.3f), yaw);
			AddDefenseBlock($"{name}LaneEnd", new Vector3(width + 3f, 4f, 1.5f), outward * (half + length + 0.75f) + Vector3.Up * 2f, wall, yaw);
			AddDefenseBlock($"{name}GateLintel", new Vector3(width + 4f, 1.2f, 2.8f), outward * half + Vector3.Up * 6.5f, wall, yaw);
			AddMapLabel($"{name.ToUpperInvariant()} GATE", outward * (half - 1.6f) + Vector3.Up * 6.5f, lane.AccentColor, 58);
			for (float distance = half + 4f; distance < half + length - 4f; distance += 7f)
			{
				AddDefenseVisual(new BoxMesh { Size = new Vector3(width * 0.65f, 0.035f, 0.20f) }, outward * distance + Vector3.Up * 0.025f, lane.AccentColor.Darkened(0.45f), yaw);
			}
			AddDefenseVisual(new BoxMesh { Size = new Vector3(width - 2f, 0.06f, 5f) }, outward * (half + length - 6f) + Vector3.Up * 0.04f, new Color(0.58f, 0.16f, 0.13f), yaw);
			AddMapLabel("SHAPE INCURSION", outward * (half + length - 6f) + Vector3.Up * 4f, new Color(1f, 0.43f, 0.22f), 44);
		}

		// Keep the center route clear: these low cover pieces sit in the side bays.
		for (int sign = -1; sign <= 1; sign += 2)
		{
			CreateArenaBlock(_arenaRoot, $"Cover{sign}", new Vector3(4f, 0.9f, 2f), new Vector3(sign * 15f, 0.45f, -11f), wall.Lightened(0.18f));
			AddDefenseVisual(new BoxMesh { Size = new Vector3(6f, 0.05f, 6f) }, new Vector3(sign * 15f, 0.04f, -16f), new Color(0.31f, 0.27f, 0.19f));
			AddMapLabel("DEFENSE BAY", new Vector3(sign * 15f, 1.5f, -17f), new Color(0.94f, 0.73f, 0.40f), 30);
		}
		BuildDefenseObjectives();
	}

	private void BuildDefenseObjectives()
	{
		FpsDefenseSettings settings = DefenseSettings!;
		Vector3 corePosition = ResolveDefenseGroundPosition(settings.CorePosition);
		Vector3 shopPosition = ResolveDefenseGroundPosition(settings.ShopPosition);
		AddDefenseVisual(new CylinderMesh { TopRadius = 3.2f, BottomRadius = 3.2f, Height = 0.20f, RadialSegments = 8 }, corePosition + Vector3.Up * 0.11f, new Color(0.12f, 0.33f, 0.42f));
		AddMapLabel("DEFEND THE CORE", corePosition + Vector3.Up * 4.3f, new Color(0.20f, 0.82f, 0.95f), 42);
		if (_defenseCore == null || !IsInstanceValid(_defenseCore))
		{
			_defenseCore = new FpsDefenseCore { Name = "DefenseCore", MaxHealth = settings.CoreMaxHealth };
			_defenseCore.Destroyed += OnDefenseCoreDestroyed;
			AddChild(_defenseCore);
		}
		else _defenseCore.Retune(settings.CoreMaxHealth);
		_defenseCore.Position = corePosition + Vector3.Up * 1.6f;
		if (_shopOpen) RebuildDefenseShopMenu();

		_shopMarker = new Node3D { Name = "FieldShop", Position = shopPosition, Visible = false };
		_arenaRoot.AddChild(_shopMarker);
		AddDefenseVisual(new CylinderMesh { TopRadius = 3f, BottomRadius = 3f, Height = 0.16f, RadialSegments = 6 }, Vector3.Up * 0.1f, new Color(0.16f, 0.56f, 0.46f), parent: _shopMarker);
		AddDefenseVisual(new BoxMesh { Size = new Vector3(2.8f, 1.2f, 1.5f) }, Vector3.Up * 0.65f, new Color(0.20f, 0.35f, 0.34f), parent: _shopMarker);
		AddDefenseVisual(new SphereMesh { Radius = 0.45f, Height = 0.90f, RadialSegments = 12, Rings = 6 }, Vector3.Up * 2.2f, new Color(0.28f, 1f, 0.68f), parent: _shopMarker);
		AddMapLabel("FIELD SHOP / F", shopPosition + Vector3.Up * 3.5f, new Color(0.40f, 1f, 0.76f), 42, _shopMarker);
	}

	private Vector3 ResolveDefenseGroundPosition(Vector3 position) => IsHillMap
		? new Vector3(position.X, SampleHillSurfaceHeight(position), position.Z)
		: position;

	private float HillPlayableHalf => Mathf.Max(50f, (HillMapSettings?.PlayableSize ?? 200f) * 0.5f);
	private float HillSummitHalf => Mathf.Clamp(HillMapSettings?.SummitHalfSize ?? 16f, 8f, HillPlayableHalf - 12f);
	private float HillFootHalf => Mathf.Clamp(HillMapSettings?.HillFootHalfSize ?? 65f, HillSummitHalf + 8f, HillPlayableHalf - 4f);
	private float HillHeight => Mathf.Max(1f, HillMapSettings?.SummitHeight ?? 8f);
	private float HillRoomHalf => Mathf.Clamp(HillMapSettings?.RoomHalfSize ?? 12f, 6f, HillSummitHalf - 2f);
	private float HillDoorWidth => Mathf.Clamp(HillMapSettings?.DoorWidth ?? 8f, 4f, HillRoomHalf * 2f - 3f);

	/// <summary>Terrain height at world X/Z; matches the flat top and planar hill faces.</summary>
	public float SampleHillSurfaceHeight(Vector3 position)
	{
		if (!IsHillMap) return 0f;
		float distance = Mathf.Max(Mathf.Abs(position.X), Mathf.Abs(position.Z));
		return HillHeight * Mathf.Clamp((HillFootHalf - distance) / (HillFootHalf - HillSummitHalf), 0f, 1f);
	}

	/// <summary>Playable X/Z rectangle in world metres.</summary>
	public Rect2 GetHillMovementBounds() => new(new Vector2(-HillPlayableHalf, -HillPlayableHalf), new Vector2(HillPlayableHalf * 2f, HillPlayableHalf * 2f));

	private bool HillDoorEnabled(int side) => side switch
	{
		1 => HillMapSettings!.EastDoorEnabled,
		2 => HillMapSettings!.SouthDoorEnabled,
		3 => HillMapSettings!.WestDoorEnabled,
		_ => true,
	};

	private static Vector3 HillOutward(int side) => side switch { 1 => Vector3.Right, 2 => Vector3.Back, 3 => Vector3.Left, _ => Vector3.Forward };

	private void BuildHillMap()
	{
		FpsHillMapSettings settings = HillMapSettings!;
		float half = HillPlayableHalf;
		CreateArenaBlock(_arenaRoot, "HillGround", new Vector3(half * 2f, 1f, half * 2f), Vector3.Down * 0.5f, settings.GroundColor);
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side);
			float yaw = side % 2 == 0 ? 0f : 90f;
			AddDefenseBlock($"HillBoundary{side}", new Vector3(half * 2f + 2f, 3f, 1f), outward * (half + 0.5f) + Vector3.Up * 1.5f, settings.GroundColor.Darkened(0.25f), yaw);
		}

		using SurfaceTool terrain = new();
		terrain.Begin(Mesh.PrimitiveType.Triangles);
		float summit = HillSummitHalf;
		float foot = HillFootHalf;
		float height = HillHeight;
		AddHillQuad(terrain, new Vector3(-summit, height, -summit), new Vector3(summit, height, -summit), new Vector3(summit, height, summit), new Vector3(-summit, height, summit));
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side);
			Vector3 across = new(-outward.Z, 0f, outward.X);
			AddHillQuad(terrain, outward * summit - across * summit + Vector3.Up * height,
				outward * foot - across * foot, outward * foot + across * foot,
				outward * summit + across * summit + Vector3.Up * height);
		}
		terrain.GenerateNormals();
		ArrayMesh mesh = terrain.Commit();
		StaticBody3D body = new() { Name = "HillTerrain", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
		body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = settings.HillColor, Roughness = 1f } });
		_arenaRoot.AddChild(body);

		float room = HillRoomHalf;
		float wallHeight = Mathf.Max(4f, settings.RoomWallHeight);
		float doorHeight = Mathf.Clamp(settings.DoorHeight, 3f, wallHeight - 0.5f);
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side);
			Vector3 across = new(-outward.Z, 0f, outward.X);
			float yaw = side % 2 == 0 ? 0f : 90f;
			if (!HillDoorEnabled(side))
				AddDefenseBlock($"SummitRoomWall{side}", new Vector3(room * 2f + 1f, wallHeight, 1f), outward * room + Vector3.Up * (height + wallHeight * 0.5f), settings.RoomColor, yaw);
			else
			{
				float panelWidth = room - HillDoorWidth * 0.5f;
				for (int sign = -1; sign <= 1; sign += 2)
					AddDefenseBlock($"SummitRoomWall{side}_{sign}", new Vector3(panelWidth, wallHeight, 1f), outward * room + across * ((room + HillDoorWidth * 0.5f) * 0.5f * sign) + Vector3.Up * (height + wallHeight * 0.5f), settings.RoomColor, yaw);
				AddDefenseBlock($"SummitDoorLintel{side}", new Vector3(HillDoorWidth, wallHeight - doorHeight, 1f), outward * room + Vector3.Up * (height + doorHeight + (wallHeight - doorHeight) * 0.5f), settings.RoomColor, yaw);
			}
			// Broad cardinal strips communicate the gentle ascent without adding obstacles.
			float width = Mathf.Clamp(settings.RampWidth, 8f, summit * 2f);
			using SurfaceTool ramp = new();
			ramp.Begin(Mesh.PrimitiveType.Triangles);
			AddHillQuad(ramp, outward * summit - across * width * 0.5f + Vector3.Up * (height + 0.025f), outward * foot - across * width * 0.5f + Vector3.Up * 0.025f,
				outward * foot + across * width * 0.5f + Vector3.Up * 0.025f, outward * summit + across * width * 0.5f + Vector3.Up * (height + 0.025f));
			ramp.GenerateNormals();
			_arenaRoot.AddChild(new MeshInstance3D { Name = $"HillRamp{side}", Mesh = ramp.Commit(), MaterialOverride = new StandardMaterial3D { AlbedoColor = settings.HillColor.Lightened(0.08f), Roughness = 1f } });
		}
		if (settings.RoofEnabled)
			CreateArenaBlock(_arenaRoot, "SummitRoomRoof", new Vector3(room * 2f + 1f, 0.5f, room * 2f + 1f), new Vector3(0f, height + wallHeight + 0.25f, 0f), settings.RoomColor);
		AddMapLabel("NORTH GATE", new Vector3(0f, height + wallHeight + 0.8f, -room), new Color(0.45f, 0.9f, 1f), 38);
	}

	private static void AddHillQuad(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		// Godot renders clockwise fronts. Choose the upward-facing side for every quad.
		if ((b - a).Cross(c - a).Y > 0f) (b, d) = (d, b);
		foreach (Vector3 vertex in new[] { a, b, c, a, c, d }) surface.AddVertex(vertex);
	}

	/// <summary>Slide a crowd foot position along the boundary and solid room walls.</summary>
	public Vector3 ConstrainHillCrowdMovement(Vector3 from, Vector3 desired, float radius)
	{
		if (!IsHillMap) return desired;
		radius = Mathf.Max(0f, radius);
		float limit = HillPlayableHalf - radius;
		desired.X = Mathf.Clamp(desired.X, -limit, limit);
		desired.Z = Mathf.Clamp(desired.Z, -limit, limit);
		float room = HillRoomHalf;
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = HillOutward(side);
			Vector3 across = new(-outward.Z, 0f, outward.X);
			bool door = HillDoorEnabled(side);
			float panelWidth = door ? room - HillDoorWidth * 0.5f : room * 2f + 1f;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				Vector3 center = outward * room + (door ? across * ((room + HillDoorWidth * 0.5f) * 0.5f * sign) : Vector3.Zero);
				Vector3 size = side % 2 == 0 ? new Vector3(panelWidth, 0f, 1f) : new Vector3(1f, 0f, panelWidth);
				SlideHillWall(from, ref desired, center, size, radius);
				if (!door) break;
			}
		}
		return desired;
	}

	private static void SlideHillWall(Vector3 from, ref Vector3 desired, Vector3 center, Vector3 size, float radius)
	{
		float minX = center.X - size.X * 0.5f - radius, maxX = center.X + size.X * 0.5f + radius;
		float minZ = center.Z - size.Z * 0.5f - radius, maxZ = center.Z + size.Z * 0.5f + radius;
		if (from.Z > minZ && from.Z < maxZ)
		{
			if (from.X <= minX && desired.X > minX) desired.X = minX;
			else if (from.X >= maxX && desired.X < maxX) desired.X = maxX;
		}
		if (desired.X > minX && desired.X < maxX)
		{
			if (from.Z <= minZ && desired.Z > minZ) desired.Z = minZ;
			else if (from.Z >= maxZ && desired.Z < maxZ) desired.Z = maxZ;
		}
	}

	private void AddDefenseBlock(string name, Vector3 size, Vector3 position, Color color, float yaw = 0f)
	{
		Node3D frame = new() { Name = name, Position = position, RotationDegrees = new Vector3(0f, yaw, 0f) };
		_arenaRoot.AddChild(frame);
		CreateArenaBlock(frame, "Body", size, Vector3.Zero, color);
	}

	private MeshInstance3D AddDefenseVisual(Mesh mesh, Vector3 position, Color color, float yaw = 0f, Node3D? parent = null)
	{
		MeshInstance3D visual = new()
		{
			Mesh = mesh, Position = position, RotationDegrees = new Vector3(0f, yaw, 0f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = color, Roughness = 0.5f,
				EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.35f,
			},
		};
		(parent ?? _arenaRoot).AddChild(visual);
		return visual;
	}

	private void AddMapLabel(string text, Vector3 position, Color color, int fontSize, Node3D? parent = null)
	{
		Label3D label = new()
		{
			Text = text, Position = parent == null ? position : position - parent.Position,
			FontSize = fontSize, PixelSize = 0.015f, Modulate = color,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8,
		};
		(parent ?? _arenaRoot).AddChild(label);
	}

	public bool TryGetDefenseTarget(Vector3 enemyPosition, out Vector3 target, out bool targetIsCore)
	{
		target = Vector3.Zero;
		targetIsCore = false;
		if (!IsDefenseMap || _defenseCore == null || !_defenseCore.IsAlive) return false;
		float radius = Mathf.Max(0f, DefenseSettings!.PlayerAggroRadius);
		float playerDistance = enemyPosition.DistanceSquaredTo(_player.GlobalPosition);
		targetIsCore = playerDistance > radius * radius || playerDistance > enemyPosition.DistanceSquaredTo(_defenseCore.GlobalPosition);
		target = targetIsCore ? _defenseCore.GlobalPosition : _player.HeadPosition;
		return true;
	}

	public bool IsDefenseCoreCollider(GodotObject? collider) => collider == _defenseCore && _defenseCore != null;
	public void ApplyDefenseCoreDamage(float damage)
	{
		if (IsCombatRunning()) _defenseCore?.ApplyDamage(damage);
	}

	private void OnDefenseCoreDestroyed()
	{
		_defeat = true;
		CloseDefenseShop();
		RefreshCombatState();
		ShowNotice("The core was destroyed.", 6f);
	}

	private void SyncDefenseBindings()
	{
		bool changed = _observedDefenseSettings != DefenseSettings || _observedHillMapSettings != HillMapSettings;
		if (!changed && DefenseSettings != null)
		{
			changed = _observedDefenseLanes.Count != DefenseSettings.Lanes.Count;
			for (int index = 0; !changed && index < _observedDefenseLanes.Count; index++)
				changed = _observedDefenseLanes[index] != DefenseSettings.Lanes[index];
		}
		if (!changed) return;
		UnbindDefenseSettings();
		_observedDefenseSettings = DefenseSettings;
		_observedHillMapSettings = HillMapSettings;
		if (_observedHillMapSettings != null) _observedHillMapSettings.Changed += OnDefenseSettingsChanged;
		if (DefenseSettings != null)
		{
			DefenseSettings.Changed += OnDefenseSettingsChanged;
			foreach (FpsDefenseLaneSettings lane in DefenseSettings.Lanes)
			{
				_observedDefenseLanes.Add(lane);
				if (lane != null) lane.Changed += OnDefenseSettingsChanged;
			}
		}
		_defenseMapDirty = true;
	}

	private void UnbindDefenseSettings()
	{
		if (_observedDefenseSettings != null) _observedDefenseSettings.Changed -= OnDefenseSettingsChanged;
		if (_observedHillMapSettings != null) _observedHillMapSettings.Changed -= OnDefenseSettingsChanged;
		_observedHillMapSettings = null;
		foreach (FpsDefenseLaneSettings lane in _observedDefenseLanes)
			if (lane != null) lane.Changed -= OnDefenseSettingsChanged;
		_observedDefenseLanes.Clear();
		_observedDefenseSettings = null;
	}
	private void OnDefenseSettingsChanged() => _defenseMapDirty = true;
}
