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

// Rendered full-scene wall timings and isolated TickSimulation diagnostics are separate samples.
public partial class CaptureHordeStress : Node
{
	private FpsVerticalSlice _map = null!;
	private readonly StringBuilder _report = new();
	public override async void _Ready()
	{
		try
		{
			if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-stress/")) throw new InvalidOperationException("Stress run requires isolated save data.");
			if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Rendered stress capture requires a graphics display.");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyHillFixture.Configure(_map);
			AddChild(_map);
			await Frames(5);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			FpsHordeManager crowd = _map.HordeManager!;
			_report.AppendLine($"UTC: {DateTime.UtcNow:O}\nGodot: {Engine.GetVersionInfo()["string"]}\nOS: {OS.GetName()}\nCPU: {OS.GetProcessorName()}\nGPU: {RenderingServer.GetVideoAdapterName()}\nDisplay backend: {DisplayServer.GetName()}\nViewport: {GetViewport().GetVisibleRect().Size}\nVSync mode: {DisplayServer.WindowGetVsyncMode()}\nCrowd capacity: {crowd.Capacity}\nSimulation Hz: {crowd.Settings.SimulationHz}\nDecision Hz: {crowd.Settings.DecisionHz}\nSeparation neighbors: {crowd.Settings.MaxSeparationNeighbors}\nSeed: {crowd.Settings.Seed}\nInvulnerability: player and core enabled; real director, collision, UI, lighting and crowd rendering active.\n");
			Camera3D camera = new() { Fov = 78f };
			_map.AddChild(camera);
			camera.GlobalPosition = new Vector3(105f, 85f, -145f);
			camera.LookAt(new Vector3(0f, 1f, -65f));
			camera.Current = true;
			foreach (int count in new[] { 100, 500, 1000, 2000 })
			{
				if (_map.DebugSpawnHorde(count, true) != count) throw new InvalidOperationException($"Could not spawn {count} stress enemies");
				await Frames(30);
				List<double> frameTimes = new();
				long previous = Stopwatch.GetTimestamp();
				for (int i = 0; i < 180; i++)
				{
					await Frames(1);
					long now = Stopwatch.GetTimestamp();
					frameTimes.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
					previous = now;
				}
				_report.AppendLine($"Rendered process-frame interval / requested {count}, final alive {crowd.ActiveCount}: {Describe(frameTimes)}. Includes pacing, simulation, draw submission and OS scheduling; not GPU execution time.");
				if (count == 2000)
				{
					await Save("crowd-2000.png");
					_map._UnhandledInput(new InputEventKey { Keycode = Key.Quoteleft, PhysicalKeycode = Key.Quoteleft, Pressed = true });
					await Save("gameplay-tuning-2000.png");
					_map._UnhandledInput(new InputEventKey { Keycode = Key.Quoteleft, PhysicalKeycode = Key.Quoteleft, Pressed = true });
				}
				// Stop automatic director ticks before timing only crowd simulation calls.
				_map.SetPhysicsProcess(false);
				_map.SetProcess(false);
				_map.DebugSpawnHorde(count, true);
				crowd.SetCombatEnabled(true);
				List<double> simulation = new();
				double dt = 1d / crowd.Settings.SimulationHz;
				for (int i = 0; i < 150; i++)
				{
					long steps = crowd.SimulationSteps;
					crowd.TickSimulation(dt);
					if (i >= 30 && crowd.SimulationSteps > steps) simulation.Add(crowd.LastSimulationMilliseconds);
				}
				_report.AppendLine($"Crowd TickSimulation-only / {count} enemies: {Describe(simulation)}. Synchronous warmed main-thread calls; excludes director weapons, rendering upload, draw, frame pacing and UI.\n");
				_map.DebugSpawnHorde(count, true);
				List<FpsHordeEnemySnapshot> staged = new();
				crowd.CopyAlive(staged);
				for (int i = 0; i < staged.Count; i++)
				{
					// Deliberately congest the doorway approach: this is a staged stress case,
					// not a claim that normal spawn positions initially overlap.
					Vector3 point = new((i % 50 - 24.5f) * 0.8f, 0f, -_map.HillMapSettings!.RoomHalfSize - 3f - (i / 50) * 0.8f);
					crowd.TrySetPosition(staged[i].Handle, point);
				}
				simulation.Clear();
				for (int i = 0; i < 150; i++)
				{
					long steps = crowd.SimulationSteps;
					crowd.TickSimulation(dt);
					if (i >= 30 && crowd.SimulationSteps > steps) simulation.Add(crowd.LastSimulationMilliseconds);
				}
				_report.AppendLine($"Staged dense north doorway TickSimulation-only / {count} enemies: {Describe(simulation)}. Initial 50-column grid at 0.8m spacing, north of room; includes deliberate crowd congestion, excludes rendering/director.\n");
				_map.SetPhysicsProcess(true);
				_map.SetProcess(true);
			}
			_report.AppendLine("These short samples characterize this scene, hardware and camera only; they do not establish sustained 2000-enemy gameplay FPS.");
			string reportPath = ProjectSettings.GlobalizePath("res://.tmp/horde-stress/stress-diagnostic.txt");
			File.WriteAllText(reportPath, _report.ToString());
			GD.Print(_report.ToString());
			GD.Print("Horde stress captures complete.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private static string Describe(List<double> samples)
	{
		if (samples.Count == 0) throw new InvalidOperationException("No timing samples collected");
		samples.Sort();
		return $"n={samples.Count}, median={samples[samples.Count / 2]:F4} ms, p95={samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(samples.Count * 0.95) - 1)]:F4} ms, max={samples[^1]:F4} ms";
	}
	private async Task Save(string name)
	{
		await Frames(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Error error = GetViewport().GetTexture().GetImage().SavePng($"res://.tmp/horde-stress/{name}");
		if (error != Error.Ok) throw new InvalidOperationException($"Screenshot {name}: {error}");
	}
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
