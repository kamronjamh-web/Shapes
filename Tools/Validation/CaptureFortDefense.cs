using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class CaptureFortDefense : Node
{
	private FpsVerticalSlice _map = null!;
	private Camera3D _camera = null!;
	private readonly StringBuilder _report = new();
	public override async void _Ready()
	{
		try
		{
			if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/fort-defense-capture/")) throw new InvalidOperationException("Fort capture requires isolated save data.");
			if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Fort capture requires a rendered display.");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyExpandedFortFixture.Configure(_map);
			AddChild(_map); await Frames(4);
			_map.SetPhysicsProcess(false);
			_map.DebugPlayerInvulnerable = true; _map.DebugCoreInvulnerable = true;
			_camera = new Camera3D { Fov = 68f, Far = 4000f }; _map.AddChild(_camera); _camera.Current = true;
			_report.AppendLine($"UTC: {DateTime.UtcNow:O}\nGodot: {Engine.GetVersionInfo()["string"]}\nCPU: {OS.GetProcessorName()}\nGPU: {RenderingServer.GetVideoAdapterName()}\nViewport: {GetViewport().GetVisibleRect().Size}\nVSync: {DisplayServer.WindowGetVsyncMode()}\nMap: {_map.HillMapSettings!.PlayableSize}m square; inner clear base: {_map.HillMapSettings.RoomHalfSize * 2f}m; wall: {_map.HillMapSettings.RoomWallHeight}m; gate: {_map.HillMapSettings.DoorWidth}m\nScreenshots use actual scene geometry and ordinary population. Manual simulation staging is not a frame-rate benchmark.\n");
			_map.DebugClearHorde();
			await View("fort-overview.png", new Vector3(210f, 160f, 240f), new Vector3(0f, 8f, 0f));
			await View("north-gate.png", new Vector3(15f, 18f, -130f), new Vector3(0f, 12f, -101f));
			await View("access-ramp.png", new Vector3(22f, 16f, -75f), new Vector3(6.786f, 11.5f, -94f));
			await View("wall-walk.png", new Vector3(18f, 15.7f, -100.5f), new Vector3(-60f, 14.8f, -100.5f));
			int spawned = _map.DebugSpawnHorde(2000, true);
			_report.AppendLine($"Initial population: {spawned}; four-sector gate health: {GateHealth()}");
			await View("mile-map-2000.png", new Vector3(950f, 1100f, 1050f), new Vector3(0f, 0f, 0f));
			await View("four-approaches-2000.png", new Vector3(300f, 310f, 340f), new Vector3(0f, 8f, 0f));
			int formationSteps = 0;
			for (; formationSteps < 3600; formationSteps++)
			{
				_map._PhysicsProcess(1.0 / 30.0);
				if (formationSteps % 60 == 0) await Frames(1);
				bool arrived = _map.DefenseGates.Any(g => g.CurrentHealth < g.MaxHealth);
				if (arrived) { formationSteps++; break; }
			}
			_report.AppendLine($"Natural first gate damage staging: {formationSteps / 30.0:F2} simulated seconds, arrival={_map.DefenseGates.Any(g => g.CurrentHealth < g.MaxHealth)}; {_map.ActiveEnemyCount} enemies; {GateHealth()}.");
			_report.AppendLine($"Pile at first arrival: {PileState()}");
			await View("north-pressure.png", new Vector3(25f, 30f, -142f), new Vector3(0f, 11f, -101f));
			await View("fort-assault.png", new Vector3(175f, 135f, 190f), new Vector3(0f, 8f, 0f));
			for (int frame = 0; frame < 8; frame++)
			{
				for (int step = 0; step < 15; step++) _map._PhysicsProcess(1.0 / 30.0);
				await View($"assault-{frame:00}.png", new Vector3(25f, 25f, -132f), new Vector3(0f, 11f, -101f));
				_report.AppendLine($"Motion frame {frame}: {GateHealth()}");
			}
			_report.AppendLine($"Pile after sequence: {PileState()}");
			List<double> simulation = new();
			for (int sample = 0; sample < 120; sample++)
			{
				_map.HordeManager!.TickSimulation(1f / 30f);
				simulation.Add(_map.HordeManager.LastSimulationMilliseconds);
				if (sample % 30 == 0) await Frames(1);
			}
			_report.AppendLine($"Crowd simulation-only CPU, staged assault, 30Hz, population {_map.ActiveEnemyCount}, contacts {_map.HordeSettings!.ContactIterations}, cell {_map.HordeSettings.GridCellSize}m: {Describe(simulation)}. Excludes rendering and other scene gameplay work.");
			_camera.Position = new Vector3(300f, 310f, 340f); _camera.LookAt(new Vector3(0f, 8f, 0f));
			_map.SetPhysicsProcess(true);
			List<double> rendered = new(); Stopwatch frames = Stopwatch.StartNew();
			StringBuilder frameTrace = new("frame,wall_ms,simulation_steps,last_simulation_ms,movement_ms,contact_ms,attack_ms,gc0,gc1,gc2\n");
			long previousSteps = _map.HordeManager!.SimulationSteps;
			for (int sample = 0; sample < 270; sample++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				double elapsed = frames.Elapsed.TotalMilliseconds; frames.Restart();
				if (sample >= 90) rendered.Add(elapsed);
				FpsHordeManager crowd = _map.HordeManager!;
				frameTrace.AppendLine(FormattableString.Invariant($"{sample},{elapsed:F4},{crowd.SimulationSteps - previousSteps},{crowd.LastSimulationMilliseconds:F4},{crowd.LastMovementMilliseconds:F4},{crowd.LastContactMilliseconds:F4},{crowd.LastAttackMilliseconds:F4},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)}"));
				previousSteps = crowd.SimulationSteps;
			}
			File.WriteAllText(Path.Combine(Output(), "frame-diagnostic.csv"), frameTrace.ToString());
			_report.AppendLine($"Rendered live full-scene ProcessFrame wall intervals, four-approach camera, native physics, population {_map.ActiveEnemyCount}: {Describe(rendered)}. No fixed FPS flag; includes render, gameplay, physics catchup and host scheduling. This is a bounded diagnostic, not a performance guarantee.");
			typeof(FpsVerticalSlice).GetMethod("SetGameplayTuningVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_map, new object[] { true });
			TabContainer tabs = Descendants(_map).OfType<TabContainer>().Single(t => Enumerable.Range(0, t.GetTabCount()).Any(index => t.GetTabTitle(index) == "Fort"));
			for (int tab = 0; tab < tabs.GetTabCount(); tab++) if (tabs.GetTabTitle(tab) == "Fort") tabs.CurrentTab = tab;
			await Save("fort-tuning-menu.png");
			typeof(FpsVerticalSlice).GetMethod("SetGameplayTuningVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_map, new object[] { false });
			FpsPlayerController player = (FpsPlayerController)typeof(FpsVerticalSlice).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_map)!;
			player.Position = new Vector3(6.786f, 14.196f, -107.5f); player.Velocity = Vector3.Zero;
			player.Camera.Current = true;
			await Frames(30);
			player.Camera.LookAt(new Vector3(0f, 9f, -111f));
			await Save("player-firing-platform.png");
			using PhysicsRayQueryParameters3D firingRay = PhysicsRayQueryParameters3D.Create(player.Camera.GlobalPosition, new Vector3(0f, 9f, -111f), 1);
			firingRay.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
			using var firingHit = _map.GetWorld3D().DirectSpaceState.IntersectRay(firingRay);
			_report.AppendLine($"Actual player firing-platform body origin: {player.Position}; camera: {player.Camera.GlobalPosition}; floor={player.IsOnFloor()}; downward sightline clear={firingHit.Count == 0}.");
			File.WriteAllText(Path.Combine(Output(), "fort-diagnostic.txt"), _report.ToString());
			GD.Print("Fort defense captures complete."); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private string PileState()
	{
		List<FpsHordeEnemySnapshot> enemies = new(); _map.HordeManager!.CopyAlive(enemies);
		return $"HighestWorldFeetY={enemies.Max(e => e.Position.Y):F4}m, StackedEnemyCount={enemies.Count(e => e.Position.Y - _map.SampleHillSurfaceHeight(e.Position) > 0.25f)}, MaxLiftAboveTerrain={enemies.Max(e => e.Position.Y - _map.SampleHillSurfaceHeight(e.Position)):F4}m (elevated count includes bodies supported by fort geometry).";
	}
	private string GateHealth()
	{
		List<string> result = new(); foreach (FpsDefenseGate gate in _map.DefenseGates) result.Add($"gate{gate.SideIndex}:{gate.CurrentHealth:F1}/{gate.MaxHealth:F1}"); return string.Join(", ", result);
	}
	private async Task View(string name, Vector3 position, Vector3 target)
	{
		_camera.Position = position; _camera.LookAt(target);
		await Save(name);
	}
	private async Task Save(string name)
	{
		await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Error error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Output(), name));
		if (error != Error.Ok) throw new InvalidOperationException($"Cannot save {name}: {error}");
	}
	private static IEnumerable<Node> Descendants(Node node) { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; } }
	private static string Describe(List<double> values) { values.Sort(); return $"n={values.Count}, median={values[values.Count / 2]:F4}ms, p95={values[(int)Math.Ceiling(values.Count * 0.95) - 1]:F4}ms, max={values[^1]:F4}ms"; }
	private static string Output() { string path = ProjectSettings.GlobalizePath("res://.tmp/fort-defense-capture"); Directory.CreateDirectory(path); return path; }
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
}
