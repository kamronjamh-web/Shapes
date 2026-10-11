using SuperGatchaRebuild.Fps.Gpu;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using Environment = System.Environment;

namespace SuperGatchaRebuild.Experiments;

/// <summary>Opt-in, isolated-save CPU/GPU investigation on real fort snapshots.</summary>
public partial class InvestigateGpuCrowd : Node
{
    [Export(PropertyHint.Range, "4,120,1")] public int WarmupSamples { get; set; } = 32;
    [Export(PropertyHint.Range, "16,300,1")] public int MeasuredSamples { get; set; } = 96;
    [Export] public string PopulationCounts { get; set; } = "500,1000,2000";
    [Export(PropertyHint.Range, "2000,100000,1000")] public int ProbeCapacity { get; set; } = 2000;
    [Export] public bool IncludeFlatContactControl { get; set; }
    [Export] public bool RunPackingDiagnostics { get; set; }
    private const float Dt = 1f / 30f;
    private readonly StringBuilder _report = new();
    private readonly StringBuilder _trace = new("scenario,count,capacity,sample,cpu_contacts_ms,gpu_total_ms,pack_ms,upload_ms,dispatch_wait_ms,readback_ms,apply_ms,gpu_execution_ms,cpu_processor,pack_start_processor,pack_end_processor,cpu_full_scan_contacts_ms\n");
    private readonly StringBuilder _summary = new("scenario,count,capacity,cpu_contact_median_ms,cpu_contact_p95_ms,gpu_total_median_ms,gpu_total_p95_ms,pack_median_ms,upload_median_ms,dispatch_wait_median_ms,readback_median_ms,apply_median_ms,gpu_execution_median_ms,gpu_execution_p95_ms,max_position_pressure_velocity_error,cpu_full_scan_contact_median_ms,cpu_full_scan_contact_p95_ms\n");
    private int _parityCases, _comparedBodies;
    private double _maxError;

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/gpu-crowd-investigation/"))
                throw new InvalidOperationException("Use RunGpuCrowdInvestigation.ps1 to isolate save data.");
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("This investigation requires actual GPU compute; do not use --headless.");
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Input.MouseMode = Input.MouseModeEnum.Visible;
            PopulationCounts = Environment.GetEnvironmentVariable("SHAPES_GPU_PROBE_COUNTS") ?? PopulationCounts;
            ProbeCapacity = int.Parse(Environment.GetEnvironmentVariable("SHAPES_GPU_PROBE_CAPACITY") ?? ProbeCapacity.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            IncludeFlatContactControl = Environment.GetEnvironmentVariable("SHAPES_GPU_PROBE_FLAT") == "1" || IncludeFlatContactControl;
            if (ProbeCapacity < 2000 || ProbeCapacity > FpsHordeSettings.MaximumCapacity) throw new InvalidOperationException("Probe allocation must be between 2,000 and 100,000 slots.");
            FpsHordeSettings cachedDefaults = GD.Load<FpsHordeSettings>("res://Resources/Fps/HillFort_Horde.tres");
            int originalDefaultCapacity = cachedDefaults.Capacity;
            bool originalDefaultGpuBackend = cachedDefaults.GpuCrowdContactsEnabled;
            Line($"GPU crowd investigation\nUTC: {DateTime.UtcNow:O}\nGodot: {Engine.GetVersionInfo()["string"]}\nCPU: {OS.GetProcessorName()}\nGPU: {RenderingServer.GetVideoAdapterName()}\nRenderer: {RenderingServer.GetCurrentRenderingMethod()}\nWarmup: {WarmupSamples}; measured samples per path: {MeasuredSamples}; physics dt: {Dt:R}s\n");
            bool optimizerDisabled = typeof(InvestigateGpuCrowd).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;
            bool apiOptimizerDisabled = typeof(Vector3).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;
            Line($"Game assembly JIT optimizer disabled: {optimizerDisabled}; Godot API assembly JIT optimizer disabled: {apiOptimizerDisabled}; DOTNET_TieredCompilation: {Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}; population order: {PopulationCounts}.");
            Line($"Fixed allocation: {ProbeCapacity} slots at every population; flat contact control: {IncludeFlatContactControl}. Extended fort populations increase queue length at unchanged body size/initial spacing, not fivefold local density.");
            Line("Scope: fort contact/piling, terrain support, ceilings, wall constraints and live gates run on the GPU; CPU retains movement/AI and its world checks, attacks, rolling and render-data preparation. Complete-round-trip timings are wall time; separate Vulkan GPU timestamp intervals cover the four compute phases/barriers, including contact world checks. Neither is whole-game FPS or a GPU occupancy measurement. Shader compilation/allocation, initial static geometry upload and state restore are excluded from steady timings; packing, per-call live gate capture/upload, body upload, dispatch/wait, readback and managed application are included in GPU total. Full-scan reference CPU, indexed CPU and GPU use immutable matched snapshots with balanced execution order. GPU contact grid is rebuilt and deterministically sorted each call. Rendering loop disabled by runner to isolate compute.\n");
            await ValidateEdgeCases();
            await ValidateGpuWorldQueries();

            foreach (int count in PopulationCounts.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)))
            {
                if (count < 1 || count > ProbeCapacity) throw new InvalidOperationException("Probe population must fit the fixed experimental allocation.");
                // Fresh seeded map per population; no uncontrolled live physics ticks.
                FpsVerticalSlice map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
                map.ProcessMode = ProcessModeEnum.Disabled;
                map.HordeSettings = (FpsHordeSettings)map.HordeSettings!.Duplicate(true);
                map.HordeSettings.Capacity = ProbeCapacity;
                map.HordeSettings.GpuCrowdContactsEnabled = false;
                AddChild(map);
                map.DebugPlayerInvulnerable = true;
                map.DebugCoreInvulnerable = true;
                foreach (FpsDefenseGate gate in map.DefenseGates) gate.DebugInvulnerable = true;
                FpsHordeManager crowd = map.HordeManager!;
                using GpuCrowdKernel kernel = new(crowd.Capacity);
                Line($"Local compute device: {kernel.DeviceName}; allocated slots: {crowd.Capacity}; GPU hash buckets: {kernel.Buckets}; input {crowd.Capacity * 64} bytes; output {crowd.Capacity * 32} bytes per contact pass.\n");
                if (crowd.Capacity != ProbeCapacity) throw new InvalidOperationException("Crowd allocation differs from the requested fixed capacity.");
                if (map.SpawnGpuProbeFort(count) != count) throw new InvalidOperationException($"Could not spawn {count} fort enemies.");
                crowd.SetCombatEnabled(true);
                await ProfileActualCrowd(crowd, "approach", count, 30);
                await Benchmark(crowd, kernel, "approach", count, map);

                StageGatePressure(map, crowd);
                await ProfileActualCrowd(crowd, "gate-pressure", count, 90);
                Line($"Staged gate-pressure state: {crowd.StackedEnemyCount} raised enemies; highest lift {crowd.HighestPileFeet:F3} m. Placement is deliberate congestion, not a natural wave arrival.");
                await Benchmark(crowd, kernel, "gate-pressure", count, map);
                if (IncludeFlatContactControl) await BenchmarkFlatControl(map, count);
                map.QueueFree();
                await Frames(2);
            }
            if (cachedDefaults.Capacity != originalDefaultCapacity) throw new InvalidOperationException("The probe changed cached production capacity.");
            if (cachedDefaults.GpuCrowdContactsEnabled != originalDefaultGpuBackend) throw new InvalidOperationException("The probe changed cached production GPU-backend settings.");
            Line($"Cached production allocation preserved: {cachedDefaults.Capacity} slots. Exact active counts were asserted before every benchmark.");
            Line($"\nPASS: {_parityCases} CPU/GPU parity cases, {_comparedBodies} live body comparisons; maximum position/pressure/vertical-velocity error {_maxError:G6} (tolerance 0.001). Dead slots are checked for unchanged managed state; unused raw GPU output is not asserted.");
            Line("Limitations: one contact iteration only; no GPU movement/AI, GPU rendering-buffer integration, async readback, live combat performance trial or sustained frame-rate measurement. Steady GPU timings exclude initial static-world upload/allocation and uploads after geometry rebuilds. Frozen fort snapshots, direct world-query fixtures and a 32-pass evolving contact-only fixture do not establish long-run full-game trajectory parity. Flat controls use synthetic terrain and exclude fort world callbacks. Results are specific to this hardware/driver and do not establish an increased supported population.");
            SaveReports();
            GD.Print("GPU crowd investigation complete.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Line($"FAILED: {error}");
            SaveReports();
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task ProfileActualCrowd(FpsHordeManager crowd, string scenario, int count, int warmup)
    {
        List<double> total = new(), movement = new(), contact = new(), attack = new();
        for (int i = 0; i < warmup + MeasuredSamples; i++)
        {
            crowd.TickSimulation(Dt);
            if (i >= warmup)
            {
                total.Add(crowd.LastSimulationMilliseconds);
                movement.Add(crowd.LastMovementMilliseconds);
                contact.Add(crowd.LastContactMilliseconds);
                attack.Add(crowd.LastAttackMilliseconds);
            }
            if (i % 16 == 0) await Frames(1);
        }
        Line($"Actual indexed CPU crowd / {scenario} / {count}: total {Describe(total)}; movement {Describe(movement)}; contacts+rolling {Describe(contact)}; attack {Describe(attack)}. GPU backend disabled while advancing simulation; excludes rendering and weapon ticks. The matched contact benchmark below excludes rolling on every path.");
    }

    private async Task Benchmark(FpsHordeManager crowd, GpuCrowdKernel kernel, string scenario, int count, FpsVerticalSlice? map = null)
    {
        if (crowd.ActiveCount != count) throw new InvalidOperationException($"Expected {count} active bodies in {scenario}, got {crowd.ActiveCount}.");
        object original = crowd.SaveGpuProbeState();
        bool worldResolved = map != null;
        kernel.SetWorld(map?.CaptureGpuCrowdWorld());
        if (worldResolved && map!.CaptureGpuCrowdWorld() == null)
            throw new InvalidOperationException("Fort benchmark requires an exportable GPU world.");
        GpuCrowdBody[] bodies = new GpuCrowdBody[crowd.Capacity];
        GpuCrowdParameters hashParameters = crowd.PackGpuCrowd(bodies, Dt);
        string bodyHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(bodies.AsSpan())));
        byte[] parameterBytes = new byte[64];
        MemoryMarshal.Write(parameterBytes.AsSpan(), in hashParameters);
        string parameterHash = Convert.ToHexString(SHA256.HashData(parameterBytes));
        Line($"Snapshot {scenario}/{count}: bodies SHA256={bodyHash}; parameters SHA256={parameterHash}. Canonical hash includes CPU-packed ground/support for comparison with earlier runs; timed fort transfers skip those CPU world queries.");
        CheckParity(crowd, kernel, original, bodies, scenario, map);
        ReportContactWork(kernel, bodies, hashParameters);
        List<double> cpu = new(), fullCpu = new(), total = new(), pack = new(), upload = new(), dispatch = new(), readback = new(), apply = new(), gpuExecution = new();
        double cpuTime = 0, fullCpuTime = 0, totalTime = 0, packTime = 0, uploadTime = 0, applyTime = 0;
        int cpuProcessor = 0, packStartProcessor = 0, packEndProcessor = 0;
        GpuCrowdTiming timing = default;
        GpuCrowdParameters parameters = default;
        void RunCpu()
        {
            if (map != null) map.FortSpatialQueriesEnabled = true;
            crowd.RestoreGpuProbeState(original);
            cpuProcessor = Thread.GetCurrentProcessorId();
            long start = Stopwatch.GetTimestamp();
            crowd.RunCpuProbeContacts(Dt);
            cpuTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        void RunFullCpu()
        {
            crowd.RestoreGpuProbeState(original);
            map!.FortSpatialQueriesEnabled = false;
            try
            {
                long start = Stopwatch.GetTimestamp();
                crowd.RunCpuProbeContacts(Dt);
                fullCpuTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            finally { map.FortSpatialQueriesEnabled = true; }
        }
        void RunGpu()
        {
            crowd.RestoreGpuProbeState(original);
            packStartProcessor = Thread.GetCurrentProcessorId();
            long all = Stopwatch.GetTimestamp(), start = all;
            parameters = crowd.PackGpuCrowd(bodies, Dt, worldResolved);
            packTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            packEndProcessor = Thread.GetCurrentProcessorId();
            start = Stopwatch.GetTimestamp();
            kernel.SetWorld(map?.CaptureGpuCrowdWorld());
            kernel.Upload(bodies);
            uploadTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            (byte[] output, GpuCrowdTiming runTiming) = kernel.Run(parameters);
            timing = runTiming;
            start = Stopwatch.GetTimestamp();
            crowd.ApplyGpuCrowdResults(output, worldResolved);
            applyTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            totalTime = Stopwatch.GetElapsedTime(all).TotalMilliseconds;
        }
        for (int i = 0; i < WarmupSamples + MeasuredSamples; i++)
        {
            if (map == null)
            {
                if ((i & 1) == 0) { RunCpu(); RunGpu(); } else { RunGpu(); RunCpu(); }
                fullCpuTime = cpuTime; // Flat controls have no structure-index distinction.
            }
            else switch (i % 6)
            {
                case 0: RunFullCpu(); RunCpu(); RunGpu(); break;
                case 1: RunGpu(); RunCpu(); RunFullCpu(); break;
                case 2: RunCpu(); RunFullCpu(); RunGpu(); break;
                case 3: RunGpu(); RunFullCpu(); RunCpu(); break;
                case 4: RunFullCpu(); RunGpu(); RunCpu(); break;
                default: RunCpu(); RunGpu(); RunFullCpu(); break;
            }
            if (i >= WarmupSamples)
            {
                cpu.Add(cpuTime); fullCpu.Add(fullCpuTime); total.Add(totalTime); pack.Add(packTime); upload.Add(uploadTime);
                dispatch.Add(timing.DispatchAndWaitMs); readback.Add(timing.ReadbackMs); apply.Add(applyTime);
                gpuExecution.Add(timing.GpuExecutionMs);
                _trace.AppendLine(FormattableString.Invariant($"{scenario},{count},{crowd.Capacity},{i - WarmupSamples},{cpuTime:F6},{totalTime:F6},{packTime:F6},{uploadTime:F6},{timing.DispatchAndWaitMs:F6},{timing.ReadbackMs:F6},{applyTime:F6},{timing.GpuExecutionMs:F6},{cpuProcessor},{packStartProcessor},{packEndProcessor},{fullCpuTime:F6}"));
            }
            if (i % 8 == 0) await Frames(1);
        }
        double error = CheckParity(crowd, kernel, original, bodies, scenario + "-after-timing", map);
        Line($"Matched contact snapshot / {scenario} / {count}: {(map == null ? "flat CPU" : "indexed CPU")} {Describe(cpu)}; GPU complete round trip {Describe(total)}; indexed/flat CPU to GPU median ratio {Median(cpu) / Median(total):F2}x.");
        Line($"  {(map == null ? "Flat CPU reference (same samples; no fort index)" : "Original full-scan CPU reference")}: {Describe(fullCpu)}; full-scan/flat CPU to GPU median ratio {Median(fullCpu) / Median(total):F2}x.");
        Line($"  GPU components (wall-time medians): pack {Median(pack):F4}, gate capture/upload + body upload {Median(upload):F4}, dispatch+wait {Median(dispatch):F4}, readback {Median(readback):F4}, {(worldResolved ? "managed apply/diagnostics" : "apply+CPU world constraints")} {Median(apply):F4} ms.");
        Line($"  Vulkan GPU timestamp execution interval: {Describe(gpuExecution)}. Covers all four phases/barriers{(worldResolved ? " and fort contact world checks" : "")}; excludes CPU preparation/application and readback. Does not measure occupancy.");
        _summary.AppendLine(FormattableString.Invariant($"{scenario},{count},{crowd.Capacity},{Median(cpu):F6},{Percentile(cpu, .95):F6},{Median(total):F6},{Percentile(total, .95):F6},{Median(pack):F6},{Median(upload):F6},{Median(dispatch):F6},{Median(readback):F6},{Median(apply):F6},{Median(gpuExecution):F6},{Percentile(gpuExecution, .95):F6},{error:G9},{Median(fullCpu):F6},{Percentile(fullCpu, .95):F6}"));
        crowd.RestoreGpuProbeState(original);
        parameters = crowd.PackGpuCrowd(bodies, Dt, worldResolved);
        kernel.SetWorld(map?.CaptureGpuCrowdWorld());
        kernel.Upload(bodies);
        List<double> batched = new(), batchedGpu = new();
        for (int i = 0; i < 12; i++)
        {
            var result = kernel.Run(parameters, repeats: 32, readback: false);
            if (i >= 4) { batched.Add(result.Timing.DispatchAndWaitMs / 32); batchedGpu.Add(result.Timing.GpuExecutionMs / 32); }
            await Frames(1);
        }
        Line($"  Repeated fixed-input kernel throughput, 32 dispatch groups per submit and no readback: {Describe(batched)} per pass. This is NOT evolving GPU crowd simulation or an achievable whole-game frame time.\n");
        Line($"  Corresponding repeated fixed-input GPU timestamp interval per pass: {Describe(batchedGpu)}.\n");
        crowd.RestoreGpuProbeState(original);
        if (RunPackingDiagnostics && scenario == "approach" && count == 2000) DiagnosePacking(crowd, original, bodies);
        SaveReports();
    }

    private async Task BenchmarkFlatControl(FpsVerticalSlice map, int count)
    {
        FpsHordeSettings settings = (FpsHordeSettings)map.HordeSettings!.Duplicate(true);
        FpsHordeManager crowd = new() { Settings = settings, ProcessMode = ProcessModeEnum.Disabled, SurfaceHeight = _ => 0f };
        AddChild(crowd);
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        const float spacing = .8f;
        int rows = (count + columns - 1) / columns;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = new((i % columns - (columns - 1) * .5f) * spacing, 0f, (i / columns - (rows - 1) * .5f) * spacing);
            FpsHordeHandle handle = crowd.Spawn(map.BruiserArchetypeSettings!, FpsEnemyRole.Bruiser, position);
            crowd.SetGpuProbeMotion(handle, position, Vector3.Forward, pressure: .5f);
        }
        Line($"Flat contact control / {count}: real cube archetype and contact solver, fixed 0.8m lattice spacing, {columns} columns, {rows} rows. Synthetic ground at Y=0; no fort callbacks, movement, attacks or initial settling. Increasing count expands the occupied area at roughly fixed contact density.");
        using GpuCrowdKernel kernel = new(crowd.Capacity);
        await Benchmark(crowd, kernel, "flat-contact-control", count);
        crowd.QueueFree();
        await Frames(1);
    }

    private void ReportContactWork(GpuCrowdKernel kernel, GpuCrowdBody[] bodies, GpuCrowdParameters parameters)
    {
        // Untimed diagnostics of the exact input: hash collisions and accepted/scanned
        // budgets can change workload even when the population grows uniformly.
        int[] occupancy = new int[kernel.Buckets];
        kernel.Upload(bodies);
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(kernel.Run(parameters).Output);
        int live = 0, acceptedCapped = 0, scannedCapped = 0;
        double accepted = 0, scanned = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            ref GpuCrowdBody body = ref bodies[i];
            if (body.WorldVelocityAlive.W < .5f) continue;
            live++;
            int x = Mathf.FloorToInt(body.PositionRadius.X / parameters.HorizontalCell);
            int y = Mathf.FloorToInt(body.PositionRadius.Y / parameters.VerticalCell);
            int z = Mathf.FloorToInt(body.PositionRadius.Z / parameters.HorizontalCell);
            uint hash = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u);
            occupancy[hash & (uint)(kernel.Buckets - 1)]++;
            accepted += results[i].VelocityCounts.Y;
            scanned += results[i].VelocityCounts.Z;
            if (results[i].VelocityCounts.Y >= parameters.Budget) acceptedCapped++;
            if (results[i].VelocityCounts.Z >= parameters.Budget * 8) scannedCapped++;
        }
        Line($"Contact workload: live {live}, occupied hash buckets {occupancy.Count(n => n > 0)}/{kernel.Buckets}, maximum bucket chain {occupancy.Max()}, mean accepted/scanned {accepted / Math.Max(1, live):F2}/{scanned / Math.Max(1, live):F2}, bodies reaching accepted/scanned budgets {acceptedCapped}/{scannedCapped}. Bucket chains include hash collisions.");
    }

    private void DiagnosePacking(FpsHordeManager crowd, object original, GpuCrowdBody[] bodies)
    {
        // Separate diagnostic, outside benchmark samples. Wrappers themselves add
        // overhead, so these values locate costs rather than replace result timings.
        var ground = crowd.SurfaceHeight;
        var support = crowd.SurfaceSupportHeight;
        int groundCalls = 0, supportCalls = 0;
        double groundMs = 0, supportMs = 0;
        try
        {
            crowd.SurfaceHeight = position =>
            {
                long start = Stopwatch.GetTimestamp();
                float result = ground?.Invoke(position) ?? 0f;
                groundMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                groundCalls++;
                return result;
            };
            crowd.SurfaceSupportHeight = (from, desired, radius, height) =>
            {
                long start = Stopwatch.GetTimestamp();
                float result = support?.Invoke(from, desired, radius, height) ?? ground?.Invoke(desired) ?? 0f;
                supportMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                supportCalls++;
                return result;
            };
            var wrappedGround = crowd.SurfaceHeight;
            var wrappedSupport = crowd.SurfaceSupportHeight;
            List<double> directPack = new(), wrappedPack = new();
            for (int i = 0; i < 32; i++)
            for (int variant = 0; variant < 2; variant++)
            {
                bool wrapped = ((i + variant) & 1) != 0;
                crowd.SurfaceHeight = wrapped ? wrappedGround : ground;
                crowd.SurfaceSupportHeight = wrapped ? wrappedSupport : support;
                crowd.RestoreGpuProbeState(original);
                long start = Stopwatch.GetTimestamp();
                crowd.PackGpuCrowd(bodies, Dt);
                long end = Stopwatch.GetTimestamp();
                if (i >= 16) (wrapped ? wrappedPack : directPack).Add((end - start) * 1000.0 / Stopwatch.Frequency);
            }
            Line($"Packing diagnostic repeated direct delegates: {Describe(directPack)}; wrapped original delegates: {Describe(wrappedPack)}; processor {Thread.GetCurrentProcessorId()}.");
            crowd.SurfaceHeight = wrappedGround;
            crowd.SurfaceSupportHeight = wrappedSupport;
            for (int pass = 0; pass < 2; pass++)
            {
                crowd.RestoreGpuProbeState(original);
                groundCalls = supportCalls = 0; groundMs = supportMs = 0;
                long start = Stopwatch.GetTimestamp();
                if (pass == 0) crowd.RunCpuProbeContacts(Dt); else crowd.PackGpuCrowd(bodies, Dt);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Line($"Packing diagnostic {(pass == 0 ? "CPU-contact" : "GPU-pack")}: total {elapsed:F4} ms, ground {groundCalls} calls/{groundMs:F4} ms, support {supportCalls} calls/{supportMs:F4} ms (instrumented, not benchmark samples).");
            }
            crowd.SurfaceHeight = _ => 0f;
            crowd.SurfaceSupportHeight = (_, _, _, _) => 0f;
            crowd.RestoreGpuProbeState(original);
            List<double> packOnly = new();
            for (int i = 0; i < 16; i++)
            {
                long start = Stopwatch.GetTimestamp();
                crowd.PackGpuCrowd(bodies, Dt);
                if (i >= 8) packOnly.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            Line($"Packing diagnostic with constant terrain/support stubs: {Describe(packOnly)}. Does not represent production world queries.\n");
        }
        finally
        {
            crowd.SurfaceHeight = ground;
            crowd.SurfaceSupportHeight = support;
            crowd.RestoreGpuProbeState(original);
        }
    }

    private double CheckParity(FpsHordeManager crowd, GpuCrowdKernel kernel, object original, GpuCrowdBody[] bodies, string name, FpsVerticalSlice? map = null)
    {
        crowd.RestoreGpuProbeState(original);
        if (map != null) map.FortSpatialQueriesEnabled = false;
        try { crowd.RunCpuProbeContacts(Dt); }
        finally { if (map != null) map.FortSpatialQueriesEnabled = true; }
        object expected = crowd.SaveGpuProbeState();
        int expectedRaised = crowd.StackedEnemyCount;
        float expectedLift = crowd.HighestPileFeet;
        crowd.RestoreGpuProbeState(original);
        if (map != null)
        {
            crowd.RunCpuProbeContacts(Dt);
            crowd.CompareGpuProbeState(expected);
            if (crowd.StackedEnemyCount != expectedRaised || Mathf.Abs(crowd.HighestPileFeet - expectedLift) > .001f)
                throw new InvalidOperationException($"Indexed CPU pile diagnostics differ from full scan in {name}.");
            crowd.RestoreGpuProbeState(original);
        }
        bool worldResolved = map != null;
        GpuCrowdParameters parameters = crowd.PackGpuCrowd(bodies, Dt, worldResolved);
        kernel.SetWorld(map?.CaptureGpuCrowdWorld());
        kernel.Upload(bodies);
        crowd.ApplyGpuCrowdResults(kernel.Run(parameters).Output, worldResolved);
        var comparison = crowd.CompareGpuProbeState(expected);
        if (crowd.StackedEnemyCount != expectedRaised || Mathf.Abs(crowd.HighestPileFeet - expectedLift) > 0.001f)
            throw new InvalidOperationException($"Pile diagnostics differ in {name}.");
        _parityCases++;
        _comparedBodies += comparison.Compared;
        _maxError = Math.Max(_maxError, comparison.MaxError);
        Line($"Parity {name}: {comparison.Compared} bodies, maximum error {comparison.MaxError:G6}.");
        crowd.RestoreGpuProbeState(original);
        return comparison.MaxError;
    }

    private async Task ValidateEdgeCases()
    {
        FpsHordeManager fixture = new()
        {
            Settings = new FpsHordeSettings { Capacity = 256, MapExtent = 100, ContactNeighborBudget = 8 },
            SurfaceHeight = _ => 0f,
            CorePosition = () => new Vector3(0f, 0f, -20f),
            PlayerPosition = () => new Vector3(40f, 0f, 40f),
            IsPlayerAlive = () => false,
        };
        fixture.ProcessMode = ProcessModeEnum.Disabled;
        AddChild(fixture);
        using GpuCrowdKernel kernel = new(fixture.Capacity);
        GpuCrowdBody[] bodies = new GpuCrowdBody[fixture.Capacity];
        FpsEnemyArchetypeSettings small = new() { CollisionRadius = 0.35f, CollisionHeight = 0.8f };
        FpsEnemyArchetypeSettings large = new() { CollisionRadius = 0.9f, CollisionHeight = 2.2f };
        CheckParity(fixture, kernel, fixture.SaveGpuProbeState(), bodies, "empty");
        List<FpsHordeHandle> handles = new();
        for (int i = 0; i < 129; i++)
            handles.Add(fixture.Spawn(i % 3 == 0 ? large : small, FpsEnemyRole.Bruiser, new Vector3((i % 13 - 6) * .4f, 0, (i / 13 - 5) * .4f)));
        for (int i = 0; i < handles.Count; i++)
        {
            if (i % 11 == 0) fixture.ApplyDamage(handles[i], 10000f);
            else if (i % 7 == 0) fixture.TrySetPosition(handles[i], new Vector3(0, 5, 0));
            else if (i % 5 == 0) fixture.TrySetPosition(handles[i], new Vector3(-.001f, 0, -.001f));
        }
        fixture.TickSimulation(Dt);
        object original = fixture.SaveGpuProbeState();
        CheckParity(fixture, kernel, original, bodies, "mixed-sizes-negative-cells-overlap-dead-slots-airborne-budget8");
        fixture.Settings.ContactNeighborBudget = 64;
        CheckParity(fixture, kernel, original, bodies, "neighbor-budget64");
        fixture.Settings.StackingEnabled = false;
        CheckParity(fixture, kernel, original, bodies, "climbing-disabled");
        fixture.Settings.StackingEnabled = true;
        fixture.Settings.MaxPileHeight = .5f;
        CheckParity(fixture, kernel, original, bodies, "lowered-pile-cap");

        fixture.Settings.MaxPileHeight = 10f;
        fixture.Settings.ContactNeighborBudget = 32;
        object cpuEvolving = original, gpuEvolving = original;
        for (int step = 0; step < 32; step++)
        {
            fixture.RestoreGpuProbeState(cpuEvolving);
            fixture.AdvanceGpuProbePreviousPositions();
            fixture.RunCpuProbeContacts(Dt);
            cpuEvolving = fixture.SaveGpuProbeState();
            fixture.RestoreGpuProbeState(gpuEvolving);
            fixture.AdvanceGpuProbePreviousPositions();
            GpuCrowdParameters evolvingParameters = fixture.PackGpuCrowd(bodies, Dt);
            kernel.Upload(bodies);
            fixture.ApplyGpuCrowdResults(kernel.Run(evolvingParameters).Output);
            gpuEvolving = fixture.SaveGpuProbeState();
            var comparison = fixture.CompareGpuProbeState(cpuEvolving);
            _parityCases++; _comparedBodies += comparison.Compared;
            _maxError = Math.Max(_maxError, comparison.MaxError);
        }
        Line("Parity evolving contact-only cluster: 32 separately advanced CPU/GPU states passed (no movement, gravity integration or weapons).");

        // A rising body must reject lower bodies it has already passed over.
        // Eight rejected candidates precede a real neighbor in Y traversal. An
        // incorrect previous-height filter exhausts budget 8 and loses its push.
        fixture.Clear();
        fixture.Settings.ContactNeighborBudget = 8;
        fixture.Settings.StackingEnabled = false;
        for (int i = 0; i < 8; i++) fixture.Spawn(large, FpsEnemyRole.Bruiser, Vector3.Zero);
        FpsHordeHandle rising = fixture.Spawn(small, FpsEnemyRole.Bruiser, Vector3.Zero);
        FpsHordeHandle neighbor = fixture.Spawn(small, FpsEnemyRole.Bruiser, new Vector3(.4f, 0, 0));
        fixture.TrySetPosition(rising, new Vector3(0, 3, 0));
        fixture.TrySetPosition(neighbor, new Vector3(.4f, 3, 0));
        fixture.SetGpuProbeMotion(rising, new Vector3(0, 1.8f, 0), Vector3.Zero);
        original = fixture.SaveGpuProbeState();
        fixture.RunCpuProbeContacts(Dt);
        if (!fixture.TryGet(rising, out var risen) || risen.Position.X > -.01f)
            throw new InvalidOperationException("Rising-body fixture must receive a horizontal push from its valid neighbor.");
        CheckParity(fixture, kernel, original, bodies, "rising-body-rejects-lower-neighbors-before-budget");

        fixture.Clear();
        fixture.Spawn(large, FpsEnemyRole.Bruiser, Vector3.Zero);
        FpsHordeHandle falling = fixture.Spawn(small, FpsEnemyRole.Bruiser, Vector3.Zero);
        fixture.TrySetPosition(falling, new Vector3(0, 2, 0));
        fixture.SetGpuProbeMotion(falling, new Vector3(0, 2.4f, 0), Vector3.Zero, verticalVelocity: -3f);
        original = fixture.SaveGpuProbeState();
        fixture.RunCpuProbeContacts(Dt);
        if (!fixture.TryGet(falling, out var supported) || supported.Position.DistanceTo(new Vector3(0, 2.2f, 0)) > .0001f)
            throw new InvalidOperationException("Falling-body fixture must land on the lower body without lateral displacement.");
        CheckParity(fixture, kernel, original, bodies, "falling-body-supported-with-climbing-disabled");
        fixture.QueueFree();
        await Frames(1);
    }

    private static void StageGatePressure(FpsVerticalSlice map, FpsHordeManager crowd)
    {
        List<FpsHordeEnemySnapshot> enemies = new();
        crowd.CopyAlive(enemies);
        for (int i = 0; i < enemies.Count; i++)
        {
            int side = i % 4, index = i / 4;
            Vector3 outward = side switch { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
            Vector3 across = new(-outward.Z, 0, outward.X);
            Vector3 point = outward * (map.HillMapSettings!.RoomHalfSize + 3f + index / 20 * .8f) + across * ((index % 20 - 9.5f) * .8f);
            point.Y = map.SampleHillSurfaceHeight(point);
            crowd.TrySetPosition(enemies[i].Handle, point);
        }
    }

    private void Line(string text) { _report.AppendLine(text); GD.Print(text); }
    private void SaveReports()
    {
        string path = ProjectSettings.GlobalizePath("res://.tmp/gpu-crowd-investigation");
        string label = Environment.GetEnvironmentVariable("SHAPES_GPU_PROBE_LABEL") ?? "latest";
        if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new InvalidOperationException("Invalid probe label.");
        path = Path.Combine(path, label);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "report.txt"), _report.ToString());
        File.WriteAllText(Path.Combine(path, "samples.csv"), _trace.ToString());
        File.WriteAllText(Path.Combine(path, "summary.csv"), _summary.ToString());
    }
    private static double Median(List<double> values) => Percentile(values, .5);
    private static double Percentile(List<double> values, double p)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
    }
    private static string Describe(List<double> values) => $"n={values.Count} median={Median(values):F4} p95={Percentile(values, .95):F4} max={values.Max():F4} ms";
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
