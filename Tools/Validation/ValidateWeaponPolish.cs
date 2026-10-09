using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Compare actual attack ticks against identical real crowd records, with cosmetics on/off.
public partial class ValidateWeaponPolish : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private FpsVerticalSlice _map = null!;
    private FpsPlayerController _player = null!;
    private FpsWeaponPolishEffects Effects => _map.WeaponEffects ?? throw new InvalidOperationException("Scene cosmetic helper was not initialized.");
    private List<IFpsCombatEnemy> _targets = null!;
    private int _checks;
    private readonly Vector3 _origin = new(60f, 1.5f, -65f);
    private sealed record AttackResult(float[] Damage, float[] Timeline);

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/weapon-polish-validation/"), "Isolated save directory");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyHillFixture.Configure(_map);
            _map.BruiserArchetypeSettings = (FpsEnemyArchetypeSettings)_map.BruiserArchetypeSettings!.Duplicate(true);
            _map.BruiserArchetypeSettings.MaxHealth = 1000000f;
            AddChild(_map);
            await Frames(5);
            Check(_map.DebugSpawnHorde(100, true) == 100, "One hundred real targets spawned");
            _map.SetPhysicsProcess(false);
            _player = Field<FpsPlayerController>("_player");
            _player.SetPhysicsProcess(false);
            _player.SetProcess(false);
            _targets = Field<List<IFpsCombatEnemy>>("_combatEnemies");
            foreach (string shape in EquipmentCatalogDefaults.CreateDefaultShapes().Select(shape => shape.Key))
            {
                AttackResult baseline = Resolve(shape, false);
                AttackResult polished = Resolve(shape, true);
                Check(baseline.Damage.Any(value => value > 0f) && baseline.Damage.Zip(polished.Damage).All(pair => Mathf.Abs(pair.First - pair.Second) < 0.01f) &&
                    baseline.Damage.Count(value => value > 0f) == polished.Damage.Count(value => value > 0f) &&
                    baseline.Timeline.Zip(polished.Timeline).All(pair => Mathf.Abs(pair.First - pair.Second) < 0.01f),
                    $"{shape}: actual damage, distinct hits and every tick's timing survive cosmetic toggling");
                if (shape == "klein")
                {
                    AttackResult fullHealth = Resolve(shape, false, 1f);
                    Check(baseline.Damage.Sum() > fullHealth.Damage.Sum() && fullHealth.Damage.Sum() > 0f,
                        "Klein: the equipped passive damages real targets and missing HP increases its intensity");
                }
            }
            ValidateDetonationAudio();
            ValidateFeedback();
            ValidateLifecycle();
            await WeaponPolishAudioChecks.Run(this, Check);
            await Frames(2);
            Check(!Effects.GetChildren().Any(node => node.IsQueuedForDeletion()), "Cosmetic deferred cleanup completes");
            GD.Print($"Weapon polish validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private AttackResult Resolve(string shape, bool enabled, float healthFraction = 0.5f)
    {
        Invoke("ResetWeaponEffectRuntimeState");
        _map.WeaponPolishEnabled = enabled;
        Effects.Enabled = enabled;
        Field<RandomNumberGenerator>("_weaponRng").Seed = 413;
        var crowd = _map.HordeManager!;
        var constrain = crowd.ConstrainMovement;
        crowd.ConstrainMovement = null;
        try
        {
            for (int i = 0; i < _targets.Count; i++) _targets[i].GlobalPosition = new Vector3(-75f + i % 10, 0f, 65f + i / 10);
            // A line for piercing/saw, plus nearby off-axis records for Rift area ticks.
            _targets[0].GlobalPosition = new Vector3(60f, 0f, -76f);
            _targets[1].GlobalPosition = new Vector3(60f, 0f, -79f);
            _targets[2].GlobalPosition = new Vector3(62f, 0f, -77f);
            if (shape is "sphere" or "cube" or "klein")
            {
                _targets[0].GlobalPosition = new Vector3(63f, 0f, -65f);
                _targets[1].GlobalPosition = new Vector3(60f, 0f, -68f);
                _targets[2].GlobalPosition = new Vector3(57f, 0f, -65f);
            }
        }
        finally { crowd.ConstrainMovement = constrain; }
        _player.GlobalPosition = new Vector3(60f, 0f, -65f);
        float[] health = _targets.Select(target => target.CurrentHealth).ToArray();
        EquipmentItem item = Item(shape);
        Field<List<EquipmentItem>>("_arsenal")[0] = item;
        Invoke("EquipWeapon", 0, false);
        _player.SetDebugHealth(1000f, 1000f * healthFraction);
        if (shape != "klein") Invoke("FireWeaponAttack", item, _origin, (_targets[0].HeadPosition - _origin).Normalized(), 1f, false);
        if (shape == "hexagon")
        {
            IList mines = Field<IList>("_playerMines");
            Check(mines.Count > 0, "Hexagon deploys actual mines");
            object mine = mines[0]!;
            Vector3 point = (Vector3)mine.GetType().GetField("Position")!.GetValue(mine)!;
            var previousConstrain = crowd.ConstrainMovement;
            crowd.ConstrainMovement = null;
            try { _targets[0].GlobalPosition = point - Vector3.Up * 0.3f; }
            finally { crowd.ConstrainMovement = previousConstrain; }
        }
        float initialDamage = DamageSum(health);
        bool emitted = Effects.ActiveTransientCount > 0;
        List<float> timeline = new() { initialDamage };
        bool mineArmPreserved = true, echoDelayPreserved = true;
        for (int frame = 0; frame < 120; frame++)
        {
            Effects.BeginFrame(1f / 60f, _origin);
            Invoke("TickPlayerProjectiles", 1f / 60f);
            Invoke("TickMines", 1f / 60f);
            Invoke("TickZones", 1f / 60f);
            Invoke("TickOrbitals", 1f / 60f);
            Invoke("TickMortars", 1f / 60f);
            Invoke("TickEchoes", 1f / 60f);
            Invoke("TickClones", 1f / 60f);
            Invoke("TickWrathAura", 1f / 60f);
            Effects.EndFrame();
            emitted |= Effects.ActiveTransientCount > 0 || Effects.VisibleSegmentCount > 0;
            float cumulativeDamage = DamageSum(health);
            timeline.Add(cumulativeDamage);
            if (shape == "hexagon" && frame < 16) mineArmPreserved &= cumulativeDamage == 0f;
            if (shape == "mobius" && frame < 30) echoDelayPreserved &= cumulativeDamage == initialDamage && Field<IList>("_playerEchoes").Count == 1;
        }
        Check(emitted == enabled, $"{shape}: cosmetics follow their enable switch");
        if (shape == "hexagon") Check(mineArmPreserved && timeline[^1] > 0f && Field<IList>("_playerMines").Count < 3,
            "Hexagon preserves arming delay before actual triggered detonation");
        if (shape == "mobius") Check(echoDelayPreserved && timeline[^1] > initialDamage && Field<IList>("_playerEchoes").Count == 0,
            "Mobius waits for its actual delayed repeat and never schedules recursive repeats");
        if (shape == "boys") Check(initialDamage == 0f && timeline[^1] > 0f && Field<IList>("_playerClones").Count > 0,
            "Boy's Surface deployed clones deliver actual follow-up attacks");
        return new(health.Select((before, i) => before - _targets[i].CurrentHealth).ToArray(), timeline.ToArray());
    }

    private float DamageSum(float[] before) => before.Select((health, i) => health - _targets[i].CurrentHealth).Sum();

    private void ValidateDetonationAudio()
    {
        FpsWeaponPolishAudio audio = Field<FpsWeaponPolishAudio>("_weaponPolishAudio");
        audio.Enabled = true;
        audio.SetPaused(false);
        _player.Camera.Current = true;
        foreach ((string shape, WeaponAttackType type, string tick) in new[]
        {
            ("hexagon", WeaponAttackType.Mine, "TickMines"), ("hyperphere", WeaponAttackType.Mortar, "TickMortars"),
        })
        {
            Invoke("ResetWeaponEffectRuntimeState");
            _map.WeaponPolishEnabled = Effects.Enabled = true;
            Field<RandomNumberGenerator>("_weaponRng").Seed = 413;
            _player.GlobalPosition = new Vector3(60f, 0f, -65f);
            var crowd = _map.HordeManager!;
            var constrain = crowd.ConstrainMovement;
            crowd.ConstrainMovement = null;
            try
            {
                for (int i = 0; i < _targets.Count; i++) _targets[i].GlobalPosition = new Vector3(-75f + i % 10, 0f, 65f + i / 10);
                _targets[0].GlobalPosition = new Vector3(60f, 0f, -76f);
                _targets[1].GlobalPosition = new Vector3(62f, 0f, -77f);
            }
            finally { crowd.ConstrainMovement = constrain; }
            // Learn the public activation mapping from actual pooled playback, without cache indices.
            audio.Clear();
            audio.PlayActivation(type, _player.HeadPosition);
            AudioStream activation = audio.GetChildren().OfType<AudioStreamPlayer3D>().Single(voice => voice.Playing).Stream
                ?? throw new InvalidOperationException("Activation playback did not select a cached stream.");
            audio.Clear();
            EquipmentItem item = Item(shape);
            Field<List<EquipmentItem>>("_arsenal")[0] = item;
            Invoke("EquipWeapon", 0, false);
            Invoke("FireWeaponAttack", item, _origin, (_targets[0].HeadPosition - _origin).Normalized(), 1f, false);
            // Synthetic simulation advances faster than the audio device. Stop the onset voice
            // while preserving cooldown state, so it cannot impersonate the later detonation cue.
            foreach (AudioStreamPlayer3D voice in audio.GetChildren().OfType<AudioStreamPlayer3D>()) voice.Stop();
            if (shape == "hexagon")
            {
                object mine = Field<IList>("_playerMines")[0]!;
                Vector3 point = (Vector3)mine.GetType().GetField("Position")!.GetValue(mine)!;
                constrain = crowd.ConstrainMovement;
                crowd.ConstrainMovement = null;
                try { _targets[0].GlobalPosition = point - Vector3.Up * .3f; }
                finally { crowd.ConstrainMovement = constrain; }
            }
            float before = _targets[0].CurrentHealth;
            bool detonated = false;
            for (int frame = 0; frame < 90; frame++)
            {
                audio._Process(1d / 60d);
                Invoke(tick, 1f / 60f);
                if (_targets[0].CurrentHealth >= before) continue;
                detonated = true;
                break;
            }
            Check(detonated && audio.GetChildren().OfType<AudioStreamPlayer3D>().Any(voice => voice.Playing && !voice.StreamPaused &&
                voice.VolumeDb > -75f && ReferenceEquals(voice.Stream, activation)),
                $"{shape}: real damaging detonation preserves its cached activation cue despite victim impact callbacks");
            audio.Clear();
        }
        Invoke("ResetWeaponEffectRuntimeState");
    }

    private void ValidateFeedback()
    {
        foreach (string shape in EquipmentCatalogDefaults.CreateDefaultShapes().Select(shape => shape.Key))
        {
            Field<List<EquipmentItem>>("_arsenal")[0] = Item(shape);
            Invoke("EquipWeapon", 0, false);
            FpsWeaponFireFeedback feedback = (FpsWeaponFireFeedback)(typeof(FpsPlayerController).GetField("_weaponFireFeedback", Private)!.GetValue(_player)
                ?? throw new InvalidOperationException($"{shape}: firing feedback missing."));
            Node3D visual = (Node3D)typeof(FpsPlayerController).GetField("_weaponItemVisual", Private)!.GetValue(_player)!;
            Check(_player.TryGetWeaponMuzzle(out Vector3 muzzle) && muzzle.IsFinite() && feedback.GetChildren().OfType<MeshInstance3D>().Count() is > 0 and <= 36,
                $"{shape}: finite authored muzzle owns bounded nonempty firing geometry");
            Dictionary<Node3D, Transform3D> mechanisms = Nodes(visual).OfType<Node3D>().Where(node => node != feedback && !feedback.IsAncestorOf(node)).ToDictionary(node => node, node => node.Transform);
            var imported = MaterialStates(visual, true, feedback);
            var local = MaterialStates(visual, false, feedback);
            feedback.Fire(.4f);
            feedback.Advance(.07f);
            Basis basis = feedback.Transform.Basis;
            bool orthogonal = basis.X.IsFinite() && basis.Y.IsFinite() && basis.Z.IsFinite() && basis.X.LengthSquared() > 0f && basis.Y.LengthSquared() > 0f && basis.Z.LengthSquared() > 0f &&
                Mathf.Abs(basis.X.Normalized().Dot(basis.Y.Normalized())) < .001f && Mathf.Abs(basis.X.Normalized().Dot(basis.Z.Normalized())) < .001f &&
                Mathf.Abs(basis.Y.Normalized().Dot(basis.Z.Normalized())) < .001f;
            feedback.Advance(1f);
            Check(orthogonal && !feedback.Visible && feedback.Transform.IsEqualApprox(Transform3D.Identity) &&
                mechanisms.All(entry => entry.Key.Transform.IsEqualApprox(entry.Value)) && MaterialsMatch(imported) && MaterialsMatch(local),
                $"{shape}: local-axis pulse expires, restores materials and leaves authored mechanisms/shared imports unchanged");
            float damage = _player.ShotDamage, health = _player.CurrentHealth;
            float cooldown = (float)typeof(FpsPlayerController).GetField("_fireCooldown", Private)!.GetValue(_player)!;
            Transform3D camera = _player.Camera.GlobalTransform;
            _player.PlayWeaponFireFeedback(.4f);
            Check(_player.ShotDamage == damage && _player.CurrentHealth == health &&
                (float)typeof(FpsPlayerController).GetField("_fireCooldown", Private)!.GetValue(_player)! == cooldown && _player.Camera.GlobalTransform.IsEqualApprox(camera),
                $"{shape}: public feedback preserves player stats, gameplay cooldown and shot camera");
            feedback.Reset();
        }
    }

    private static Dictionary<StandardMaterial3D, (Color Emission, float Energy, bool Enabled)> MaterialStates(Node3D visual, bool imported, Node feedback)
    {
        Dictionary<StandardMaterial3D, (Color, float, bool)> states = new();
        foreach (MeshInstance3D mesh in Nodes(visual).OfType<MeshInstance3D>().Where(mesh => !feedback.IsAncestorOf(mesh) && mesh.Mesh != null))
        for (int surface = 0; surface < mesh.Mesh!.GetSurfaceCount(); surface++)
        {
            Material? material = imported ? mesh.Mesh.SurfaceGetMaterial(surface) : mesh.GetActiveMaterial(surface);
            if (material is StandardMaterial3D standard) states[standard] = (standard.Emission, standard.EmissionEnergyMultiplier, standard.EmissionEnabled);
        }
        return states;
    }
    private static bool MaterialsMatch(Dictionary<StandardMaterial3D, (Color Emission, float Energy, bool Enabled)> states) =>
        states.All(entry => entry.Key.Emission == entry.Value.Emission && entry.Key.EmissionEnergyMultiplier == entry.Value.Energy && entry.Key.EmissionEnabled == entry.Value.Enabled);
    private static IEnumerable<Node> Nodes(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node descendant in Nodes(child)) yield return descendant; }
    }

    private void ValidateLifecycle()
    {
        var effects = Effects;
        effects.Enabled = true;
        _map.WeaponPolishEnabled = true;
        Invoke("ResetWeaponEffectRuntimeState");
        for (int i = 0; i < 1000; i++) effects.Impact(WeaponAttackType.Single, _origin, Vector3.Forward, Colors.Cyan);
        Check(effects.ActiveTransientCount > 0 && effects.ActiveTransientCount <= FpsWeaponPolishEffects.MaxTransientInstances, "Burst cosmetics respect transient capacity");
        effects.BeginFrame(0f, _origin);
        for (int i = 0; i < 2000; i++) effects.ProjectileTrail(WeaponAttackType.Single, _origin, _origin + Vector3.Forward, Colors.Cyan, 0.05f);
        for (int i = 0; i < 1000; i++) effects.DrawRift(_origin, 4.2f, 0.3f, 3.1f, 0.2f, 0.35f, Colors.Cyan);
        effects.EndFrame();
        Check(effects.ActiveTransientCount <= FpsWeaponPolishEffects.MaxTransientInstances, "Dense trails remain within transient capacity");
        Check(effects.VisibleSegmentCount > 0 && effects.VisibleSegmentCount <= FpsWeaponPolishEffects.MaxPersistentSegments, "Dense persistent fields respect segment capacity");
        Check(!effects.DrawRift(_origin, 4.2f, 0.3f, 3.1f, 0.2f, 0.35f, Colors.Cyan), "Full persistent batch declines another complete field");
        effects.BeginFrame(0f, _origin);
        int completeFields = 0;
        for (int i = 0; i <= FpsWeaponPolishEffects.MaxSolidInstances; i++)
        {
            if (!effects.DrawFlame(_origin, new Vector3(7f, 4f, -11f).Normalized(), 13f, .42f, .1f, .3f, Colors.Cyan)) break;
            completeFields++;
        }
        int solidCount = effects.VisibleSolidCount, segmentCount = effects.VisibleSegmentCount;
        bool rejected = !effects.DrawFlame(_origin, Vector3.Forward, 13f, .42f, .1f, .3f, Colors.Cyan);
        Check(completeFields > 0 && solidCount > 0 && solidCount <= FpsWeaponPolishEffects.MaxSolidInstances &&
            segmentCount < FpsWeaponPolishEffects.MaxPersistentSegments && rejected && effects.VisibleSolidCount == solidCount && effects.VisibleSegmentCount == segmentCount,
            "Solid capacity rejects a whole field without partial solids or line geometry");
        for (int i = 0; i < 1000; i++) effects.Burst(WeaponAttackType.Nova, _origin, 8.5f, Colors.Cyan);
        Check(effects.ActiveCosmeticBurstCount > 0 && effects.ActiveCosmeticBurstCount <= FpsWeaponPolishEffects.MaxCosmeticBursts,
            "Standalone bursts remain within their independent pool capacity");
        effects.EndFrame();
        float time = effects.SimulationTime;
        int count = effects.ActiveTransientCount;
        int burstCount = effects.ActiveCosmeticBurstCount;
        typeof(FpsVerticalSlice).GetField("_pauseRequested", Private)!.SetValue(_map, true);
        _map._PhysicsProcess(0.5);
        Check(effects.SimulationTime == time && effects.ActiveTransientCount == count && effects.ActiveCosmeticBurstCount == burstCount && effects.VisibleSolidCount == solidCount,
            "Paused combat freezes transient/burst lifetimes and solid field geometry");
        typeof(FpsVerticalSlice).GetField("_pauseRequested", Private)!.SetValue(_map, false);
        effects.BeginFrame(20f, _origin);
        effects.EndFrame();
        Check(effects.ActiveTransientCount == 0 && effects.VisibleSegmentCount == 0 && effects.VisibleSolidCount == 0 && effects.ActiveCosmeticBurstCount == 0,
            "Expired cosmetics, bursts and old persistent geometry clean up");
        if (DisplayServer.GetName() != "headless") CheckRenderedBeam(effects, Check);
        effects.DrawClone(_origin, Vector3.Forward, .2f, 6f, Colors.Cyan);
        effects.Burst(WeaponAttackType.Nova, _origin, 8.5f, Colors.Cyan);
        effects.Enabled = false;
        effects.EndFrame();
        Check(effects.VisibleSolidCount == 0 && effects.ActiveCosmeticBurstCount == 0 && effects.VisibleSegmentCount == 0 && effects.ActiveTransientCount == 0,
            "Disabling cosmetics clears every pool immediately");
        effects.Enabled = true;
        effects.Impact(WeaponAttackType.Single, _origin, Vector3.Forward, Colors.Cyan);
        effects.DrawClone(_origin, Vector3.Forward, .2f, 6f, Colors.Cyan);
        effects.Burst(WeaponAttackType.Nova, _origin, 8.5f, Colors.Cyan);
        Invoke("ResetWeaponEffectRuntimeState");
        Check(effects.ActiveTransientCount == 0 && effects.VisibleSegmentCount == 0 && effects.VisibleSolidCount == 0 && effects.ActiveCosmeticBurstCount == 0,
            "Run reset clears all transient, persistent, solid and standalone burst state");
    }

    public static void CheckRenderedBeam(FpsWeaponPolishEffects effects, Action<bool, string> check)
    {
        // Regression from the rendered review: nonuniform scale must use each segment's
        // local axes. Scaling world rows produces stretched, disconnected wireframes.
        // The headless dummy rendering server cannot read back MultiMesh transforms.
        effects.Clear();
        Vector3 beamStart = new(60f, 1.5f, -65f);
        Vector3 beamEnd = beamStart + new Vector3(7f, 4f, -11f);
        effects.DrawBeam(beamStart, beamEnd, beamStart, 1.1f, .1f, .3f, Colors.Cyan);
        effects.EndFrame();
        MultiMesh lines = effects.GetNode<MultiMeshInstance3D>("FieldOutlines").Multimesh;
        Transform3D segment = lines.GetInstanceTransform(1);
        Vector3 endpointA = segment * new Vector3(0f, 0f, -.5f);
        Vector3 endpointB = segment * new Vector3(0f, 0f, .5f);
        check(Mathf.Min(endpointA.DistanceTo(beamEnd), endpointB.DistanceTo(beamEnd)) < .001f,
            "Rotated beam geometry ends at the actual target after nonuniform scaling");
        Vector3 nearEnd = beamStart + (beamEnd - beamStart).Normalized() * 2f;
        check(Mathf.Min(endpointA.DistanceTo(nearEnd), endpointB.DistanceTo(nearEnd)) < .001f,
            "Rotated beam joins its muzzle convergence without stretching across world axes");
        effects.Clear();
        Vector3 direction = new Vector3(7f, 4f, -11f).Normalized();
        check(effects.DrawClone(beamStart, direction, .3f, 6f, Colors.Cyan), "Rotated clone has room for its complete solid geometry");
        effects.EndFrame();
        MultiMesh solids = effects.GetNode<MultiMeshInstance3D>("FacetedSolids").Multimesh;
        bool sane = solids.VisibleInstanceCount > 0;
        for (int i = 0; i < solids.VisibleInstanceCount; i++)
        {
            Transform3D solid = solids.GetInstanceTransform(i);
            Basis pose = solid.Basis;
            sane &= solid.Origin.IsFinite() && pose.X.IsFinite() && pose.Y.IsFinite() && pose.Z.IsFinite() &&
                pose.X.LengthSquared() > 0f && pose.Y.LengthSquared() > 0f && pose.Z.LengthSquared() > 0f &&
                Mathf.Abs(pose.X.Normalized().Dot(pose.Y.Normalized())) < .001f && Mathf.Abs(pose.X.Normalized().Dot(pose.Z.Normalized())) < .001f &&
                Mathf.Abs(pose.Y.Normalized().Dot(pose.Z.Normalized())) < .001f;
        }
        check(sane, "Native rotated solids retain finite orthogonal axes after nonuniform scaling");
        effects.Clear();
        const float flameRange = 13f, flameHalfAngle = 1.5f;
        check(effects.DrawFlame(beamStart, direction, flameRange, flameHalfAngle, .1f, .3f, Colors.Cyan), "Wide rotated flame has room for its complete geometry");
        effects.EndFrame();
        bool ribsMatch = lines.VisibleInstanceCount >= 3;
        for (int i = Math.Max(0, lines.VisibleInstanceCount - 3); i < lines.VisibleInstanceCount; i++)
        {
            Transform3D rib = lines.GetInstanceTransform(i);
            Vector3 a = rib * new Vector3(0f, 0f, -.5f), b = rib * new Vector3(0f, 0f, .5f);
            Vector3 endpoint = a.DistanceSquaredTo(beamStart) > b.DistanceSquaredTo(beamStart) ? a : b;
            Vector3 delta = endpoint - beamStart;
            float angle = Mathf.Acos(Mathf.Clamp(delta.Normalized().Dot(direction), -1f, 1f));
            ribsMatch &= Mathf.Abs(delta.Length() - flameRange) < .001f && Mathf.Abs(angle - flameHalfAngle) < .001f;
        }
        check(ribsMatch, "Wide rotated flame ribs mark actual radial reach and the full gameplay half-angle");
        effects.Clear();
        effects.BeginFrame(0f, beamStart);
        effects.Echo(beamStart, beamStart + Vector3.Forward * 10f, Colors.Cyan, false);
        effects.EndFrame();
        MultiMesh traces = effects.GetNode<MultiMeshInstance3D>("FragmentsAndTrails").Multimesh;
        bool traceInitiallyVisible = Enumerable.Range(0, traces.VisibleInstanceCount).Any(i => traces.GetInstanceColor(i).A > 0f);
        effects.BeginFrame(.01f, beamStart + Vector3.Forward * 3f);
        effects.EndFrame();
        check(traceInitiallyVisible && Enumerable.Range(0, traces.VisibleInstanceCount).All(i => traces.GetInstanceColor(i).A == 0f),
            "Moving into a lingering echo suppresses its near-camera faces on the next rendered frame");
        effects.Clear();
    }

    private static EquipmentItem Item(string shape) => new()
    {
        Shape = EquipmentCatalogDefaults.CreateShapeByKey(shape), Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine"),
        Level = 5, Damage = 100, Health = 100, Substats = new() { new(SubstatType.CritChance, -100f) },
    };
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); _checks++; GD.Print($"PASS: {description}"); }
}
