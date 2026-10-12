using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    [ExportGroup("Fort Turrets")]
    [Export(PropertyHint.Range, "0,1000,1")] public int TurretUnlockPrice { get; set; } = 20;
    [Export(PropertyHint.Range, "0,1000,1")] public int TurretRepairPrice { get; set; } = 10;
    [Export(PropertyHint.Range, "1,20000,1")] public float TurretMaximumHealth { get; set; } = 250f;
    [Export(PropertyHint.Range, "10,200,1")] public float TurretRange { get; set; } = 100f;
    [Export(PropertyHint.Range, "0.1,2,0.05")] public float TurretAcquireInterval { get; set; } = .35f;
    [Export(PropertyHint.Range, "1,5,0.1")] public float TurretMuzzleHeight { get; set; } = 2.4f;
    [Export(PropertyHint.Range, "1,12,0.1")] public float TurretShapeScale { get; set; } = 5f;
    private sealed class FortTurret
    {
        public bool Unlocked, StrongestFirst;
        public EquipmentItem? Weapon;
        public float Health, Cooldown, Acquire, OutputCarry, PierceCarry;
        public int QueryCursor;
        public IFpsCombatEnemy? Target;
        public Node3D? Visual;
        public Node3D? Head;
        public Node3D? ShapeVisual;
        public MeshInstance3D? EmptyHead;
        public EquipmentItem? VisualSource;
        public Label3D? Label;
    }
    private sealed class FortTurretBullet
    {
        public Vector3 Position, Velocity;
        public float Life, Damage, Radius;
        public int Hits;
        public required EquipmentItem Source;
        public required MeshInstance3D Visual;
        public readonly HashSet<IFpsCombatEnemy> Visited = new();
    }
    private readonly FortTurret[] _fortTurrets = { new(), new(), new(), new() };
    private readonly List<FortTurretBullet> _fortTurretBullets = new();
    private readonly List<FpsHordeEnemySnapshot> _turretCandidates = new();
    private static readonly string[] TurretSides = { "North", "East", "South", "West" };
    private static readonly Vector3[] TurretPads = { new(28f,0f,-107f), new(107f,0f,28f), new(-28f,0f,107f), new(-107f,0f,-28f) };
    public EquipmentItem? GetMountedTurretShape(int side) => side is >= 0 and < 4 ? _fortTurrets[side].Weapon : null;
    public float GetTurretHealth(int side) => side is >= 0 and < 4 ? _fortTurrets[side].Health : 0f;
    private Vector3 TurretPosition(int side) => TurretPads[side] + Vector3.Up * (HillHeight + 6.096f);

    public static bool IsTurretShapeSupported(EquipmentItem weapon, out string reason)
    {
        reason = string.Empty;
        if (weapon.Shape?.AttackType != WeaponAttackType.Single) reason = "The first turret accepts Square shapes only.";
        foreach (WeaponModulatorData part in weapon.Modulators)
        {
            if (reason.Length > 0) break;
            if (part.IsWingModulator) { reason = $"Remove {part.DisplayName}: Wings require a player support slot."; break; }
            foreach (WeaponModulatorEffectData effect in part.GetResolvedEffects())
                if (effect.Trigger != ModulatorEffectTrigger.Passive || effect.Type is not
                    (ModulatorEffectType.None or ModulatorEffectType.DamageMultiplier or ModulatorEffectType.CooldownMultiplier
                    or ModulatorEffectType.AreaMultiplier or ModulatorEffectType.RangeMultiplier
                    or ModulatorEffectType.ProjectileSpeedMultiplier or ModulatorEffectType.CritChanceAdd
                    or ModulatorEffectType.CritDamageMultiplier or ModulatorEffectType.ProjectileCountAdd or ModulatorEffectType.PierceAdd))
                { reason = $"Remove {part.DisplayName}: its {effect.Type} effect is not supported by the Square turret."; break; }
        }
        return reason.Length == 0;
    }
    private bool CanManageTurrets => UsesSteppedStronghold && _shopOpen && IsDefenseShopAvailable;
    public bool TryMountTurretShape(int side, EquipmentItem weapon, out string reason)
    {
        reason = string.Empty;
        if (!CanManageTurrets || side is < 0 or > 3) reason = "Use the stronghold field shop to manage a turret.";
        else if (!_fortTurrets[side].Unlocked || _fortTurrets[side].Weapon != null) reason = "Unlock an empty turret mount first.";
        else if (!_arsenal.Contains(weapon) && !_reserveShapes.Contains(weapon)) reason = "Choose a shape owned by this run.";
        else if (_arsenal.Contains(weapon) && _arsenal.Count <= 1) reason = "Keep at least one shape equipped for the player.";
        else if (_arsenal.IndexOf(weapon) == 0 && _arsenal.Count > 1 && _arsenal[1].Modulators.Exists(part => part.IsWingModulator))
            reason = "Move a shape without Wings into the next active slot before transferring the first shape.";
        else if (!IsTurretShapeSupported(weapon, out reason)) return false;
        if (reason.Length > 0) return false;
        EquipmentItem? held = GetCurrentWeapon();
        int activeIndex = _arsenal.IndexOf(weapon);
        if (activeIndex >= 0) _arsenal.RemoveAt(activeIndex); else _reserveShapes.Remove(weapon);
        _fortTurrets[side].Weapon = weapon;
        _fortTurrets[side].Target = null;
        _fortTurrets[side].Cooldown = .1f;
        _fortTurrets[side].OutputCarry = 0f;
        _fortTurrets[side].PierceCarry = 0f;
        _fortTurrets[side].Acquire = side * .08f;
        _selectedWeaponIndex = held != null && _arsenal.Contains(held) ? _arsenal.IndexOf(held) : 0;
        _inspectionWeaponIndex = _selectedWeaponIndex;
        EquipWeapon(_selectedWeaponIndex, false);
        RebuildDefenseShopMenu();
        return true;
    }
    public bool TryReturnTurretShape(int side, out string reason)
    {
        reason = string.Empty;
        if (!CanManageTurrets || side is < 0 or > 3) reason = "Use the stronghold field shop to manage a turret.";
        else if (_fortTurrets[side].Weapon == null) reason = "This mount is empty.";
        else if (_arsenal.Count + _reserveShapes.Count >= EquippedShapeCapacity + ReserveShapeCapacity)
            reason = "Shape storage is full. Drop a shape in Tab inventory before returning the turret shape.";
        if (reason.Length > 0) return false;
        FortTurret turret = _fortTurrets[side];
        EquipmentItem weapon = turret.Weapon!;
        turret.Weapon = null;
        if (!TryStoreRunShape(weapon, out reason)) { turret.Weapon = weapon; return false; }
        turret.Target = null;
        RebuildDefenseShopMenu();
        return true;
    }
    private void BuildFortTurretShop()
    {
        if (!UsesSteppedStronghold) return;
        AddShopLabel("FORT TURRETS / four balcony mounts", 22);
        AddShopLabel("Mount an owned Square with its installed parts. Supports damage, cadence, range, size, speed, critical hits, up to 8 bullets and 9 piercing hits. Remove unsupported installed parts in Tab first. Player-only base stats do not affect turrets.", 15);
        AddShopLabel("Each mount covers its cardinal front. Nearby enemies damage it; disabled mounts keep their shape and parts. Return equipment to free inventory space before editing its parts.", 15);
        for (int side = 0; side < 4; side++)
        {
            int index = side; FortTurret turret = _fortTurrets[index];
            AddShopLabel($"{TurretSides[index]} / {(turret.Unlocked ? $"HP {turret.Health:0}/{TurretMaximumHealth:0} / {turret.Weapon?.Shape?.DisplayName ?? "empty"}" : "locked")}", 18);
            if (!turret.Unlocked)
            {
                AddShopButton($"Unlock {TurretSides[index]} mount / {TurretUnlockPrice} coins", () => CanManageTurrets && !turret.Unlocked && _defenseCoins >= TurretUnlockPrice,
                    () => { if (!TrySpendDefenseCoins(TurretUnlockPrice)) return; turret.Unlocked = true; turret.Health = TurretMaximumHealth; RebuildDefenseShopMenu(); });
                continue;
            }
            AddShopButton($"Repair mount to full / {TurretRepairPrice} coins", () => CanManageTurrets && turret.Health < TurretMaximumHealth && _defenseCoins >= TurretRepairPrice,
                () => { if (!TrySpendDefenseCoins(TurretRepairPrice)) return; turret.Health = TurretMaximumHealth; RebuildDefenseShopMenu(); });
            if (turret.Weapon == null)
                foreach (EquipmentItem shape in GetInventoryShapes())
                {
                    if (shape.Shape?.AttackType != WeaponAttackType.Single) continue;
                    bool supported = IsTurretShapeSupported(shape, out string why);
                    AddShopButton(supported ? $"Mount {shape.Shape.DisplayName} Lv{shape.Level} / {shape.Modulators.Count} installed parts" : why,
                        () => CanManageTurrets && turret.Weapon == null && supported && (!_arsenal.Contains(shape) || _arsenal.Count > 1),
                        () => { if (!TryMountTurretShape(index, shape, out string error)) ShowNotice(error, 3f); });
                }
            else
            {
                AddShopButton($"Priority: {(turret.StrongestFirst ? "Toughest nearby target" : "Nearby targets")}", () => CanManageTurrets,
                    () => { turret.StrongestFirst = !turret.StrongestFirst; turret.Target = null; RebuildDefenseShopMenu(); });
                AddShopButton("Return exact shape and all installed parts to inventory", () => CanManageTurrets,
                    () => { if (!TryReturnTurretShape(index, out string error)) ShowNotice(error, 3f); });
                AddShopButton($"Upgrade mounted shape / {DefenseSettings!.WeaponUpgradePrice} coins", () => CanManageTurrets && turret.Weapon!.Level < EquipmentCatalogDefaults.MaxWeaponLevel && _defenseCoins >= DefenseSettings!.WeaponUpgradePrice,
                    () => { if (!TrySpendDefenseCoins(DefenseSettings!.WeaponUpgradePrice)) return; turret.Weapon!.Level++; StatGenerator.RecalculateDerivedStats(turret.Weapon); RebuildDefenseShopMenu(); });
            }
        }
    }
    private void ResetFortTurrets()
    {
        foreach (FortTurretBullet bullet in _fortTurretBullets) bullet.Visual.QueueFree();
        _fortTurretBullets.Clear();
        foreach (FortTurret turret in _fortTurrets)
        {
            turret.Visual?.QueueFree(); turret.Visual = null; turret.Head = null; turret.ShapeVisual = null; turret.EmptyHead = null;
            turret.VisualSource = null; turret.Label = null; turret.Weapon = null; turret.Unlocked = false;
            turret.Target = null; turret.Health = 0f; turret.Cooldown = 0f; turret.Acquire = 0f;
            turret.QueryCursor = 0; turret.OutputCarry = 0f; turret.PierceCarry = 0f; turret.StrongestFirst = false;
        }
    }
    private void EnsureTurretVisual(int side)
    {
        FortTurret turret = _fortTurrets[side];
        if (turret.Visual != null && IsInstanceValid(turret.Visual)) return;
        turret.VisualSource = null; turret.ShapeVisual = null;
        turret.Visual = new Node3D { Name = $"{TurretSides[side]}ShapeTurret" }; AddChild(turret.Visual);
        StandardMaterial3D baseMaterial = new() { AlbedoColor = new Color(.12f,.2f,.24f), Metallic = .55f };
        turret.Visual.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.8f,.8f,1.8f) }, MaterialOverride = baseMaterial, Position = Vector3.Up * .4f });
        turret.Visual.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .35f, BottomRadius = .55f, Height = 1.8f }, MaterialOverride = baseMaterial, Position = Vector3.Up * 1.3f });
        turret.Head = new Node3D { Position = Vector3.Up * TurretMuzzleHeight }; turret.Visual.AddChild(turret.Head);
        turret.EmptyHead = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.9f,.8f,1.4f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.12f,.75f,.9f), EmissionEnabled = true, Emission = new Color(.05f,.35f,.4f) } };
        turret.Head.AddChild(turret.EmptyHead);
        turret.Label = new Label3D { Position = Vector3.Up * 3.5f, FontSize = 38, PixelSize = .01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }; turret.Visual.AddChild(turret.Label);
    }
    private static int TurretSector(Vector3 p) => Mathf.Abs(p.X) > Mathf.Abs(p.Z) ? (p.X >= 0f ? 1 : 3) : (p.Z >= 0f ? 2 : 0);
    private void RefreshFortTurretVisuals()
    {
        if (!UsesSteppedStronghold)
        {
            foreach (FortTurret turret in _fortTurrets) if (turret.Visual != null && IsInstanceValid(turret.Visual)) turret.Visual.Visible = false;
            foreach (FortTurretBullet bullet in _fortTurretBullets) bullet.Visual.QueueFree();
            _fortTurretBullets.Clear();
            return;
        }
        for (int side = 0; side < 4; side++)
        {
            FortTurret turret = _fortTurrets[side]; if (!turret.Unlocked) continue;
            EnsureTurretVisual(side);
            turret.Visual!.Visible = true;
            turret.EmptyHead!.Visible = turret.Weapon == null;
            if (!ReferenceEquals(turret.VisualSource, turret.Weapon))
            {
                turret.ShapeVisual?.QueueFree(); turret.ShapeVisual = null;
                turret.VisualSource = turret.Weapon;
                if (turret.Weapon != null)
                {
                    turret.ShapeVisual = FpsWeaponVisualFactory.Create(turret.Weapon);
                    turret.Head!.AddChild(turret.ShapeVisual);
                }
            }
            if (turret.ShapeVisual != null) turret.ShapeVisual.Scale = Vector3.One * TurretShapeScale;
            turret.Visual!.Position = TurretPosition(side);
            turret.Label!.Text = $"{TurretSides[side]} {turret.Health:0} HP\n{(turret.Health <= 0f ? "DISABLED / repair at shop" : turret.Weapon == null ? "EMPTY / mount at shop" : $"Square Lv{turret.Weapon.Level}")}";
            turret.Head!.Position = Vector3.Up * TurretMuzzleHeight;
        }
    }
    private void TickFortTurrets(float dt)
    {
        RefreshFortTurretVisuals();
        if (!UsesSteppedStronghold) return;
        for (int side=0;side<4;side++)
        {
            FortTurret turret = _fortTurrets[side]; if (!turret.Unlocked) continue;
            if (turret.Health <= 0f) continue;
            turret.Acquire -= dt; turret.Cooldown -= dt;
            Vector3 origin = TurretPosition(side) + Vector3.Up * TurretMuzzleHeight;
            float range = Mathf.Clamp(TurretRange * GetFpsRangeScale(turret.Weapon), 10f, 200f);
            if (turret.Acquire <= 0f)
            {
                turret.Acquire = Mathf.Max(.1f, TurretAcquireInterval);
                // Contact damage uses its own bounded near query so distant candidates cannot mask attackers.
                int nearCursor = 0;
                HordeManager?.QueryRadiusBounded(origin, 4f, _turretCandidates, ref nearCursor, 128, 32);
                foreach (FpsHordeEnemySnapshot near in _turretCandidates)
                    if (near.HeadPosition.DistanceSquaredTo(origin) < 25f) turret.Health = Mathf.Max(0f, turret.Health - 8f * turret.Acquire);
                if (turret.Weapon == null || turret.Health <= 0f) continue;
                HordeManager?.QueryRadiusBounded(origin, range, _turretCandidates, ref turret.QueryCursor);
                IFpsCombatEnemy? best = turret.Target is { IsAlive: true } current && TurretSector(current.GlobalPosition) == side && current.HeadPosition.DistanceSquaredTo(origin) <= range * range && HasLineOfSight(origin, current) ? current : null;
                float score = best == null ? float.NegativeInfinity : turret.StrongestFirst ? best.CurrentHealth : -best.HeadPosition.DistanceSquaredTo(origin);
                int visibilityChecks = 0;
                foreach (FpsHordeEnemySnapshot snapshot in _turretCandidates)
                {
                    if (TurretSector(snapshot.Position) != side || !_hordeTargets.TryGetValue(snapshot.Handle, out FpsHordeCombatEnemy? enemy) || !enemy.IsAlive) continue;
                    float candidateScore = turret.StrongestFirst ? snapshot.CurrentHealth : -snapshot.HeadPosition.DistanceSquaredTo(origin);
                    if (candidateScore <= score || snapshot.HeadPosition.DistanceSquaredTo(origin) > range * range) continue;
                    if (++visibilityChecks > 12) break;
                    if (!HasLineOfSight(origin, enemy)) continue;
                    best = enemy; score = candidateScore;
                }
                turret.Target = best;
            }
            if (turret.Weapon == null || turret.Health <= 0f || turret.Target is not { IsAlive: true } target) continue;
            Vector3 direction = (target.HeadPosition - origin).Normalized();
            turret.Head!.LookAt(origin + direction);
            if (turret.Cooldown > 0f) continue;
            EquipmentItem weapon = turret.Weapon;
            float speed = Mathf.Clamp(SquareProjectileSpeed * WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon), 1f, 1200f);
            float cadence = Mathf.Max(.1f, 1f + StatGenerator.GetTotalSubstatValue(weapon, SubstatType.AttackSpeed) / 100f);
            turret.Cooldown = Mathf.Max(.08f, GetWeaponRuntimeCooldown(weapon) / cadence);
            int count = 1 + TurretCount(ref turret.OutputCarry, StatGenerator.GetTotalSubstatValue(weapon, SubstatType.ProjectileCount), 7);
            int hits = 1 + TurretCount(ref turret.PierceCarry, WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon), 8);
            for (int shot = 0; shot < count && _fortTurretBullets.Count < 96; shot++)
            {
                Vector3 spread = direction.Rotated(Vector3.Up, (shot - (count - 1) * .5f) * .025f);
                MeshInstance3D visual = new() { Mesh = new BoxMesh { Size = new Vector3(.12f,.12f,.36f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.1f,.85f,1f), EmissionEnabled = true, Emission = new Color(.1f,.85f,1f) } };
                AddChild(visual); visual.GlobalPosition = origin;
                _fortTurretBullets.Add(new() { Position = origin, Velocity = spread * speed, Life = range / speed, Damage = GetWeaponBaseDamage(weapon), Radius = .04f * GetFpsAreaScale(weapon), Hits = hits, Source = weapon, Visual = visual });
            }
        }
        TickFortTurretBullets(dt);
    }
    private static int TurretCount(ref float carry, float value, int max)
    { float total = carry + Mathf.Clamp(value,0f,max); int count = Mathf.FloorToInt(total); carry = total - count; return count; }
    private void TickFortTurretBullets(float dt)
    {
        for (int index = _fortTurretBullets.Count - 1; index >= 0; index--)
        {
            FortTurretBullet bullet = _fortTurretBullets[index];
            Vector3 end = bullet.Position + bullet.Velocity * Mathf.Min(dt,bullet.Life);
            bool blocked = TryRaycastWorldBlock(bullet.Position,end,out Vector3 wall); if (blocked) end = wall;
            foreach (IFpsCombatEnemy enemy in CollectEnemiesAlongSegment(bullet.Position,end,bullet.Radius,bullet.Visited))
            {
                bullet.Visited.Add(enemy);
                float damage = bullet.Damage;
                if (_weaponRng.Randf() * 100f < WeaponModulatorSharedRuntime.GetWeaponItemCritChancePercent(bullet.Source))
                    damage *= 1.5f + WeaponModulatorSharedRuntime.GetWeaponItemCritDamagePercent(bullet.Source) / 100f;
                // This ownership path deliberately cannot heal the player, fire Wings or schedule player procs.
                enemy.ApplyDamageFrom(damage,bullet.Velocity);
                if (--bullet.Hits <= 0) break;
            }
            bullet.Life -= dt; bullet.Position = end; bullet.Visual.GlobalPosition = end;
            if (bullet.Life > 0f && bullet.Hits > 0 && !blocked) continue;
            bullet.Visual.QueueFree(); _fortTurretBullets.RemoveAt(index);
        }
    }
}
