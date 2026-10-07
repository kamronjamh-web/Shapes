using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Engine screenshots of ordinary rolling and real physics corpses; no fabricated poses.
public partial class CaptureEnemyMotion : Node
{
	private FpsVerticalSlice _map = null!;
	private readonly StringBuilder _report = new();
	public override async void _Ready()
	{
		try
		{
			if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/enemy-motion-capture/")) throw new InvalidOperationException("Motion capture requires isolated save data.");
			if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Motion capture requires a graphics display.");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyHillFixture.Configure(_map);
			AddChild(_map);
			await PhysicsFrames(4);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			FpsHordeManager crowd = _map.HordeManager!;
			FpsEnemyCorpsePool pool = _map.CorpsePool!;
			Camera3D camera = new() { Fov = 68f };
			_map.AddChild(camera);
			camera.Current = true;
			_report.AppendLine($"UTC: {DateTime.UtcNow:O}\nGodot: {Engine.GetVersionInfo()["string"]}\nCPU: {OS.GetProcessorName()}\nGPU: {RenderingServer.GetVideoAdapterName()}\nViewport: {GetViewport().GetVisibleRect().Size}\nVSync: {DisplayServer.WindowGetVsyncMode()}\nRolling pause: {crowd.Settings.RollingPauseSeconds}s; rolling duration override: {crowd.Settings.RollingDurationSeconds}s\nFrames below use live scene physics and rendering. Initial positions are on actual flat outdoor ground; no injected visual poses.\n");
			_map.DebugSpawnHorde(3, true);
			List<FpsHordeEnemySnapshot> rolling = new(); crowd.CopyAlive(rolling);
			for (int i = 0; i < rolling.Count; i++) crowd.TrySetPosition(rolling[i].Handle, new Vector3(60f + i * 3f, 0f, -76f));
			camera.Position = new Vector3(72f, 4f, -83f);
			camera.LookAt(new Vector3(62f, 0.8f, -75f));
			for (int frame = 0; frame < 12; frame++)
			{
				await PhysicsFrames(5);
				await Save($"rolling-{frame:00}.png");
				foreach (FpsHordeEnemySnapshot enemy in rolling)
				{
					crowd.TryGet(enemy.Handle, out FpsHordeEnemySnapshot current);
					crowd.TryGetRollingState(enemy.Handle, out float distance, out float pause, out Quaternion orientation);
					_report.AppendLine($"Roll frame {frame:00}, slot {enemy.Handle.Slot}, position {current.Position}, distance {distance:F4}, pause {pause:F4}s, orientation {orientation}");
				}
			}
			_map.DebugSpawnHorde(2, true);
			crowd.Settings.Enabled = false;
			pool.Settings.CorpseLifetimeSeconds = 4f;
			pool.Settings.CorpseCapacity = 8;
			List<FpsHordeEnemySnapshot> targets = new(); crowd.CopyAlive(targets);
			for (int i = 0; i < targets.Count; i++) crowd.TrySetPosition(targets[i].Handle, new Vector3(60f, 0f, -80f + i * 8f));
			camera.Position = new Vector3(95f, 15f, -95f);
			camera.LookAt(new Vector3(76f, 1f, -76f));
			var targetMap = (Dictionary<FpsHordeHandle, FpsHordeCombatEnemy>)typeof(FpsVerticalSlice).GetField("_hordeTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_map)!;
			for (int i = 0; i < targets.Count; i++)
			{
				crowd.TryGet(targets[i].Handle, out FpsHordeEnemySnapshot current);
				float overkill = i == 0 ? 0.5f : 1000f;
				typeof(FpsVerticalSlice).GetMethod("DealDirectionalDamageToEnemy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_map,
					new object?[] { targetMap[current.Handle], current.CurrentHealth + overkill, current.HeadPosition, null, false, false, Vector3.Right });
				_report.AppendLine($"Fatal hit target {current.Handle.Slot}: remaining HP {current.CurrentHealth:F2}, requested overkill {overkill:F2}, incoming direction +X.");
			}
			crowd.FlushDeathEvents();
			for (int frame = 0; frame < 12; frame++)
			{
				await PhysicsFrames(6);
				await Save($"tumbling-{frame:00}.png");
				List<FpsEnemyCorpseSnapshot> corpses = new(); pool.CopyActiveSnapshots(corpses);
				foreach (FpsEnemyCorpseSnapshot corpse in corpses)
					_report.AppendLine($"Corpse frame {frame:00}, enemy slot {corpse.EnemyHandle.Slot}, position {corpse.WorldTransform.Origin}, orientation {corpse.WorldTransform.Basis.GetRotationQuaternion()}, velocity {corpse.LinearVelocity}, initial {corpse.InitialLaunchVelocity}, fatal {corpse.FatalDamage:F2}, overkill {corpse.Overkill:F2}, remaining {corpse.RemainingLifetime:F3}s");
			}
			_report.AppendLine("This sequence records actual rolling poses and bounded single-body tumbling. It does not establish general movement quality or sustained performance.");
			File.WriteAllText(ProjectSettings.GlobalizePath("res://.tmp/enemy-motion-capture/motion-diagnostic.txt"), _report.ToString());
			GD.Print("Enemy motion captures complete.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private async Task Save(string name)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Error error = GetViewport().GetTexture().GetImage().SavePng($"res://.tmp/enemy-motion-capture/{name}");
		if (error != Error.Ok) throw new InvalidOperationException($"Screenshot {name}: {error}");
	}
	private async Task PhysicsFrames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
}
