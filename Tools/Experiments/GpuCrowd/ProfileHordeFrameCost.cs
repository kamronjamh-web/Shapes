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

// Diagnostic ablations only; the production simulation/render algorithms are unchanged.
public partial class ProfileHordeFrameCost : Node
{
    private FpsVerticalSlice _map = null!;
    private FpsHordeManager _crowd = null!;
    private MultiMeshInstance3D[] _drawNodes = Array.Empty<MultiMeshInstance3D>();
    private readonly StringBuilder _report = new();
    private readonly StringBuilder _csv = new("scenario,mode,sample,frame_ms,publication_ms,simulation_ms,movement_ms,contacts_rolling_ms,attacks_ms,steps,viewport_gpu_ms,viewport_render_cpu_ms,render_setup_cpu_ms,visible_primitives,shadow_primitives,visible_draws,count,render_backend,contact_backend,async_pose_gpu_ms,pose_gpu_completed_sample_id,movement_mode\n");
    private readonly StringBuilder _componentCsv = new("scenario,sample,count,movement_mode,render_backend,simulation_ms,movement_ms,contacts_ms,attacks_ms,publication_cpu_ms,body_pack_ms,drive_pack_ms,upload_ms,dispatch_wait_ms,readback_ms,validate_ms,apply_ms,world_prepare_ms,boundary_total_ms,contact_residual_ms,kernel_gpu_ms\n");
    private bool _publish;
    private double _publishMs, _simulationMs, _movementMs, _contactsMs, _attacksMs;
    private long _lastSteps, _frameSteps, _maxPhysicsCallSteps;
    private Rid _viewport;
    private string _directory = string.Empty;
    private int _count;
    private bool _cpuRendering;
    private bool _measurePoseGpu;
    private bool _cpuMovement;
    private bool _componentsOnly;
    private string MovementMode => _cpuMovement ? "CPU-drive+GPU-world" : "GPU-drive+GPU-world";
    private bool _canSave;
    private string ExpectedRenderBackend => _cpuRendering ? "CPU" : "GPU";
    private static readonly FieldInfo Accumulator = typeof(FpsHordeManager).GetField("_accumulator", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private const int Warmups = 24, Samples = 90;
    private const double MinimumWarmupSeconds = .5, MinimumMeasureSeconds = 3;

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-frame-profile/"))
                throw new InvalidOperationException("Profile must use isolated save data.");
            if (DisplayServer.GetName() == "headless" || RenderingServer.GetCurrentRenderingDriverName() != "vulkan")
                throw new InvalidOperationException("Profile requires native Vulkan rendering.");
            string label = System.Environment.GetEnvironmentVariable("SHAPES_FRAME_PROFILE_LABEL") ?? "latest";
            if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new InvalidOperationException("Invalid run label.");
            _count = int.Parse(Option("count", "SHAPES_FRAME_PROFILE_COUNT", "10000"));
            if (_count != 10000 && _count != 25000 && _count != 50000 && _count != 100000)
                throw new InvalidOperationException("Count must be 10000, 25000, 50000 or 100000.");
            string render = Option("render", "SHAPES_FRAME_PROFILE_RENDER", "gpu");
            if (render != "cpu" && render != "gpu") throw new InvalidOperationException("Render must be cpu or gpu.");
            _cpuRendering = render == "cpu";
            if (!bool.TryParse(Option("pose-timing", "SHAPES_FRAME_PROFILE_GPU_TIMING", "false"), out _measurePoseGpu))
                throw new InvalidOperationException("Pose-timing must be true or false.");
            if (!bool.TryParse(Option("cpu-movement", "SHAPES_FRAME_PROFILE_CPU_MOVEMENT", "false"), out _cpuMovement))
                throw new InvalidOperationException("Cpu-movement must be true or false.");
            if (!bool.TryParse(Option("components-only", "SHAPES_FRAME_PROFILE_COMPONENTS_ONLY", "false"), out _componentsOnly))
                throw new InvalidOperationException("Components-only must be true or false.");
            _directory = ProjectSettings.GlobalizePath($"res://.tmp/horde-frame-profile/{label}");
            Directory.CreateDirectory(_directory);
            if (File.Exists(Path.Combine(_directory, "report.txt"))) throw new InvalidOperationException("Run label already contains a report; choose a new label to preserve previous measurements.");
            _canSave = true;
            ProcessPriority = ProcessPhysicsPriority = 1000;
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            _map.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(_map);
            await Frames(3);
            _map.DebugPlayerInvulnerable = _map.DebugCoreInvulnerable = true;
            foreach (FpsDefenseGate gate in _map.DefenseGates) gate.DebugInvulnerable = true;
            _crowd = _map.HordeManager!;
            _crowd.Settings.GpuCrowdRenderingEnabled = !_cpuRendering;
            _crowd.MeasureGpuRenderTime = _measurePoseGpu && !_cpuRendering;
            _crowd.GpuCrowdFullDriveEnabled = !_cpuMovement;
            _crowd.MeasureGpuBoundaryTime = _componentsOnly;
            _crowd.MeasureGpuKernelTime = _componentsOnly && _measurePoseGpu;
            _crowd.SetProcess(false); // Same production _Process is timed once from this diagnostic.
            _crowd.SetCombatEnabled(true);
            _drawNodes = Descendants(_crowd).OfType<MultiMeshInstance3D>().ToArray();
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _viewport = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
            bool debug = typeof(ProfileHordeFrameCost).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;
            bool apiDebug = typeof(Vector3).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;
            Line($"{_count:N0} enemy frame profile; requested render={ExpectedRenderBackend}; movement={MovementMode}; {DateTime.UtcNow:O}; CPU={OS.GetProcessorName()}; GPU={RenderingServer.GetVideoAdapterName()}; Godot={Engine.GetVersionInfo()["string"]}");
            Line($"Role=cubes; camera=overview (140,150,-390) toward (0,5,-140); viewport={GetViewport().GetVisibleRect().Size}; VSync={DisplayServer.WindowGetVsyncMode()}; maxFPS={Engine.MaxFps}; game/API JIT optimizer disabled={debug}/{apiDebug}; allocated slots={_crowd.Capacity}; simulationHz={_crowd.Settings.SimulationHz}; frame warmup >= {Warmups} frames AND {MinimumWarmupSeconds}s; measurement >= {Samples} frames AND {MinimumMeasureSeconds}s; fixed-snapshot component warmups/samples={Warmups}/{Samples}");
            Line("Production publication is timed once each process frame after the director. GPU publication time is CPU packing/enqueue wall time, not GPU execution time. Viewport GPU timing measures viewport drawing; it excludes local-device contacts and may exclude the main-device crowd compute prepass. Frame throughput includes the complete active workload. Normal physics stays active for live modes. No firing, player/core/gates invulnerable; staged gates are diagnostic congestion, not natural arrival. Conditions restore identical initial snapshots; populations advancing in time can diverge. No Release or sustained-combat claim. Available-device measurements do not establish 100k/1080p/60 FPS on GTX 1060 or RX 580 class hardware.");
            Line($"Pose GPU timestamps enabled={_crowd.MeasureGpuRenderTime}. When enabled, the latest completed pose dispatch interval is collected once per distinct sample ID; intervals arrive asynchronously from prior frames and are not paired with the CSV row's viewport sample. Intermediate intervals can be skipped when several finish between observations. Never add pose and viewport timings: scopes can overlap. Diagnostic marker/collection overhead is included in this run's workload; default profiling leaves markers disabled.");
            Line("Movement control preserves GPU world constraints and contacts; it disables only GPU full drive. Full drive includes objective/route steering, gait, gravity and rolling in the contact dispatch. Movement phase measures CPU preparation (or CPU drive in the control); contact phase includes the synchronized GPU drive/world/contact dispatch and transfers, plus CPU rolling in the control. Compare total simulation/frame cost rather than either phase alone.");
            if (_componentsOnly)
            {
                Line("Components-only diagnostic: two staged populations, scene captures and fixed-snapshot steps; live frame ablations are skipped. Snapshot restoration and captures are excluded from measured boundaries. Publication queues every step and the diagnostic yields a rendered frame every eight steps, matching the full profiler's component protocol; this is not a real-time throughput or FPS estimate.");
                Line("GPU boundary durations are CPU wall intervals. Dispatch/wait includes command recording, submission and GPU completion wait; it is not a pure GPU execution timestamp. Boundary total sums disjoint body+drive packing, upload, dispatch/wait, readback, validation and application. World preparation is nested in movement preparation and excluded from boundary total. Contact residual includes work outside these boundaries and CPU rolling in the movement control. Instrumentation overhead is diagnostic-only.");
                Line($"Local kernel GPU timestamps enabled={_crowd.MeasureGpuKernelTime}; optional kernel GPU execution is nested within dispatch/wait and excluded from summed boundary. Marker inspection overhead remains in the contact residual.");
            }
            if (GetViewport().GetVisibleRect().Size != new Vector2(1920f, 1080f)) throw new InvalidOperationException("Profile requires a 1920x1080 viewport.");
            Camera3D overview = new() { Fov = 78f, Far = 2414f };
            AddChild(overview);
            overview.GlobalPosition = new Vector3(140f, 150f, -390f);
            overview.LookAt(new Vector3(0f, 5f, -140f));
            overview.Current = true;

            foreach (string scenario in new[] { "approach", "gate-pressure" })
            {
                _map.ProcessMode = ProcessModeEnum.Disabled;
                _publish = false;
                if (_map.DebugSpawnHorde(_count, true) != _count) throw new InvalidOperationException("Exact population unavailable.");
                _crowd.SetCombatEnabled(true);
                if (scenario == "gate-pressure") StageGates();
                for (int i = 0; i < (scenario == "gate-pressure" ? 90 : 30); i++)
                {
                    _crowd.TickSimulation(1f / 30f);
                    if (i % 16 == 0) await Frames(1);
                }
                if (_crowd.LastCrowdContactBackend != "GPU") throw new InvalidOperationException(_crowd.GpuCrowdContactFallbackReason);
                object snapshot = _crowd.SaveGpuProbeState();
                _crowd._Process(0);
                await Frames(3);
                AssertBackends();
                using (Image capture = GetViewport().GetTexture().GetImage())
                {
                    Error saved = capture.SavePng(Path.Combine(_directory, scenario + ".png"));
                    if (saved != Error.Ok) throw new InvalidOperationException($"Could not save {scenario} scene capture: {saved}");
                }
                Line($"{scenario}: live={_crowd.ActiveCount}; rendered={_crowd.RenderedInstanceCount}; render={_crowd.LastCrowdRenderBackend}; contacts={_crowd.LastCrowdContactBackend}; render fallback='{_crowd.GpuCrowdRenderFallbackReason}'; GPU body buffer={_crowd.GpuContactBufferCapacity}; raised={_crowd.StackedEnemyCount}.");
                await ProfileComponents(scenario, snapshot);
                Save();
                if (_componentsOnly) continue;
                foreach (var mode in new[]
                {
                    (Name: "live", Sim: true, Publish: true, Draw: true),
                    (Name: "live-hidden-crowd", Sim: true, Publish: true, Draw: false),
                    (Name: "live-frozen-publication", Sim: true, Publish: false, Draw: true),
                    (Name: "frozen-simulation", Sim: false, Publish: true, Draw: true),
                    (Name: "draw-only", Sim: false, Publish: false, Draw: true),
                    (Name: "live-repeat", Sim: true, Publish: true, Draw: true),
                })
                {
                    _map.ProcessMode = ProcessModeEnum.Disabled;
                    _publish = false;
                    _crowd.RestoreGpuProbeState(snapshot);
                    Accumulator.SetValue(_crowd, 0d);
                    foreach (var draw in _drawNodes) draw.Visible = mode.Draw;
                    _crowd._Process(0);
                    _lastSteps = _crowd.SimulationSteps;
                    ResetCounters();
                    _publish = mode.Publish;
                    _map.ProcessMode = mode.Sim ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
                    await MeasureFrames(scenario, mode.Name);
                    if (_crowd.ActiveCount != _count || _crowd.RenderedInstanceCount != _count)
                        throw new InvalidOperationException("Profile changed population.");
                    AssertBackends();
                }
            }
            _publish = false;
            _map.ProcessMode = ProcessModeEnum.Disabled;
            SetPhysicsProcess(false);
            if (_report.Length > 0) Save();
            _map.QueueFree();
            await Frames(3);
            GD.Print("Horde frame profile complete.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Line($"FAILED: {error}");
            Save();
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        _publishMs = 0;
        if (!_publish || _crowd == null) return;
        long start = Stopwatch.GetTimestamp();
        _crowd._Process(delta);
        _publishMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_crowd == null || !GodotObject.IsInstanceValid(_map) || _map.ProcessMode == ProcessModeEnum.Disabled) return;
        long steps = _crowd.SimulationSteps - _lastSteps;
        _maxPhysicsCallSteps = Math.Max(_maxPhysicsCallSteps, steps);
        _simulationMs += _crowd.LastSimulationMilliseconds;
        if (steps > 0)
        {
            _frameSteps += steps;
            _movementMs += _crowd.LastMovementMilliseconds;
            _contactsMs += _crowd.LastContactMilliseconds;
            _attacksMs += _crowd.LastAttackMilliseconds;
        }
        _lastSteps = _crowd.SimulationSteps;
    }

    private async Task MeasureFrames(string scenario, string mode)
    {
        long warmupStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < Warmups || Stopwatch.GetElapsedTime(warmupStart).TotalSeconds < MinimumWarmupSeconds; i++)
            await Frames(1);
        List<double> frames = new(), publication = new(), simulation = new(), gpu = new(), renderCpu = new(), setupCpu = new(), steps = new();
        List<double> primitives = new(), shadowPrimitives = new(), draws = new();
        List<double> poseGpu = new();
        long poseSample = _crowd.GetGpuRenderTimingSnapshot().Samples;
        long measurementStart = Stopwatch.GetTimestamp(), previous = measurementStart;
        _maxPhysicsCallSteps = 0;
        ResetCounters();
        for (int i = 0; i < Samples || Stopwatch.GetElapsedTime(measurementStart).TotalSeconds < MinimumMeasureSeconds; i++)
        {
            await Frames(1);
            AssertBackends();
            long now = Stopwatch.GetTimestamp();
            double frame = Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;
            previous = now;
            double g = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
            double c = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
            double setup = RenderingServer.GetFrameSetupTimeCpu();
            int tris = RenderingServer.ViewportGetRenderInfo(_viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
            int shadows = RenderingServer.ViewportGetRenderInfo(_viewport, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
            int dc = RenderingServer.ViewportGetRenderInfo(_viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
            frames.Add(frame); publication.Add(_publishMs); simulation.Add(_simulationMs); gpu.Add(g); renderCpu.Add(c); setupCpu.Add(setup); steps.Add(_frameSteps);
            primitives.Add(tris); shadowPrimitives.Add(shadows); draws.Add(dc);
            var completedPose = _crowd.GetGpuRenderTimingSnapshot();
            string poseValue = string.Empty, poseId = string.Empty;
            if (_crowd.MeasureGpuRenderTime && completedPose.Samples > poseSample)
            {
                poseSample = completedPose.Samples;
                if (!double.IsFinite(completedPose.Milliseconds) || completedPose.Milliseconds <= 0d)
                    throw new InvalidOperationException("Completed pose GPU timing must be finite and positive.");
                poseGpu.Add(completedPose.Milliseconds);
                poseValue = completedPose.Milliseconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                poseId = completedPose.Samples.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            _csv.AppendLine(FormattableString.Invariant($"{scenario},{mode},{i},{frame:F6},{_publishMs:F6},{_simulationMs:F6},{_movementMs:F6},{_contactsMs:F6},{_attacksMs:F6},{_frameSteps},{g:F6},{c:F6},{setup:F6},{tris},{shadows},{dc},{_count},{_crowd.LastCrowdRenderBackend},{_crowd.LastCrowdContactBackend},{poseValue},{poseId},{MovementMode}"));
            ResetCounters();
        }
        double duration = Stopwatch.GetElapsedTime(measurementStart).TotalSeconds;
        Line($"{scenario}/{mode}: count={_count}; renderer={_crowd.LastCrowdRenderBackend}; contacts={_crowd.LastCrowdContactBackend}; samples={frames.Count}; elapsed={duration:F3}s; simulation steps/sec={steps.Sum() / duration:F3}; frame {Describe(frames)}, mean={frames.Average():F3} ms ({1000 / frames.Average():F2} FPS throughput); publication CPU wall {Describe(publication)}; simulation/frame mean={simulation.Average():F3} ms; steps/frame={steps.Average():F3}; max steps/physics call={_maxPhysicsCallSteps}; viewportGPU {Describe(gpu)}; viewportCPU {Describe(renderCpu)}; setupCPU {Describe(setupCpu)}; primitives visible/shadow med={Median(primitives)}/{Median(shadowPrimitives)}; visible drawcalls med={Median(draws)}.");
        if (_maxPhysicsCallSteps > 1) Line("Phase/frame CSV contains only the final step per physics call; use total simulation time and fixed-snapshot phases for this configuration.");
        if (_crowd.MeasureGpuRenderTime)
        {
            if (_publish && poseGpu.Count == 0) throw new InvalidOperationException("Pose GPU timestamps enabled but no completed intervals were observed during publication.");
            Line($"{scenario}/{mode}: asynchronous completed pose GPU intervals: observed distinct samples={poseGpu.Count}; {(poseGpu.Count > 0 ? Describe(poseGpu) : "no new completed intervals (expected when publication is frozen)")}. Separate scope; not added to viewport timing.");
        }
        Save();
    }

    private async Task ProfileComponents(string scenario, object snapshot)
    {
        List<double> total = new(), movement = new(), contacts = new(), attacks = new(), publication = new();
        List<double> bodyPack = new(), drivePack = new(), upload = new(), dispatchWait = new(), readback = new();
        List<double> validate = new(), apply = new(), worldPrepare = new(), boundaryTotal = new(), residual = new();
        List<double> kernelGpu = new();
        for (int i = 0; i < Warmups + Samples; i++)
        {
            _crowd.RestoreGpuProbeState(snapshot);
            Accumulator.SetValue(_crowd, 0d);
            long before = _crowd.SimulationSteps;
            _crowd.TickSimulation(1f / 30f);
            if (_crowd.SimulationSteps != before + 1 || _crowd.LastCrowdContactBackend != "GPU")
                throw new InvalidOperationException("Fixed snapshot must advance one GPU-backed step.");
            AssertDriveBackend();
            double t = _crowd.LastSimulationMilliseconds, m = _crowd.LastMovementMilliseconds, c = _crowd.LastContactMilliseconds, a = _crowd.LastAttackMilliseconds;
            var boundary = _crowd.LastGpuBoundaryTiming;
            if (_componentsOnly && !boundary.Succeeded) throw new InvalidOperationException("Fixed snapshot has no successful GPU boundary timing sample.");
            long start = Stopwatch.GetTimestamp();
            _crowd._Process(0);
            double p = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            AssertBackends();
            if (i >= Warmups)
            {
                total.Add(t); movement.Add(m); contacts.Add(c); attacks.Add(a); publication.Add(p);
                string boundaryValues = ",,,,,,,,,,";
                if (_componentsOnly)
                {
                    bodyPack.Add(boundary.BodyPackMs); drivePack.Add(boundary.DrivePackMs); upload.Add(boundary.UploadMs);
                    dispatchWait.Add(boundary.DispatchAndWaitMs); readback.Add(boundary.ReadbackMs);
                    validate.Add(boundary.ValidateMs); apply.Add(boundary.ApplyMs); worldPrepare.Add(boundary.WorldPrepareMs);
                    boundaryTotal.Add(boundary.BoundaryTotalMs); residual.Add(c - boundary.BoundaryTotalMs);
                    string kernelValue = string.Empty;
                    if (_crowd.MeasureGpuKernelTime)
                    {
                        if (!double.IsFinite(boundary.GpuExecutionMs) || boundary.GpuExecutionMs <= 0d)
                            throw new InvalidOperationException("Measured local kernel GPU interval must be finite and positive.");
                        kernelGpu.Add(boundary.GpuExecutionMs);
                        kernelValue = boundary.GpuExecutionMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    boundaryValues = FormattableString.Invariant($"{boundary.BodyPackMs:F6},{boundary.DrivePackMs:F6},{boundary.UploadMs:F6},{boundary.DispatchAndWaitMs:F6},{boundary.ReadbackMs:F6},{boundary.ValidateMs:F6},{boundary.ApplyMs:F6},{boundary.WorldPrepareMs:F6},{boundary.BoundaryTotalMs:F6},{c - boundary.BoundaryTotalMs:F6},{kernelValue}");
                }
                _componentCsv.AppendLine(FormattableString.Invariant($"{scenario},{i - Warmups},{_count},{MovementMode},{_crowd.LastCrowdRenderBackend},{t:F6},{m:F6},{c:F6},{a:F6},{p:F6},{boundaryValues}"));
            }
            if (i % 8 == 0) await Frames(1);
        }
        Line($"{scenario}/fixed-snapshot components: movement={MovementMode}; simulation/step {Describe(total)}; movement preparation/CPU drive {Describe(movement)}; GPU drive+world+contacts/control rolling {Describe(contacts)}; attacks {Describe(attacks)}; {ExpectedRenderBackend} publication CPU wall {Describe(publication)}. Snapshot restoration excluded; timings are sequential CPU wall times, not frame FPS or asynchronous GPU pose execution.");
        if (_componentsOnly)
        {
            Line($"{scenario}/GPU boundary ({Samples} samples): body packing {DescribeMean(bodyPack)}; drive packing {DescribeMean(drivePack)}; upload {DescribeMean(upload)}; dispatch/wait {DescribeMean(dispatchWait)}; readback {DescribeMean(readback)}; validation {DescribeMean(validate)}; application {DescribeMean(apply)}.");
            Line($"{scenario}/GPU boundary reconciliation: disjoint total {DescribeMean(boundaryTotal)}; contact phase mean={contacts.Average():F3} ms; contact residual {DescribeMean(residual)}; world preparation (inside movement phase, excluded from sum) {DescribeMean(worldPrepare)}. Means reconcile; sums of medians/percentiles need not equal phase medians/percentiles.");
            if (_crowd.MeasureGpuKernelTime) Line($"{scenario}/local kernel GPU execution {DescribeMean(kernelGpu)}; nested in dispatch/wait, not added to boundary or viewport timings.");
        }
    }

    private void StageGates()
    {
        List<FpsHordeEnemySnapshot> bodies = new();
        _crowd.CopyAlive(bodies);
        for (int i = 0; i < bodies.Count; i++)
        {
            int sector = i % 4, index = i / 4;
            Vector3 outward = sector switch { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
            Vector3 across = new(-outward.Z, 0, outward.X);
            Vector3 point = outward * (_map.HillMapSettings!.RoomHalfSize + 3f + index / 20 * .8f) + across * ((index % 20 - 9.5f) * .8f);
            point.Y = _map.SampleHillSurfaceHeight(point);
            _crowd.TrySetPosition(bodies[i].Handle, point);
        }
    }

    private void ResetCounters() { _simulationMs = _movementMs = _contactsMs = _attacksMs = 0; _frameSteps = 0; }
    private static double Median(List<double> values) { double[] sorted = values.OrderBy(x => x).ToArray(); return sorted[sorted.Length / 2]; }
    private static string Describe(List<double> values)
    {
        double[] sorted = values.OrderBy(x => x).ToArray();
        return $"median={sorted[sorted.Length / 2]:F3}, p95={sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F3}, p99={sorted[(int)Math.Ceiling(sorted.Length * .99) - 1]:F3}, max={sorted[^1]:F3} ms";
    }
    private static string DescribeMean(List<double> values) => $"mean={values.Average():F3}, {Describe(values)}";
    private void Line(string text) { _report.AppendLine(text); GD.Print(text); }
    private void Save()
    {
        if (!_canSave || _directory.Length == 0) return;
        File.WriteAllText(Path.Combine(_directory, "report.txt"), _report.ToString());
        File.WriteAllText(Path.Combine(_directory, "samples.csv"), _csv.ToString());
        File.WriteAllText(Path.Combine(_directory, "components.csv"), _componentCsv.ToString());
    }
    private void AssertBackends()
    {
        if (_crowd.ActiveCount != _count || _crowd.RenderedInstanceCount != _count)
            throw new InvalidOperationException($"Population mismatch: requested {_count}, alive {_crowd.ActiveCount}, rendered {_crowd.RenderedInstanceCount}.");
        if (_crowd.LastCrowdRenderBackend != ExpectedRenderBackend)
            throw new InvalidOperationException($"Unexpected render fallback: requested {ExpectedRenderBackend}, actual {_crowd.LastCrowdRenderBackend}; {_crowd.GpuCrowdRenderFallbackReason}");
        if (_crowd.LastCrowdContactBackend != "GPU")
            throw new InvalidOperationException($"Unexpected contact fallback: {_crowd.LastCrowdContactBackend}; {_crowd.GpuCrowdContactFallbackReason}");
        AssertDriveBackend();
    }
    private void AssertDriveBackend()
    {
        if (_crowd.LastGpuCrowdDriveResolved == _cpuMovement || !_crowd.LastGpuCrowdMovementResolved)
            throw new InvalidOperationException($"Unexpected movement backend: requested {MovementMode}; full drive={_crowd.LastGpuCrowdDriveResolved}; GPU world={_crowd.LastGpuCrowdMovementResolved}.");
    }
    private static string Option(string name, string environment, string defaultValue)
    {
        string prefix = "--" + name + "=";
        foreach (string argument in OS.GetCmdlineUserArgs())
            if (argument.StartsWith(prefix, StringComparison.Ordinal)) return argument[prefix.Length..];
        return System.Environment.GetEnvironmentVariable(environment) ?? defaultValue;
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
    }
}
