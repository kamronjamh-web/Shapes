using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Rendered review artifact, with a separate simulation-only timing diagnostic.
public partial class CaptureHillHorde : Node
{
	public override async void _Ready()
	{
		try { await Capture(); GetTree().Quit(); }
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private async Task Capture()
	{
		if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/hill-horde-validation/"))
			throw new InvalidOperationException("Run captures with the isolated hill validation save directory.");
		FpsVerticalSlice map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(map);
		AddChild(map);
		await Frames(4);
		map._UnhandledInput(new InputEventKey { Keycode = Key.N, Pressed = true });
		await Frames(3);
		FpsPlayerController player = map.GetNode<FpsPlayerController>("FpsPlayer");
		player.SetPhysicsProcess(false);
		map.SetProcess(false);
		map.SetPhysicsProcess(false);
		FpsHordeManager crowd = map.HordeManager!;
		List<double> timings = new();
		for (int frame = 0; frame < 840; frame++)
		{
			long steps = crowd.SimulationSteps;
			map._PhysicsProcess(1d / 60d);
			if (frame > 120 && crowd.SimulationSteps > steps) timings.Add(crowd.LastSimulationMilliseconds);
		}
		crowd.SetCombatEnabled(false);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		SubViewport viewport = new() { Size = new Vector2I(1600, 900), World3D = map.GetWorld3D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
		AddChild(viewport);
		Camera3D camera = new() { Fov = 78f };
		camera.Environment = map.GetChildren().OfType<WorldEnvironment>().First().Environment;
		viewport.AddChild(camera);
		camera.GlobalTransform = player.Camera.GlobalTransform;
		camera.LookAt(new Vector3(0f, 4f, -65f));
		camera.Current = true;
		CanvasLayer hud = map.GetNode<CanvasLayer>("HudLayer");
		hud.CustomViewport = viewport;
		await Save(viewport, "hill-first-person.png");
		hud.Hide();
		camera.Position = new Vector3(115f, 108f, -140f);
		camera.Fov = 68f;
		camera.LookAt(new Vector3(0f, 4f, -20f));
		await Save(viewport, "hill-overview.png");
		hud.Show();
		List<FpsHordeEnemySnapshot> enemies = new();
		crowd.CopyAlive(enemies);
		foreach (FpsHordeEnemySnapshot enemy in enemies.Take(8)) crowd.ApplyDamage(enemy.Handle, 100000f);
		crowd.FlushDeathEvents();
		player.Position = map.DefenseSettings!.ShopPosition;
		map._UnhandledInput(new InputEventKey { Keycode = Key.F, Pressed = true });
		map._Process(0);
		await Save(viewport, "hill-shop.png");
		timings.Sort();
		double median = timings[timings.Count / 2], p95 = timings[(int)(timings.Count * 0.95)];
		string diagnostic = $"Active enemies during timing: {enemies.Count}\nSimulation Hz: {crowd.Settings.SimulationHz}\nCrowd simulation-only median: {median:F4} ms\nCrowd simulation-only p95: {p95:F4} ms\nThis measures warm main-thread crowd steps, not rendered FPS or full combat frame time.\n";
		File.WriteAllText(ProjectSettings.GlobalizePath("res://.tmp/hill-horde-validation/simulation-diagnostic.txt"), diagnostic);
		GD.Print(diagnostic);
		hud.CustomViewport = GetViewport();
		map.QueueFree();
		await Frames(3);
		viewport.QueueFree();
		await Frames(3);
		GD.Print("Hill horde captures complete.");
	}
	private async Task Save(SubViewport viewport, string name)
	{
		await Frames(3);
		RenderingServer.ForceDraw(false);
		Error result = viewport.GetTexture().GetImage().SavePng($"res://.tmp/hill-horde-validation/{name}");
		if (result != Error.Ok) throw new InvalidOperationException($"Capture {name}: {result}");
	}
	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
