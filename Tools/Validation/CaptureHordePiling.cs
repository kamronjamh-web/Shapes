using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Forms a pile from ordinary 2000-enemy spawns using the real collision simulation.
public partial class CaptureHordePiling : Node
{
	private FpsVerticalSlice _map = null!;
	private FpsHordeManager _crowd = null!;
	private readonly StringBuilder _report = new();
	public override async void _Ready()
	{
		try
		{
			bool profilingOnly = Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--profiling-only");
			if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-piling-capture/")) throw new InvalidOperationException("Piling captures require isolated save data.");
			if (DisplayServer.GetName() == "headless" && !profilingOnly) throw new InvalidOperationException("Piling capture requires a graphics display; --profiling-only permits headless CPU diagnostics.");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyHillFixture.Configure(_map);
			AddChild(_map);
			await Frames(5);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			_crowd = _map.HordeManager!;
			Camera3D camera = new() { Fov = 78f };
			_map.AddChild(camera);
			camera.Current = true;
			_map.SetPhysicsProcess(false);
			if (_map.DebugSpawnHorde(2000, true) != 2000) throw new InvalidOperationException("Could not spawn the 2000-enemy capture crowd.");
			_report.AppendLine($"UTC: {DateTime.UtcNow:O}\nGodot: {Engine.GetVersionInfo()["string"]}\nCPU: {OS.GetProcessorName()}\nGPU: {RenderingServer.GetVideoAdapterName()}\nOS: {OS.GetName()}\nViewport: {GetViewport().GetVisibleRect().Size}\nVSync: {DisplayServer.WindowGetVsyncMode()}\nSimulation Hz: {_crowd.Settings.SimulationHz}\nDecision Hz: {_crowd.Settings.DecisionHz}\nContact iterations: {_crowd.Settings.ContactIterations}\nContact neighbor budget: {_crowd.Settings.ContactNeighborBudget}\nContact stiffness: {_crowd.Settings.ContactStiffness}\nMax pile height: {_crowd.Settings.MaxPileHeight}m\nSoft collision: {_crowd.Settings.SoftCollisionEnabled}; stacking: {_crowd.Settings.StackingEnabled}\nRoof enabled: {_map.HillMapSettings!.RoofEnabled}\nScene: unmodified North Gate terrain, walls, doorway and 2000 bounded spawns; player/core invulnerable. No relocated enemies or injected height/support callbacks.\n");
			float maxLift = 0f;
			int formationSteps = 0;
			for (int block = 0; block < 45; block++)
			{
				Advance(30); formationSteps += 30;
				maxLift = Math.Max(maxLift, Alive().Max(Lift));
				await Frames(1);
				if (maxLift > 1f && block >= 17 && Alive().Any(OnRoof)) break;
			}
			List<FpsHordeEnemySnapshot> piled = Alive();
			if (piled.Count != 2000 || !piled.Any(OnRoof)) throw new InvalidOperationException($"Natural roof traversal did not form: active {piled.Count}, historical lift {maxLift:F3}m");
			_report.AppendLine($"Natural formation: {formationSteps} manual director physics calls at 1/30s; historical max foot lift {maxLift:F3}m; currently raised above0.35m: {piled.Count(e => Lift(e) > 0.35f)}; on actual roof: {piled.Count(OnRoof)}; active {piled.Count}.\n");
			GD.Print($"Natural roof pile formed after {formationSteps} director steps; {piled.Count(OnRoof)} bodies on roof, {_crowd.ActiveCount} active.");
			if (profilingOnly)
			{
				for (int block = 0; block < 10; block++) { Advance(30); await Frames(1); }
				_report.AppendLine("Mode: headless CPU profiling only; another 300 manual director steps form a denser natural pile. No rendered frame timings or screenshots in this mode.\n");
			}
			else
			{
			camera.Position = new Vector3(100f, 80f, -135f);
			camera.LookAt(new Vector3(0f, 6f, -20f));
			await Save("pile-2000-overview.png");
			camera.Position = new Vector3(10f, 17f, -27f);
			camera.LookAt(new Vector3(0f, 11f, -_map.HillMapSettings.RoomHalfSize));
			await Save("pile-north-doorway.png");
			camera.Position = new Vector3(18f, 24f, -26f);
			camera.LookAt(new Vector3(0f, 16f, -4f));
			await Save("pile-roof-traversal.png");
			// Engine-produced frame sequence: 0.5 seconds of genuine simulation per image.
			for (int frame = 0; frame < 12; frame++)
			{
				Advance(15);
				await Save($"pile-motion-{frame:00}.png");
			}
			camera.Position = new Vector3(_map.HillMapSettings.RoomHalfSize - 2f, _map.HillMapSettings.SummitHeight + 4f, -_map.HillMapSettings.RoomHalfSize + 2f);
			camera.LookAt(_map.DefenseSettings!.CorePosition);
			await Save("pile-room-interior.png");
			camera.Position = new Vector3(18f, 24f, -26f);
			camera.LookAt(new Vector3(0f, 16f, -4f));
			_map._UnhandledInput(new InputEventKey { Keycode = Key.Quoteleft, Pressed = true });
			TabContainer contacts = Descendants(_map).OfType<TabContainer>().First(t => Enumerable.Range(0, t.GetTabCount()).Any(i => t.GetTabTitle(i) == "Crowd contact"));
			contacts.CurrentTab = Enumerable.Range(0, contacts.GetTabCount()).First(i => contacts.GetTabTitle(i) == "Crowd contact");
			// Replace the manually fast-forwarded frame metrics with real frame intervals.
			_map.SetPhysicsProcess(true);
			await Frames(270);
			await Save("pile-gameplay-tuning.png");
			_map._UnhandledInput(new InputEventKey { Keycode = Key.Quoteleft, Pressed = true });
			// Actual rendered frame intervals: no fixed-FPS runner cap.
			_map.SetPhysicsProcess(true);
			await Frames(30);
			List<double> rendered = new();
			long previous = Stopwatch.GetTimestamp();
			for (int frame = 0; frame < 180; frame++)
			{
				await Frames(1);
				long now = Stopwatch.GetTimestamp();
				rendered.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
				previous = now;
			}
			_report.AppendLine($"Rendered piled scene ProcessFrame intervals: {Describe(rendered)}; active {_crowd.ActiveCount}. Includes frame pacing, OS scheduling, real scene simulation and rendering submission; not GPU execution time.\n");
			}
			_map.SetPhysicsProcess(false);
			List<double> simulation = new();
			List<double> movement = new(), contact = new(), attack = new();
			for (int step = 0; step < 150; step++)
			{
				long before = _crowd.SimulationSteps;
				_crowd.TickSimulation(1d / _crowd.Settings.SimulationHz);
				if (step >= 30 && _crowd.SimulationSteps > before)
				{
					simulation.Add(_crowd.LastSimulationMilliseconds);
					movement.Add(_crowd.LastMovementMilliseconds);
					contact.Add(_crowd.LastContactMilliseconds);
					attack.Add(_crowd.LastAttackMilliseconds);
				}
			}
			_report.AppendLine($"Crowd TickSimulation-only in natural pile: {Describe(simulation)}; active {_crowd.ActiveCount}. Warmed synchronous main-thread calls include crowd contacts and attack callbacks; exclude director weapons/projectile integration, render upload and frame pacing.\n");
			_report.AppendLine($"Movement phase: {Describe(movement)}\nContact phase: {Describe(contact)}\nAttack phase: {Describe(attack)}\nPhase sample medians are independent and do not necessarily sum to the median total.\n");
			_report.AppendLine(profilingOnly
				? "Headless CPU samples characterize this machine and natural pile state; they do not measure rendering or establish sustained gameplay FPS."
				: "Short camera-specific samples characterize this machine and scene; they do not establish sustained 2000-enemy gameplay FPS. Motion PNGs are engine screenshots at 0.5 simulated-second intervals, not fabricated pile poses.");
			File.WriteAllText(ProjectSettings.GlobalizePath(profilingOnly ? "res://.tmp/horde-piling-capture/piling-profile-diagnostic.txt" : "res://.tmp/horde-piling-capture/piling-diagnostic.txt"), _report.ToString());
			GD.Print(_report.ToString());
			GD.Print(profilingOnly ? "Horde piling profiling complete." : "Horde piling captures complete.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private void Advance(int count) { for (int i = 0; i < count; i++) _map._PhysicsProcess(1d / 30d); }
	private float Lift(FpsHordeEnemySnapshot enemy) => enemy.Position.Y - _map.SampleHillSurfaceHeight(enemy.Position);
	private bool OnRoof(FpsHordeEnemySnapshot enemy)
	{
		FpsHillMapSettings hill = _map.HillMapSettings!;
		float top = hill.SummitHeight + Mathf.Max(4f, hill.RoomWallHeight) + 0.5f;
		return Mathf.Abs(enemy.Position.X) < hill.RoomHalfSize - 0.5f && Mathf.Abs(enemy.Position.Z) < hill.RoomHalfSize - 0.5f && enemy.Position.Y >= top - 0.05f;
	}
	private List<FpsHordeEnemySnapshot> Alive() { List<FpsHordeEnemySnapshot> result = new(); _crowd.CopyAlive(result); return result; }
	private static IEnumerable<Node> Descendants(Node root)
	{
		foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
	}
	private static string Describe(List<double> values)
	{
		if (values.Count == 0) throw new InvalidOperationException("No timing samples collected");
		values.Sort();
		return $"n={values.Count}, median={values[values.Count / 2]:F4}ms, p95={values[Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * 0.95) - 1)]:F4}ms, max={values[^1]:F4}ms";
	}
	private async Task Save(string name)
	{
		await Frames(2);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Error error = GetViewport().GetTexture().GetImage().SavePng($"res://.tmp/horde-piling-capture/{name}");
		if (error != Error.Ok) throw new InvalidOperationException($"Screenshot {name}: {error}");
	}
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
