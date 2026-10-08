using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Collision, gravity, hit queries and world constraints use the actual North Gate scene.
public partial class ValidateHordePiling : Node
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private FpsVerticalSlice _map = null!;
	private FpsHordeManager _crowd = null!;
	private int _checks;
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-piling-validation/"), "Piling validation uses isolated save data");
			await Load();
			ValidateWorldClearance();
			ValidateVerticalMelee();
			ValidatePhysicalSupports();
			await ValidateStationaryContacts();
			await ValidateRoofSupport();
			await Load();
			await ValidateNaturalPile();
			GD.Print($"Horde piling validation passed: {_checks} checks.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private async Task Load()
	{
		if (_map != null) { RemoveChild(_map); _map.QueueFree(); await Frames(2); }
		_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		AddChild(_map);
		await Frames(4);
		_map.DebugPlayerInvulnerable = true;
		_map.DebugCoreInvulnerable = true;
		_crowd = _map.HordeManager!;
		_map.SetPhysicsProcess(false);
		Check(_crowd.Settings.SoftCollisionEnabled && _crowd.Settings.StackingEnabled, "North Gate enables soft contacts and piling by default");
	}

	private async Task ValidateStationaryContacts()
	{
		_crowd.Settings.StackingEnabled = false;
		_crowd.Settings.SeparationStrength = 0f;
		_map.BruiserArchetypeSettings!.MoveSpeed = 0f;
		_map.ApplyHordeArchetypeChangesLive();
		_map.DebugSpawnHorde(2, true);
		List<FpsHordeEnemySnapshot> pair = Alive();
		Vector3 origin = new(60f, 0f, -76f);
		_crowd.TrySetPosition(pair[0].Handle, origin);
		_crowd.TrySetPosition(pair[1].Handle, origin);
		await Steps(30);
		pair = Alive();
		float distance = pair[0].Position.DistanceTo(pair[1].Position);
		Check(distance >= (pair[0].Radius + pair[1].Radius) * 0.9f, "Stationary coincident bodies separate without movement or steering separation");
		Check(pair.All(e => Mathf.Abs(e.Position.Y - _map.SampleHillSurfaceHeight(e.Position)) < 0.05f), "Unstacked stationary contacts stay on terrain");
		_crowd.Settings.SoftCollisionEnabled = false;
		_crowd.TrySetPosition(pair[0].Handle, origin);
		_crowd.TrySetPosition(pair[1].Handle, origin);
		await Steps(10);
		pair = Alive();
		Check(pair[0].Position.DistanceTo(pair[1].Position) < 0.05f, "Disabling soft contacts disables physical overlap resolution independently of steering");
		_crowd.Settings.SoftCollisionEnabled = true;
		await Steps(30);
		pair = Alive();
		Check(pair[0].Position.DistanceTo(pair[1].Position) >= (pair[0].Radius + pair[1].Radius) * 0.9f, "Enabling soft contacts resolves existing overlaps live");
		Check(_map.ActiveEnemyCount == 2 && _crowd.ActiveCount == 2, "Contact tuning preserves crowd identities and population");
	}
	private void ValidateWorldClearance()
	{
		FpsHillMapSettings hill = _map.HillMapSettings!;
		float radius = _map.BruiserArchetypeSettings!.CollisionRadius;
		float height = _map.BruiserArchetypeSettings.CollisionHeight;
		float room = hill.RoomHalfSize;
		float interiorLimit = _map.SampleHillCrowdMaxFootHeight(new Vector3(0f, hill.SummitHeight, 0f), radius, height);
		Check(hill.RoofEnabled && interiorLimit + height <= hill.SummitHeight + hill.RoomWallHeight + 0.01f, "Real summit roof clearance accounts for the complete enemy body height");
		Vector3 doorway = new(0f, hill.SummitHeight, -room);
		float doorLimit = _map.SampleHillCrowdMaxFootHeight(doorway, radius, height);
		Check(doorLimit + height <= hill.SummitHeight + hill.DoorHeight + 0.01f, "North door lintel clearance accounts for the complete enemy body height");
		Vector3 from = new(0f, hill.SummitHeight, -room - 2f), to = new(0f, hill.SummitHeight, -room + 2f);
		Check(_map.ConstrainHillCrowdBodyMovement(from, to, radius, height).DistanceTo(to) < 0.05f, "Ground-level body can traverse the north doorway");
		from.Y = hill.SummitHeight + hill.DoorHeight;
		to.Y = from.Y;
		Check(_map.ConstrainHillCrowdBodyMovement(from, to, radius, height).DistanceTo(to) > 0.5f, "Raised body cannot pass horizontally through the door lintel");
		from = new Vector3(room - 2f, hill.SummitHeight + 2f, -room - 2f);
		to = new Vector3(room - 2f, hill.SummitHeight + 2f, -room + 2f);
		Check(_map.ConstrainHillCrowdBodyMovement(from, to, radius, height).DistanceTo(to) > 0.5f, "Raised body cannot cross the solid room wall beside the doorway");
		float roofTop = hill.SummitHeight + Mathf.Max(4f, hill.RoomWallHeight) + 0.5f;
		from.Y = roofTop;
		to.Y = roofTop;
		Check(_map.ConstrainHillCrowdBodyMovement(from, to, radius, height).DistanceTo(to) < 0.05f, "Body with feet above the roof slab can cross the room wall and roof edge");
		from = new Vector3(0f, roofTop, -room - 3f);
		to = new Vector3(0f, roofTop, -room + 3f);
		Check(_map.ConstrainHillCrowdBodyMovement(from, to, radius, height).DistanceTo(to) < 0.05f, "High body can climb over the north lintel onto the roof");
		Check(Mathf.IsEqualApprox(_map.SampleHillCrowdSupportHeight(to, to, radius, height), roofTop), "Real roof slab supplies its top as support to a body already above it");
		Vector3 beneath = new(0f, hill.SummitHeight, 0f);
		Check(Mathf.IsEqualApprox(_map.SampleHillCrowdSupportHeight(beneath, beneath, radius, height), hill.SummitHeight), "Roof support cannot teleport a body upward from underneath");
	}
	private void ValidateVerticalMelee()
	{
		// A real manager fixture isolates vertical range from the room's line-of-sight walls.
		// It does not participate in the natural pile acceptance or rendered capture.
		FpsHordeManager fixture = new() { Settings = new FpsHordeSettings { Capacity = 100, SoftCollisionEnabled = false, StackingEnabled = false } };
		AddChild(fixture);
		FpsEnemyArchetypeSettings archetype = new() { MoveSpeed = 0f, AttackRange = 2f, AttackCooldownSeconds = 0.05f, AttackDamage = 1f };
		FpsHordeHandle handle = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(1f, 0f, 0f));
		int attacks = 0;
		fixture.Attack += _ => attacks++;
		fixture.TrySetPosition(handle, new Vector3(1f, 6f, 0f));
		for (int i = 0; i < 3; i++) fixture.TickSimulation(1d / 30d);
		Check(attacks == 0, "Elevated melee body cannot bite a target far below despite close XZ distance");
		fixture.TrySetPosition(handle, new Vector3(1f, 0f, 0f));
		for (int i = 0; i < 3; i++) fixture.TickSimulation(1d / 30d);
		Check(attacks > 0, "Melee body can attack a nearby target within its actual 3D range");
		RemoveChild(fixture);
		fixture.QueueFree();
	}
	private void ValidatePhysicalSupports()
	{
		// Controlled two-body contact fixture checks support semantics independently of
		// the later pile acceptance, which starts with ordinary ground-level spawns.
		FpsHordeManager fixture = new() { Settings = new FpsHordeSettings { Capacity = 100, SoftCollisionEnabled = true, StackingEnabled = false, MaxPileHeight = 10f } };
		AddChild(fixture);
		FpsEnemyArchetypeSettings archetype = new() { MoveSpeed = 0f };
		FpsHordeHandle lower = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(50f, 0f, -76f));
		FpsHordeHandle upper = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(50f, 0f, -76f));
		fixture.TrySetPosition(upper, new Vector3(50f, archetype.CollisionHeight, -76f));
		for (int i = 0; i < 30; i++) fixture.TickSimulation(1d / 30d);
		Check(fixture.TryGet(upper, out FpsHordeEnemySnapshot top) && Mathf.Abs(top.Position.Y - archetype.CollisionHeight) < 0.05f, "Disabling new climbing retains physical support from an existing lower body");
		fixture.Settings.MaxPileHeight = 0f;
		for (int i = 0; i < 30; i++) fixture.TickSimulation(1d / 30d);
		Check(fixture.TryGet(upper, out top) && Mathf.Abs(top.Position.Y - archetype.CollisionHeight) < 0.05f, "Lowering climb-height limit does not make a supported body fall through a living body");
		fixture.ApplyDamage(lower, archetype.MaxHealth + 1f);
		fixture.FlushDeathEvents();
		for (int i = 0; i < 45; i++) fixture.TickSimulation(1d / 30d);
		Check(fixture.TryGet(upper, out top) && top.Position.Y < 0.05f, "Removing the supporting body releases gravity even when climbing is disabled");
		RemoveChild(fixture);
		fixture.QueueFree();
	}
	private async Task ValidateRoofSupport()
	{
		// Explicit height is confined to a geometry regression: natural pressure tests below
		// still start at ground level and must create their own pile through contact solving.
		_map.DebugSpawnHorde(1, true);
		FpsHordeHandle handle = Alive()[0].Handle;
		float roofTop = _map.HillMapSettings!.SummitHeight + Mathf.Max(4f, _map.HillMapSettings.RoomWallHeight) + 0.5f;
		float room = _map.HillMapSettings.RoomHalfSize;
		_crowd.TrySetPosition(handle, new Vector3(0f, roofTop, -room - 4f));
		_crowd.TrySetPosition(handle, new Vector3(0f, roofTop, 0f));
		await Steps(30);
		Check(_crowd.TryGet(handle, out FpsHordeEnemySnapshot supported) && Mathf.Abs(supported.Position.Y - roofTop) < 0.05f, "Stationary body above the actual roof remains supported by the roof slab");
		_crowd.TrySetPosition(handle, new Vector3(0f, roofTop, -room - 4f));
		await Steps(2);
		Check(_crowd.TryGet(handle, out FpsHordeEnemySnapshot falling) && falling.Position.Y < roofTop, "Leaving the real roof footprint begins gravity-driven edge fall");
		await Steps(120);
		Check(_crowd.TryGet(handle, out falling) && Lift(falling) < 0.05f, "Body falling from the roof edge lands on terrain rather than retaining roof support");
	}

	private async Task ValidateNaturalPile()
	{
		Check(_map.DebugSpawnHorde(2000, true) == 2000, "Natural pressure test starts with 2000 bounded nonoverlapping spawns");
		HashSet<FpsHordeHandle> initial = Alive().Select(e => e.Handle).ToHashSet();
		float maxLift = 0f;
		FpsHordeEnemySnapshot elevated = default;
		for (int block = 0; block < 45; block++)
		{
			await Steps(30);
			foreach (FpsHordeEnemySnapshot enemy in Alive())
			{
				float lift = Lift(enemy);
				if (lift > maxLift) { maxLift = lift; elevated = enemy; }
			}
			ValidateBounds(false);
			if (maxLift > 1f && block >= 17 && Alive().Any(OnRoof)) break;
		}
		List<FpsHordeEnemySnapshot> pile = Alive();
		GD.Print($"Natural pile diagnostic: active={pile.Count}, maximum observed foot lift={maxLift:F3}m, currently elevated={pile.Count(e => Lift(e) > 0.35f)}, simulation steps={_crowd.SimulationSteps}");
		Check(pile.Count == 2000 && pile.All(e => initial.Contains(e.Handle)), "Natural collision piling retains all 2000 original live handles");
		Check(maxLift > 0.75f && pile.Any(e => Lift(e) > 0.35f), "Unmodified crowd pressure naturally raises enemy feet above the terrain");
		Check(pile.Any(OnRoof), "Unmodified 2000-enemy pressure naturally carries bodies onto the actual roof");
		ValidateBounds(true);
		Check(pile.Any(e => Mathf.Abs(e.Position.X) < _map.HillMapSettings!.RoomHalfSize - 1f && e.Position.Z > -_map.HillMapSettings.RoomHalfSize + 1f), "Crowd still traverses the north doorway while piling");
		// Pause freezes actual elevated and grounded records together.
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		Dictionary<FpsHordeHandle, Vector3> paused = Alive().ToDictionary(e => e.Handle, e => e.Position);
		await Steps(30);
		Check(Alive().All(e => paused[e.Handle] == e.Position), "Pause freezes horizontal contacts and vertical pile motion");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		// Select a current top, not the historical peak that may already have fallen.
		elevated = Alive().Where(e => Mathf.Abs(SupportHeight(e) - _map.SampleHillSurfaceHeight(e.Position)) < 0.05f).OrderByDescending(Lift).First();
		ValidateRaisedHitCapsule(elevated);
		// Every remaining support is removed through real death bookkeeping. The survivor
		// must fall under gravity without an injected elevation or an artificial support callback.
		_map.BruiserArchetypeSettings!.MoveSpeed = 0f;
		_map.ApplyHordeArchetypeChangesLive();
		_map.DefenseSettings!.WeaponDropChance = 0f;
		_map.DefenseSettings.ModulatorDropChance = 0f;
		foreach (FpsHordeEnemySnapshot enemy in Alive()) if (enemy.Handle != elevated.Handle) _crowd.ApplyDamage(enemy.Handle, enemy.MaxHealth + 1f);
		_crowd.FlushDeathEvents();
		float before = Lift(elevated);
		await Steps(2);
		Check(_crowd.TryGet(elevated.Handle, out FpsHordeEnemySnapshot falling) && Lift(falling) < before, "Removing real supports causes the elevated survivor to fall");
		await Steps(120);
		Check(_crowd.TryGet(elevated.Handle, out falling) && Lift(falling) < 0.05f && _map.ActiveEnemyCount == 1, "Unsupported survivor settles onto terrain without floating or losing its identity");
		// New ground-level population must remain unstacked with pressure climbing disabled;
		// the independent contact fixture above covers retaining existing physical supports.
		_map.BruiserArchetypeSettings.MoveSpeed = GD.Load<FpsEnemyArchetypeSettings>("res://Resources/Fps/NorthGate_Cube.tres").MoveSpeed;
		_map.ApplyHordeArchetypeChangesLive();
		_crowd.Settings.StackingEnabled = false;
		_map.DebugSpawnHorde(2000, true);
		await Steps(540);
		Check(Alive().All(e => e.Position.Y - SupportHeight(e) < 0.05f) && _crowd.ActiveCount == 2000, "Disabling pressure climbing prevents a fresh 2000-body wave from building new elevated layers");
	}

	private void ValidateRaisedHitCapsule(FpsHordeEnemySnapshot top)
	{
		float ground = _map.SampleHillSurfaceHeight(top.Position);
		float rayY = top.Position.Y + top.CollisionHeight * 0.85f;
		Check(rayY > ground + top.CollisionHeight, "Natural elevated target exposes a hit region above a grounded body");
		Vector3 from = new(top.Position.X - 1.5f, rayY, top.Position.Z), to = new(top.Position.X + 1.5f, rayY, top.Position.Z);
		List<FpsHordeEnemySnapshot> candidates = new();
		_crowd.QuerySegment(from, to, 0f, candidates);
		Check(candidates.Any(e => e.Handle == top.Handle), "Crowd segment queries include the actual elevated hit capsule");
		var targets = (Dictionary<FpsHordeHandle, FpsHordeCombatEnemy>)typeof(FpsVerticalSlice).GetField("_hordeTargets", Private)!.GetValue(_map)!;
		object[] hitArgs = { targets[top.Handle], from, to, 0f, float.PositiveInfinity };
		Check((bool)typeof(FpsVerticalSlice).GetMethod("TryEnemySegmentHit", Private)!.Invoke(_map, hitArgs)!, "FPS narrow-phase ray intersection uses the raised body capsule");
		EquipmentItem weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		Invoke("DealDamageToEnemy", targets[top.Handle], 1f, top.HeadPosition, weapon, false, false);
		Check(_crowd.TryGet(top.Handle, out FpsHordeEnemySnapshot after) && after.CurrentHealth < top.CurrentHealth, "Actual elevated ray candidate receives damage through the FPS combat path");
	}
	private void ValidateBounds(bool report)
	{
		List<FpsHordeEnemySnapshot> alive = Alive();
		float half = _map.HillMapSettings!.PlayableSize * 0.5f;
		bool finite = alive.All(e => e.Position.IsFinite() && Mathf.Abs(e.Position.X) <= half - e.Radius + 0.02f && Mathf.Abs(e.Position.Z) <= half - e.Radius + 0.02f && Lift(e) >= -0.05f && e.Position.Y <= _map.SampleHillCrowdSupportHeight(e.Position, e.Position, e.Radius, e.CollisionHeight) + _crowd.Settings.MaxPileHeight + 0.1f && e.Position.Y <= _map.SampleHillCrowdMaxFootHeight(e.Position, e.Radius, e.CollisionHeight) + 0.1f);
		if (report) Check(finite, "2000-body pile remains finite, above terrain, inside boundaries and below pile/roof/lintel height limits");
		else if (!finite) throw new InvalidOperationException("Pile position escaped finite world/height bounds during pressure test");
	}
	private float Lift(FpsHordeEnemySnapshot enemy) => enemy.Position.Y - _map.SampleHillSurfaceHeight(enemy.Position);
	private float SupportHeight(FpsHordeEnemySnapshot enemy) => _map.SampleHillCrowdSupportHeight(enemy.Position, enemy.Position, enemy.Radius, enemy.CollisionHeight);
	private bool OnRoof(FpsHordeEnemySnapshot enemy)
	{
		FpsHillMapSettings hill = _map.HillMapSettings!;
		float top = hill.SummitHeight + Mathf.Max(4f, hill.RoomWallHeight) + 0.5f;
		return Mathf.Abs(enemy.Position.X) < hill.RoomHalfSize - 0.5f && Mathf.Abs(enemy.Position.Z) < hill.RoomHalfSize - 0.5f && enemy.Position.Y >= top - 0.05f;
	}
	private List<FpsHordeEnemySnapshot> Alive() { List<FpsHordeEnemySnapshot> result = new(); _crowd.CopyAlive(result); return result; }
	private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
	private async Task Steps(int count)
	{
		for (int i = 0; i < count; i++) _map._PhysicsProcess(1d / 30d);
		await Frames(1);
	}
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); _checks++; GD.Print($"PASS: {text}"); }
}
