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

public partial class ValidateHordeSpawnMovement : Node
{
    private readonly StringBuilder _report = new();
    private string _root = "";
    private FpsVerticalSlice _map = null!;
    private FpsHordeManager _crowd = null!;
    private readonly List<FpsHordeEnemySnapshot> _alive = new(50000);
    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\','/').Contains("/.tmp/horde-spawn-movement/")) throw new Exception("Isolated saves required");
            _root = ProjectSettings.GlobalizePath("res://.tmp/horde-spawn-movement/native");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            _map.ProcessMode = ProcessModeEnum.Disabled;
            _map.HordeSettings = (FpsHordeSettings)_map.HordeSettings!.Duplicate(true);
            _map.HordeSettings.GpuCrowdContactsEnabled = true;
            AddChild(_map); await Frames(2);
            _map.DebugPlayerInvulnerable = _map.DebugCoreInvulnerable = true;
            foreach (var gate in _map.DefenseGates) gate.DebugInvulnerable = true;
            _crowd = _map.HordeManager!;
            if (_map.DebugSpawnHorde(50000, true) != 50000) throw new Exception("Expected exact50000 spawn");
            _crowd.SetCombatEnabled(true);
            _crowd.CopyAlive(_alive); var initial = _alive.ToArray();
            object state = _crowd.SaveGpuProbeState();
            FieldInfo randomField = typeof(FpsHordeManager).GetField("_random", BindingFlags.Instance|BindingFlags.NonPublic)!;
            object random = randomField.GetValue(_crowd)!;
            Line($"CPU={OS.GetProcessorName()}; GPU={RenderingServer.GetVideoAdapterName()}; count={_alive.Count}; rolling={_crowd.Settings.RollingEnabled}; dt=1/30; authority sampled from CopyAlive.");
            Tick(20); _crowd.CopyAlive(_alive); var gpu20 = _alive.ToArray();
            Summarize("GPU20 from initial", initial, gpu20);
            _crowd.RestoreGpuProbeState(state); randomField.SetValue(_crowd, random);
            _crowd.Settings.GpuCrowdContactsEnabled = false;
            Tick(20); _crowd.CopyAlive(_alive); var cpu20 = _alive.ToArray();
            Summarize("CPU20 matched initial", initial, cpu20);
            Line($"CPU/GPU20 maxPositionError={cpu20.Zip(gpu20, (a,b)=>a.Position.DistanceTo(b.Position)).Max():G9}m; same saved enemy records and RNG, evolving contacts need not remain bit-identical. CPU uses current managed source; in the archived old-GPU reproduction the CPU precision fix was already present, so it is not an old-CPU comparison.");
            _crowd.RestoreGpuProbeState(state); randomField.SetValue(_crowd, random);
            _crowd.Settings.GpuCrowdContactsEnabled = true;
            Tick(120); _crowd.CopyAlive(_alive); var before = _alive.ToArray();
            Tick(30); _crowd.CopyAlive(_alive); var after = _alive.ToArray();
            Summarize("GPU150 from initial", initial, after);
            Summarize("GPU last30 rolling on", before, after);
            var stalled = before.Zip(after).Where(pair=>pair.First.Position.DistanceTo(pair.Second.Position)<.05f).Select(pair=>pair.Second).ToArray();
            Line($"Late stationary (<.05m over30ticks): {stalled.Length}; below samples include harmless true destination stops, so inspect target distances and rolling residuals.");
            Array enemies = (Array)typeof(FpsHordeManager).GetField("_enemies", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(_crowd)!;
            foreach (var enemy in stalled.Where(e=>Mathf.Max(Mathf.Abs(e.Position.X),Mathf.Abs(e.Position.Z))>100).Take(24))
            {
                _crowd.TryGetRollingState(enemy.Handle,out float distance,out float pause,out _);
                object record = enemies.GetValue(enemy.Handle.Slot)!;
                object Field(string name)=>record.GetType().GetField(name)!.GetValue(record)!;
                float width = enemy.Radius*2;
                Line($"stalled slot={enemy.Handle.Slot} pos={enemy.Position} width={width:G9} rollDistance={distance:G9} residual={width-distance:G9} pause={pause:G9} target={Field("Target")} desired={Field("DesiredVelocity")} speed={Field("Speed")} nextPause={Field("RollNextPause")}");
            }
            _crowd.Settings.RollingEnabled=false; Tick(30); _crowd.CopyAlive(_alive);
            Summarize("GPU30 rolling off from stalled state",after,_alive.ToArray());
            Line($"Previously stationary unblocked >=.05m: {stalled.Count(e=>_crowd.TryGet(e.Handle,out var now)&&now.Position.DistanceTo(e.Position)>=.05f)}/{stalled.Length}");
            _crowd.SetCombatEnabled(false); _crowd._Process(0); await Frames(3);
            var render = await _crowd.DebugReadbackGpuCrowdRenderBuffersAsync();
            int checkedRender=0; float maxRenderError=0;
            foreach(int i in Enumerable.Range(0,100).Select(i=>i*499))
            {
                var e=_alive[i]; int offset=i*80;
                Vector3 local=new(BitConverter.ToSingle(render.Cubes,offset+12),BitConverter.ToSingle(render.Cubes,offset+28),BitConverter.ToSingle(render.Cubes,offset+44));
                Vector3 expected=_crowd.GlobalTransform.AffineInverse()*_crowd.GetEnemyVisualTransform(e.Handle).Origin;
                maxRenderError=Mathf.Max(maxRenderError,local.DistanceTo(expected)); checkedRender++;
            }
            Line($"Actual GPU render buffer pose samples={checkedRender}, maxVisualOriginError={maxRenderError:G9}m; backend={_crowd.LastCrowdRenderBackend}");
            if(maxRenderError>.002f) throw new Exception("GPU render origin disagrees with authoritative pose");
            _crowd.Clear(); _map.QueueFree(); await Frames(3); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            _map=null!; _crowd=null!;
            GC.Collect(); GC.WaitForPendingFinalizers();
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await Frames(2);
            File.WriteAllText(Path.Combine(_root,"report.txt"),_report.ToString());
            if(stalled.Length!=0) throw new Exception($"Fresh50k movement regression: {stalled.Length} bodies failed to move >=.05m over late30 ticks with rolling enabled, before meaningful gate congestion.");
            GD.Print("Horde spawn movement diagnostic complete."); GetTree().Quit();
        }
        catch(Exception error) { if(_root.Length>0) File.WriteAllText(Path.Combine(_root,"report.txt"),_report.ToString()); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Tick(int count)
    {
        Stopwatch watch=Stopwatch.StartNew();
        for(int i=0;i<count;i++) _crowd.TickSimulation(1f/30);
        Line($"{count}ticks backend={_crowd.LastCrowdContactBackend}, wall={watch.Elapsed.TotalSeconds:F3}s; fallback={_crowd.GpuCrowdContactFallbackReason}");
    }
    private void Summarize(string label,FpsHordeEnemySnapshot[] before,FpsHordeEnemySnapshot[] after)
    {
        var delta=before.Zip(after,(a,b)=>new {slot=a.Handle.Slot, p=a.Position, distance=a.Position.DistanceTo(b.Position)}).ToArray();
        Line($"{label}: moved>=.05m={delta.Count(d=>d.distance>=.05f)}/{delta.Length}, unchanged<=1e-6m={delta.Count(d=>d.distance<=.000001f)}, max={delta.Max(d=>d.distance):F6}m");
        foreach(var group in delta.GroupBy(d=>$"quadrant{(d.p.X>=0?1:0)+(d.p.Z>=0?2:0)}")) Line($"  {group.Key}: moved={group.Count(d=>d.distance>=.05f)}/{group.Count()}");
        foreach(var group in delta.GroupBy(d=>d.slot/5000)) Line($"  slots{group.Key*5000}-{group.Key*5000+4999}: moved={group.Count(d=>d.distance>=.05f)}/{group.Count()}");
    }
    private void Line(string line) { _report.AppendLine(line); GD.Print(line); }
    private async Task Frames(int count) { for(int i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
}
