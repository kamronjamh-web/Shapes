using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateEnemyMotion : Node
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private FpsVerticalSlice _map = null!;
	private FpsHordeManager _crowd = null!;
	private FpsEnemyCorpsePool _corpses = null!;
	private int _checks;
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/enemy-motion-validation/"), "Motion validation isolates normal save data");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyHillFixture.Configure(_map);
			AddChild(_map);
			await Frames(4);
			_map.SetPhysicsProcess(false);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			_crowd = _map.HordeManager!;
			_corpses = _map.CorpsePool!;
			Check(_corpses != null && _corpses.Settings.CorpseEnabled, "North Gate installs an enabled bounded corpse pool");
			await ValidateFatalSources();
			await ValidateRolling();
			await ValidateUnsupportedRoofBodies();
			GD.Print($"Enemy motion validation passed: {_checks} checks.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private async Task ValidateFatalSources()
	{
		_crowd.Settings.Enabled = false;
		_corpses.Settings.CorpseCapacity = 8;
		_corpses.Settings.CorpseLifetimeSeconds = 1f;
		_map.DefenseSettings!.WeaponDropChance = 0f;
		_map.DefenseSettings.ModulatorDropChance = 0f;
		_map.DebugSpawnHorde(3, true);
		List<FpsHordeEnemySnapshot> enemies = Alive();
		for (int i = 0; i < enemies.Count; i++) _crowd.TrySetPosition(enemies[i].Handle, new Vector3(60f + i * 10f, 0f, -76f));
		FpsHordeHandle bulletTarget = enemies[0].Handle;
		_crowd.TryGet(bulletTarget, out FpsHordeEnemySnapshot bulletBefore);
		_crowd.ApplyDamageFrom(bulletTarget, bulletBefore.MaxHealth - 2f, Vector3.Back);
		_crowd.TryGet(bulletTarget, out bulletBefore);
		Check(bulletBefore.CurrentHealth == 2f && Corpses().Count == 0, "Nonfatal damage wounds a real record without creating a corpse");
		EquipmentItem weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		weapon.Modulators.Clear();
		weapon.Substats.Clear();
		Vector3 origin = bulletBefore.HeadPosition - Vector3.Right * 4f;
		Invoke("FireSquareProjectiles", weapon, origin, Vector3.Right, 1f);
		await Steps(4);
		FpsEnemyCorpseSnapshot bullet = Corpses().Single(c => c.EnemyHandle == bulletTarget);
		Check(!_crowd.IsAlive(bulletTarget), "Actual moving Square bullet kills the wounded crowd record");
		Check(Mathf.IsEqualApprox(bullet.Overkill, bullet.FatalDamage - bulletBefore.CurrentHealth), "Fatal overkill uses remaining health immediately before the hit");
		Check(bullet.InitialLaunchVelocity.X > 0f && Mathf.Abs(bullet.InitialLaunchVelocity.Z) < 0.01f, "Bullet corpse launches along incoming projectile travel rather than away from the player");
		FpsHordeHandle blastTarget = enemies[1].Handle;
		_crowd.TryGet(blastTarget, out FpsHordeEnemySnapshot blastBefore);
		Vector3 center = blastBefore.Position + Vector3.Back * 4f;
		Invoke("DamageEnemiesInRadius", center, 5f, blastBefore.MaxHealth + 5f, null!, false);
		_crowd.FlushDeathEvents();
		FpsEnemyCorpseSnapshot blast = Corpses().Single(c => c.EnemyHandle == blastTarget);
		Check(blast.InitialLaunchVelocity.Z < 0f && Mathf.Abs(blast.InitialLaunchVelocity.X) < 0.01f, "Explosion corpse launches away from the actual off-player explosion center");
		Check(Mathf.IsEqualApprox(blast.Overkill, 5f), "Explosion preserves fatal damage and overkill metadata");
		Vector3 travelVelocity = bullet.InitialLaunchVelocity - Vector3.Up * _corpses.Settings.CorpseUpwardSpeed;
		float expectedSpeed = Mathf.Min(_corpses.Settings.CorpseMaxLaunchSpeed, _corpses.Settings.CorpseBaseLaunchSpeed + Mathf.Sqrt(bullet.Overkill) * _corpses.Settings.CorpseOverkillSpeedScale);
		Check(Mathf.IsEqualApprox(travelVelocity.Length(), expectedSpeed), "Launch strength follows the configured overkill curve and speed cap");
		Invoke("SweepDefenseLoot");
		int rewarded = Field<int>("_defenseCoins"), corpseCount = _corpses.ActiveCount;
		_crowd.ApplyDamageFrom(bulletTarget, 100000f, Vector3.Left);
		_crowd.FlushDeathEvents();
		Invoke("SweepDefenseLoot");
		Check(Field<int>("_defenseCoins") == rewarded && _corpses.ActiveCount == corpseCount, "Stale fatal hits cannot duplicate death rewards or cosmetic bodies");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		await Steps(1);
		List<FpsEnemyCorpseSnapshot> paused = Corpses();
		await Steps(30);
		List<FpsEnemyCorpseSnapshot> afterPause = Corpses();
		Check(afterPause.All(c => c.Frozen && paused.Any(p => p.Slot == c.Slot && p.WorldTransform == c.WorldTransform && p.RemainingLifetime == c.RemainingLifetime)), "Pause freezes corpse physics and lifetime together");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		await Steps(2);
		Check(Corpses().All(c => !c.Frozen) && Corpses().Any(c => paused.Any(p => p.Slot == c.Slot && c.RemainingLifetime < p.RemainingLifetime && c.WorldTransform != p.WorldTransform)), "Resume restores actual corpse physics movement and lifetime countdown");
		// A real 2000-enemy clear must allocate only the bounded cosmetic pool.
		_map.DebugSpawnHorde(2000, true);
		int coinsBefore = Field<int>("_defenseCoins"), deaths = 0;
		HashSet<FpsHordeHandle> notified = new();
		_crowd.EnemyDied += e => { deaths++; notified.Add(e.Handle); };
		List<FpsHordeEnemySnapshot> mass = Alive();
		foreach (FpsHordeEnemySnapshot enemy in mass) _crowd.ApplyDamageFrom(enemy.Handle, enemy.MaxHealth + 100f, Vector3.Right);
		_crowd.FlushDeathEvents();
		Invoke("SweepDefenseLoot");
		Check(deaths == 2000 && notified.Count == 2000 && _map.ActiveEnemyCount == 0, "Mass clear retires and notifies every crowd identity exactly once");
		Check(_corpses.ActiveCount == 8 && _corpses.Capacity == 8 && Descendants(_corpses).OfType<RigidBody3D>().Count() == 8, "Mass deaths recycle eight physics bodies instead of creating 2000 ragdoll nodes");
		Check(Field<int>("_defenseCoins") == coinsBefore + 2000 * _map.DefenseSettings.CoinsPerEnemy, "Mass corpse recycling leaves configured kill rewards intact");
		foreach (FpsHordeEnemySnapshot enemy in mass) _crowd.ApplyDamageFrom(enemy.Handle, 100000f, Vector3.Left);
		_crowd.FlushDeathEvents();
		Check(deaths == 2000 && _corpses.ActiveCount == 8, "Recycled dead handles cannot create repeated notifications or corpses");
		await Steps(90);
		Check(_corpses.ActiveCount == 0 && Corpses().Count == 0 && Descendants(_corpses).OfType<RigidBody3D>().All(b => b.Freeze && !b.Visible), "Expired corpses retire physics and rendering while keeping the bounded pool reusable");
	}

	private async Task ValidateRolling()
	{
		_crowd.Settings.Enabled = true;
		_crowd.Settings.RollingEnabled = true;
		_map.DebugSpawnHorde(1, true);
		FpsHordeHandle handle = Alive()[0].Handle;
		_crowd.TryGetRollingState(handle, out _, out _, out Quaternion first);
		Vector3 start = Alive()[0].Position;
		await Steps(30);
		_crowd.TryGetRollingState(handle, out _, out _, out Quaternion moved);
		Check(_crowd.TryGet(handle, out FpsHordeEnemySnapshot walking) && walking.Position.DistanceTo(start) > 0.25f && moved != first, "Cube visual rotation follows actual travel");
		bool observedDwell = false;
		for (int i = 0; i < 120 && !observedDwell; i++)
		{
			_crowd.TryGetRollingState(handle, out _, out float pause, out Quaternion orientation);
			_crowd.TryGet(handle, out FpsHordeEnemySnapshot before);
			if (pause > 1f / 30f)
			{
				await Steps(1);
				_crowd.TryGetRollingState(handle, out _, out _, out Quaternion after);
				_crowd.TryGet(handle, out FpsHordeEnemySnapshot still);
				observedDwell = after == orientation && still.Position.DistanceTo(before.Position) < 0.001f;
			}
			else await Steps(1);
		}
		Check(observedDwell, "Cube rolling produces a real stop interval between quarter turns");
		_map.BruiserArchetypeSettings!.MoveSpeed = 0f;
		_map.ApplyHordeArchetypeChangesLive();
		_crowd.TryGetRollingState(handle, out float stoppedDistance, out _, out Quaternion stopped);
		await Steps(30);
		_crowd.TryGetRollingState(handle, out float afterDistance, out _, out Quaternion afterStopped);
		Check(stopped == afterStopped && stoppedDistance == afterDistance, "Zero-speed cube keeps its rolling pose instead of animating in place");
		_map.BruiserArchetypeSettings.MoveSpeed = 3.5f;
		_map.ApplyHordeArchetypeChangesLive();
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		_crowd.TryGetRollingState(handle, out float pausedDistance, out _, out Quaternion paused);
		await Steps(30);
		_crowd.TryGetRollingState(handle, out float resumedDistance, out _, out Quaternion pausedAfter);
		Check(paused == pausedAfter && pausedDistance == resumedDistance, "Pause freezes live cube rolling progression");
		_map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
		await Steps(30);
		_crowd.TryGetRollingState(handle, out _, out _, out Quaternion resumed);
		Check(resumed != pausedAfter, "Resume continues cube rolling with actual movement");
		ValidateBlockedRolling();
	}
	private void ValidateBlockedRolling()
	{
		float room = _map.HillMapSettings!.RoomHalfSize;
		FpsHordeManager fixture = new()
		{
			Settings = new FpsHordeSettings { Capacity = 100 },
			CorePosition = () => new Vector3(0f, _map.HillMapSettings.SummitHeight, 0f),
			SurfaceHeight = _map.SampleHillSurfaceHeight,
			SurfaceSupportHeight = _map.SampleHillCrowdSupportHeight,
			MaxFootHeight = _map.SampleHillCrowdMaxFootHeight,
			ConstrainBodyMovement = _map.ConstrainHillCrowdBodyMovement,
		};
		AddChild(fixture);
		FpsEnemyArchetypeSettings archetype = new() { MoveSpeed = 3.5f };
		FpsHordeHandle handle = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(room + 0.5f + archetype.CollisionRadius, _map.HillMapSettings.SummitHeight, 0f));
		for (int i = 0; i < 5; i++) fixture.TickSimulation(1d / 30d);
		fixture.TryGetRollingState(handle, out float distance, out _, out Quaternion orientation);
		fixture.TryGet(handle, out FpsHordeEnemySnapshot before);
		for (int i = 0; i < 30; i++) fixture.TickSimulation(1d / 30d);
		fixture.TryGetRollingState(handle, out float afterDistance, out _, out Quaternion afterOrientation);
		fixture.TryGet(handle, out FpsHordeEnemySnapshot after);
		Check(before.Position == after.Position && distance == afterDistance && orientation == afterOrientation, "Cube blocked by the real solid room wall does not advance its roll animation");
		RemoveChild(fixture); fixture.QueueFree();
	}

	private async Task ValidateUnsupportedRoofBodies()
	{
		// Keep the airborne group moving, with enough distance to the core that a later
		// legitimate core-pressure pile cannot mask the initial unsupported fall.
		_map.BruiserArchetypeSettings!.MoveSpeed = 1f;
		_map.ApplyHordeArchetypeChangesLive();
		_map.DebugSpawnHorde(4, true);
		_crowd.Settings.SoftCollisionEnabled = true;
		_crowd.Settings.StackingEnabled = true;
		float roofUnderside = _map.HillMapSettings!.SummitHeight + _map.HillMapSettings.RoomWallHeight;
		List<FpsHordeEnemySnapshot> bodies = Alive();
		for (int i = 0; i < bodies.Count; i++)
		{
			Vector3 ground = new(6f + i * 0.35f, _map.HillMapSettings.SummitHeight, -6f);
			_crowd.TrySetPosition(bodies[i].Handle, ground);
			Vector3 airborne = ground; airborne.Y = roofUnderside - bodies[i].CollisionHeight - 0.05f;
			_crowd.TrySetPosition(bodies[i].Handle, airborne);
		}
		Check(Alive().All(e => e.Position.Y - _map.SampleHillSurfaceHeight(e.Position) > 3f), "Underside regression begins with unsupported same-height bodies beneath the real roof");
		await Steps(120);
		Check(Alive().All(e => e.Position.Y - _map.SampleHillSurfaceHeight(e.Position) < 0.2f), "Unsupported moving contacts fall from the roof underside instead of sustaining mutual spider-like lift");
		Check(Alive().Count == 4 && Alive().All(e => e.Position.IsFinite()), "Roof underside correction preserves living finite enemy records");
	}

	private List<FpsHordeEnemySnapshot> Alive() { List<FpsHordeEnemySnapshot> result = new(); _crowd.CopyAlive(result); return result; }
	private List<FpsEnemyCorpseSnapshot> Corpses() { List<FpsEnemyCorpseSnapshot> result = new(); _corpses.CopyActiveSnapshots(result); return result; }
	private static IEnumerable<Node> Descendants(Node root) { foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; } }
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
	private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
	private async Task Steps(int count) { for (int i = 0; i < count; i++) _map._PhysicsProcess(1d / 30d); await Frames(2); }
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; GD.Print($"PASS: {message}"); }
}
