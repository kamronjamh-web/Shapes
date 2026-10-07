using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Physical scene contracts are separate from screenshots and frame-time acceptance.
public partial class ValidateFortDefense : Node
{
	private FpsVerticalSlice _map = null!;
	private FpsHordeManager _crowd = null!;
	private int _checks;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/fort-defense-validation/"), "Fort validation isolates normal save data");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			AddChild(_map); await Frames(4);
			_map.SetPhysicsProcess(false);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			_crowd = _map.HordeManager!;
			_map._UnhandledInput(new InputEventKey { Keycode = Key.N, Pressed = true });
			await Frames(3);
			List<FpsHordeEnemySnapshot> configured = Alive();
			Check(configured.Count == 100 && Enumerable.Range(0, 4).All(side => configured.Count(e => Sector(e.Position) == side) == 25), "Default configured wave spawns one hundred enemies with25 in each cardinal sector");
			_map.DebugClearHorde();
			ValidateDimensions();
			await ValidateWalkability();
			await ValidateLargeGrid();
			await ValidateFortMenu();
			await ValidateGates();
			await ValidateRangedGateAttack();
			GD.Print($"Fort defense validation passed: {_checks} checks."); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private void ValidateDimensions()
	{
		FpsHillMapSettings hill = _map.HillMapSettings!;
		Check(hill.FortificationsEnabled && Mathf.IsEqualApprox(hill.PlayableSize, 1609.344f), "Default fort spans one mile on each axis");
		Rect2 bounds = _map.GetHillMovementBounds();
		Check(bounds.Size.IsEqualApprox(new Vector2(1609.344f, 1609.344f)) && bounds.Position.IsEqualApprox(-bounds.Size * 0.5f), "Movement limits cover the entire centered mile map");
		Check(_map.DefenseGates.Count == 4 && _map.DefenseGates.Select(g => g.SideIndex).Distinct().Count() == 4, "All four cardinal approaches have distinct live gates");
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = Outward(side), across = new(-outward.Z, 0f, outward.X);
			StaticBody3D wall = Descendants(_map).OfType<StaticBody3D>().Single(n => n.Name == $"FortWall{side}_1");
			CollisionShape3D shape = wall.GetChildren().OfType<CollisionShape3D>().Single();
			Vector3 size = ((BoxShape3D)shape.Shape).Size;
			float halfThickness = (side % 2 == 0 ? size.Z : size.X) * 0.5f;
			Check(Mathf.Abs(wall.GlobalPosition.Dot(outward) - halfThickness - 100f) < 0.01f && Mathf.Abs(size.Y - 6.096f) < 0.01f, $"Side {side} physical wall leaves a 200m interior and rises20ft");
			Check(!Ray(outward * 99f + across * 50f + Vector3.Up * 9f, outward * 98f + across * 50f + Vector3.Up * 9f).Any(), $"Side {side} interior is physically clear just inside the wall");
			FpsDefenseGate gate = _map.DefenseGates.Single(g => g.SideIndex == side);
			Check(Mathf.Abs((side % 2 == 0 ? gate.Dimensions.X : gate.Dimensions.Z) - 4.572f) < 0.001f && Mathf.Abs(gate.GlobalPosition.Dot(outward) - 101f) < 0.01f, $"Side {side} gate occupies the centered15ft opening");
			Check(Ray(outward * 98f + Vector3.Up * 9f, outward * 104f + Vector3.Up * 9f).Any(), $"Side {side} closed gate has real physical collision");
		}
		Check(Mathf.Abs(Floor(new Vector3(90f, 0f, 80f)) - 8f) < 0.02f && Mathf.Abs(Floor(new Vector3(150f, 0f, 150f)) - _map.SampleHillSurfaceHeight(new Vector3(150f, 0f, 150f))) < 0.05f && Mathf.Abs(Floor(new Vector3(300f, 0f, 300f))) < 0.05f, "Real terrain keeps the base flat and slopes down outside its walls");
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		Check(Descendants(player).OfType<Camera3D>().Single().Far > 1609.344f, "Player camera can see beyond the old400m far plane");
	}
	private async Task ValidateWalkability()
	{
		const float top = 14.096f;
		for (int side = 0; side < 4; side++)
		{
			Vector3 outward = Outward(side), across = new(-outward.Z, 0f, outward.X);
			Check(new[] { -100.5f, -50f, 0f, 50f, 100.5f }.All(offset => Mathf.Abs(Floor(outward * 100.5f + across * offset) - top) < 0.03f), $"Side {side} walltop is continuously supported across the gate and corners");
			foreach (int sign in new[] { -1, 1 })
			{
				float flank = sign * 6.786f;
				Check(Mathf.Abs(Floor(outward * 105f + across * flank) - top) < 0.03f && Mathf.Abs(Floor(outward * 89.856f + across * flank) - 11.048f) < 0.04f, $"Side {side} platform {sign} has real deck and graded ramp collision");
				Check(!Ray(outward * 107.5f + across * flank + Vector3.Up * (top + 1.13f), outward * 111f + Vector3.Up * 9f).Any(), $"Side {side} platform {sign} permits firing down into the gate approach from its outer edge");
			}
		}
		CharacterBody3D walker = new() { Name = "FortWalkProbe", CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = 0.3f, FloorMaxAngle = Mathf.DegToRad(40f) };
		walker.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = Vector3.Up * 0.9f });
		_map.AddChild(walker);
		walker.Position = new Vector3(6.786f, 8.1f, -79.8f);
		await Frames(2);
		for (int step = 0; step < 400 && walker.Position.Z > -100.5f; step++)
		{
			walker.Velocity = new Vector3(0f, walker.IsOnFloor() ? -0.5f : walker.Velocity.Y - 18f / 60f, -5f);
			walker.MoveAndSlide(); await Frames(1);
		}
		Check(walker.Position.Y > top - 0.08f && walker.Position.Z < -99f && walker.IsOnFloor(), $"Player-sized physical capsule walks from base up the access ramp onto walltop (position {walker.Position}, floor {walker.IsOnFloor()}, contacts {walker.GetSlideCollisionCount()})");
		walker.Position = new Vector3(20f, top + 0.1f, -100.5f); walker.Velocity = Vector3.Zero; await Frames(2);
		for (int step = 0; step < 120; step++) { walker.Velocity = new Vector3(5f, -0.5f, 0f); walker.MoveAndSlide(); await Frames(1); }
		Check(walker.Position.X > 28f && Mathf.Abs(walker.Position.Y - top) < 0.08f, "Player-sized capsule traverses the actual wallwalk without falling");
		walker.QueueFree(); await Frames(2);
	}
	private async Task ValidateLargeGrid()
	{
		Check(_map.DebugSpawnHorde(2000, true) == 2000, "Fort debug command spawns exactly2000 live shapes");
		List<FpsHordeEnemySnapshot> alive = Alive();
		int[] sectors = new int[4];
		foreach (FpsHordeEnemySnapshot enemy in alive)
		{
			Vector3 p = enemy.Position;
			int sector = Sector(p);
			sectors[sector]++;
		}
		Check(sectors.All(n => n == 500), "Two thousand enemies distribute evenly across all four spawn sectors");
		Check(alive.All(e => float.IsFinite(e.Position.X) && float.IsFinite(e.Position.Y) && float.IsFinite(e.Position.Z) && Mathf.Abs(e.Position.X) + e.Radius < 804.672f && Mathf.Abs(e.Position.Z) + e.Radius < 804.672f && Mathf.Max(Mathf.Abs(e.Position.X), Mathf.Abs(e.Position.Z)) > 102f), "Spawn positions remain finite outside the fort and inside the mile boundary");
		_map.DebugSpawnHorde(1, true);
		FpsHordeHandle handle = Alive().Single().Handle;
		_crowd.TrySetPosition(handle, new Vector3(700f, 0f, -700f));
		_crowd.TryGet(handle, out FpsHordeEnemySnapshot far);
		Check(far.Position.X > 699f && far.Position.Z < -699f, "Live position beyond500m avoids legacy grid-bound clamping");
		List<FpsHordeEnemySnapshot> query = new();
		_crowd.QueryRadius(far.Position, 2f, query);
		Check(query.Any(e => e.Handle == handle) && _crowd.TryNearest(far.Position, 3f, out FpsHordeEnemySnapshot nearest) && nearest.Handle == handle, "Radius and nearest queries find real enemies beyond the old grid edge");
		_crowd.QuerySegment(far.HeadPosition - Vector3.Right * 4f, far.HeadPosition + Vector3.Right * 4f, 0.1f, query);
		Check(query.Any(e => e.Handle == handle), "Segment queries find displaced enemies beyond500m");
		int hits = (int)Invoke("DamageEnemiesInRadius", far.Position, 3f, 1f, null!, false)!;
		_crowd.TryGet(handle, out FpsHordeEnemySnapshot damaged);
		Check(hits == 1 && Mathf.IsEqualApprox(damaged.CurrentHealth, far.CurrentHealth - 1f), "Actual weapon area path damages the far-world target");
		await Frames(1);
	}
	private async Task ValidateFortMenu()
	{
		FpsHillMapSettings cached = GD.Load<FpsHillMapSettings>("res://Resources/Fps/HillFort_Hill.tres");
		var healthBefore = _map.DefenseGates.ToDictionary(g => g.SideIndex, g => (g.CurrentHealth, g.MaxHealth));
		_map._Input(new InputEventKey { Keycode = Key.Quoteleft, PhysicalKeycode = Key.Quoteleft, Pressed = true });
		await Frames(2);
		Control menu = Field<Control>("_gameplayTuningMenu");
		TabContainer tabs = Descendants(menu).OfType<TabContainer>().Single();
		int fortIndex = Enumerable.Range(0, tabs.GetTabCount()).Single(i => tabs.GetTabTitle(i) == "Fort");
		tabs.CurrentTab = fortIndex;
		SpinBox maximum = Descendants(tabs.GetTabControl(fortIndex)).OfType<SpinBox>().Single();
		Check(menu.Visible && maximum.Value == 1000d && _map.HillMapSettings!.GateMaxHealth == 1000f && _map.DefenseGates.All(g => healthBefore[g.SideIndex] == (g.CurrentHealth, g.MaxHealth)), "Opening grave Fort tab displays actual1000 gate health without mutating health");
		FpsDefenseGate wounded = _map.DefenseGates.Single(g => g.SideIndex == 0);
		wounded.ApplyEnemyDamage(250f);
		maximum.Value = 2000d;
		await Frames(3);
		Check(_map.DefenseGates.All(g => g.MaxHealth == 2000f) && Mathf.IsEqualApprox(_map.DefenseGates.Single(g => g.SideIndex == 0).HealthFraction, 0.75f) && _map!.HillMapSettings!.GateMaxHealth == 2000f && cached.GateMaxHealth == 1000f && !ReferenceEquals(cached, _map.HillMapSettings), "Fort numeric signal retunes live gate health while preserving damage fraction and cached1000 resource");
		maximum.Value = 1000d;
		await Frames(3);
		Button restore = Descendants(tabs.GetTabControl(fortIndex)).OfType<Button>().Single(b => b.Text == "Restore all gates");
		restore.EmitSignal(Button.SignalName.Pressed);
		await Frames(2);
		Check(_map.DefenseGates.All(g => g.MaxHealth == 1000f && g.CurrentHealth == 1000f && g.CollisionLayer == 1), "Fort Restore all gates button signal restores full physical gates after tuning");
		_map._Input(new InputEventKey { Keycode = Key.Quoteleft, PhysicalKeycode = Key.Quoteleft, Pressed = true });
		await Frames(1);
	}
	private async Task ValidateRangedGateAttack()
	{
		_map.DebugResetDefenseGates();
		FpsDefenseGate gate = _map.DefenseGates.Single(g => g.SideIndex == 0);
		gate.Retune(1000f); gate.ResetHealth();
		_map.DebugSpawnRole = FpsEnemyRole.Striker;
		_map.StrikerArchetypeSettings!.MoveSpeed = 0f;
		_map.StrikerArchetypeSettings.DesiredRange = 20f;
		_map.StrikerArchetypeSettings.ProjectileSpeed = 30f;
		_map.StrikerArchetypeSettings.ProjectileDamage = 11f;
		_map.StrikerArchetypeSettings.AttackCooldownSeconds = 0.2f;
		_map.DebugSpawnHorde(1, true); _map.ApplyHordeArchetypeChangesLive();
		FpsHordeHandle handle = Alive().Single().Handle;
		Vector3 position = new(0f, 0f, -116f); position.Y = _map.SampleHillSurfaceHeight(position);
		_crowd.TrySetPosition(handle, position);
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		float coreBefore = core.CurrentHealth;
		bool emitted = false, nativeProjectile = false;
		void Attack(FpsHordeAttack attack) { if (attack.TargetPosition.DistanceSquaredTo(gate.Position + Vector3.Up * 1.1f) < 0.01f && attack.ProjectileSpeed > 0f) emitted = true; }
		_crowd.Attack += Attack;
		for (int step = 0; step < 120 && gate.CurrentHealth == gate.MaxHealth; step++)
		{
			await Steps(1);
			nativeProjectile |= Descendants(_map).OfType<FpsEnemyProjectile>().Any(p => p.Damage == 11f);
		}
		_crowd.Attack -= Attack;
		Check(emitted && nativeProjectile, "Real Striker gate targeting and line of sight create a native enemy projectile");
		Check(gate.CurrentHealth < gate.MaxHealth && core.CurrentHealth == coreBefore, "Native moving projectile ray damages the closed gate before the core");
		_map.DebugSpawnRole = FpsEnemyRole.Bruiser;
	}
	private async Task ValidateGates()
	{
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		FpsDefenseGate wounded = _map.DefenseGates.Single(g => g.SideIndex == 0), breached = _map.DefenseGates.Single(g => g.SideIndex == 1);
		wounded.ApplyEnemyDamage(wounded.MaxHealth * 0.25f);
		breached.ApplyEnemyDamage(breached.MaxHealth);
		_map.HillMapSettings!.RoomColor = new Color(0.37f, 0.41f, 0.43f);
		await Frames(4);
		Check(Mathf.Abs(_map.DefenseGates.Single(g => g.SideIndex == 0).HealthFraction - 0.75f) < 0.001f && !_map.DefenseGates.Single(g => g.SideIndex == 1).IsAlive, "Live geometry rebuild preserves wounded fractions and already breached gates");
		_map.DebugResetDefenseGates();
		Check(_map.DefenseGates.All(g => g.IsAlive && g.CurrentHealth == g.MaxHealth), "Explicit gate reset restores all four gate health values");
		breached = _map.DefenseGates.Single(g => g.SideIndex == 1);
		breached.ApplyEnemyDamage(breached.MaxHealth);
		_map.DebugSpawnHorde(1, true);
		FpsHordeHandle overlapHandle = Alive().Single().Handle;
		Place(overlapHandle, breached.Position);
		_crowd.Settings.StackingEnabled = false;
		_map.DebugResetDefenseGates();
		await Steps(3);
		_crowd.TryGet(overlapHandle, out FpsHordeEnemySnapshot depenetrated);
		Check(Mathf.Abs(depenetrated.Position.X - breached.Position.X) >= 1f + depenetrated.Radius - 0.06f, "Restoring a gate resolves a crowd body occupying its opening");
		_map.DebugCoreInvulnerable = false;
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		player.Position = new Vector3(400f, 0.1f, 400f);
		_crowd.Settings.PlayerAggroRadius = 0f; _crowd.Settings.StackingEnabled = false;
		_map.BruiserArchetypeSettings!.MoveSpeed = 12f;
		_map.BruiserArchetypeSettings.AttackDamage = 10f;
		_map.BruiserArchetypeSettings.AttackCooldownSeconds = 0.2f;
		for (int side = 0; side < 4; side++)
		{
			FpsDefenseGate gate = _map.DefenseGates.Single(g => g.SideIndex == side);
			gate.Retune(30f); gate.ResetHealth();
			_map.DebugSpawnHorde(1, true);
			_map.ApplyHordeArchetypeChangesLive();
			FpsHordeHandle handle = Alive().Single().Handle;
			Vector3 outward = Outward(side), outside = outward * 104f;
			outside.Y = _map.SampleHillSurfaceHeight(outside);
			Place(handle, outside);
			Vector3 desired = outward * 98f + Vector3.Up * 8f;
			Vector3 blocked = _crowd.ConstrainBodyMovement!(outside, desired, 0.55f, 1.2f);
			Check(blocked.Dot(outward) > 102f, $"Side {side} live gate prevents ground-level enemy phasing");
			float coreBefore = core.CurrentHealth;
			for (int step = 0; step < 180 && gate.CurrentHealth == gate.MaxHealth; step++) await Steps(1);
			Check(gate.CurrentHealth < gate.MaxHealth && core.CurrentHealth == coreBefore, $"Side {side} real crowd attacks its gate before damaging the core");
			for (int step = 0; step < 180 && gate.IsAlive; step++) await Steps(1);
			Check(!gate.IsAlive && gate.CollisionLayer == 0, $"Side {side} repeated enemy attacks breach gate health and remove collision");
			await Frames(2);
			Vector3 passed = _crowd.ConstrainBodyMovement!(outside, desired, 0.55f, 1.2f);
			Check(passed.Dot(outward) < 100f && !Ray(outward * 98f + Vector3.Up * 9f, outward * 104f + Vector3.Up * 9f).Any(), $"Side {side} breached15ft opening passes both analytic movement and physics rays");
			for (int step = 0; step < 450 && core.CurrentHealth == coreBefore; step++) await Steps(1);
			Check(core.CurrentHealth < coreBefore, $"Side {side} surviving attacker crosses the breach and then damages the core");
		}
	}
	private float Floor(Vector3 point)
	{
		var hit = Ray(new Vector3(point.X, 30f, point.Z), new Vector3(point.X, -2f, point.Z));
		if (!hit.Any()) throw new InvalidOperationException($"No physical terrain/deck at {point}");
		return ((Vector3)hit["position"]).Y;
	}
	private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to) => _map.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1));
	private static Vector3 Outward(int side) => side switch { 1 => Vector3.Right, 2 => Vector3.Back, 3 => Vector3.Left, _ => Vector3.Forward };
	private static int Sector(Vector3 p) => Mathf.Abs(p.X) > Mathf.Abs(p.Z) ? (p.X > 0f ? 1 : 3) : (p.Z > 0f ? 2 : 0);
	private static IEnumerable<Node> Descendants(Node node) { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; } }
	private List<FpsHordeEnemySnapshot> Alive() { List<FpsHordeEnemySnapshot> result = new(); _crowd.CopyAlive(result); return result; }
	private void Place(FpsHordeHandle handle, Vector3 position)
	{
		float along = Mathf.Max(Mathf.Abs(position.X), Mathf.Abs(position.Z));
		if (along >= 99f && along <= 103f && position.Y < 14.096f)
		{
			// Lower outside the overhead wallwalk, then enter below it through the breach.
			Vector3 outward = Outward(Sector(position)), across = new(-outward.Z, 0f, outward.X);
			Vector3 approach = outward * 110f + across * position.Dot(across);
			approach.Y = _map.SampleHillSurfaceHeight(approach);
			Place(handle, approach);
			_crowd.TrySetPosition(handle, position);
			return;
		}
		// Relocation across the fort respects walls. Route a controlled fixture above
		// the deck before lowering it; natural population tests never use this helper.
		_crowd.TryGet(handle, out FpsHordeEnemySnapshot current);
		_crowd.TrySetPosition(handle, new Vector3(current.Position.X, 20f, current.Position.Z));
		_crowd.TrySetPosition(handle, new Vector3(position.X, 20f, position.Z));
		_crowd.TrySetPosition(handle, position);
	}
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
	private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
	private async Task Steps(int count) { for (int i = 0; i < count; i++) { _map._PhysicsProcess(1.0 / 30.0); if (i % 60 == 0) await Frames(1); } }
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); _checks++; GD.Print($"PASS: {description}"); }
}
