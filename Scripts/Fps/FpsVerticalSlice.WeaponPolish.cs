using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    [ExportGroup("Weapon Effects")]
    [Export] public bool WeaponPolishEnabled { get; set; } = true;
    [Export] public bool WeaponSoundsEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,1,0.05")] public float WeaponEffectDetail { get; set; } = 1f;
    [Export] public FpsExplosionAudioSettings? ExplosionAudioSettings { get; set; }
    public FpsWeaponPolishEffects? WeaponEffects => _weaponPolish;
    private FpsWeaponPolishEffects? _weaponPolish;
    private FpsWeaponPolishAudio? _weaponPolishAudio;
    private int _explosionAudioDepth;

    private void EnsureWeaponPolishRuntime()
    {
        if (_weaponPolish == null || !IsInstanceValid(_weaponPolish))
        {
            _weaponPolish = new FpsWeaponPolishEffects { Name = "WeaponPolish", Enabled = WeaponPolishEnabled };
            AddChild(_weaponPolish);
        }
        if (_weaponPolishAudio == null || !IsInstanceValid(_weaponPolishAudio))
        {
            _weaponPolishAudio = new FpsWeaponPolishAudio
            {
                Name = "WeaponSound", Enabled = WeaponSoundsEnabled,
                ExplosionSettings = ExplosionAudioSettings
                    ?? GD.Load<FpsExplosionAudioSettings>("res://Resources/Audio/Explosions/DefaultExplosionAudio.tres")
                    ?? new FpsExplosionAudioSettings(),
            };
            AddChild(_weaponPolishAudio);
        }
    }

    private void UpdateWeaponPolishState()
    {
        if (_weaponPolish != null)
        {
            _weaponPolish.Enabled = WeaponPolishEnabled;
            _weaponPolish.Detail = WeaponEffectDetail;
        }
        if (_weaponPolishAudio != null)
        {
            _weaponPolishAudio.Enabled = WeaponSoundsEnabled;
            _weaponPolishAudio.SetPaused(!IsCombatRunning());
        }
    }

    private static Color PolishRarity(EquipmentItem? item) => item?.Rarity?.UiColor ?? Colors.White;

    private Vector3 ResolvePolishMuzzle(Vector3 origin)
    {
        // Wings and delayed echoes keep their own world origin. Muzzle parallax is cosmetic.
        if (_player == null || origin.DistanceSquaredTo(_player.HeadPosition) > .04f ||
            !_player.TryGetWeaponMuzzle(out Vector3 muzzle)) return origin;
        return TryRaycastWorldBlock(origin, muzzle, out _) ? origin : muzzle;
    }

    private void UpdatePolishedProjectile(PlayerProjectileEffect projectile, float dt)
    {
        projectile.VisualAge += dt;
        Vector3 after = projectile.Position + projectile.VisualOffset * Mathf.Max(0f, 1f - projectile.VisualAge * 20f);
        projectile.Mesh.GlobalPosition = after;
        Basis basis = FpsWeaponPolishEffects.Facing(projectile.Direction);
        if (projectile.Kind == PlayerProjectileKind.Sawblade)
            basis *= new Basis(Vector3.Right, Mathf.Pi * .43f) * new Basis(Vector3.Back, projectile.VisualAge * 30f);
        else if (projectile.Kind == PlayerProjectileKind.Drill)
            basis *= new Basis(Vector3.Back, projectile.VisualAge * 24f);
        else if (projectile.Kind == PlayerProjectileKind.Homing)
            basis *= new Basis(Vector3.Back, Mathf.Sin(projectile.VisualAge * 8f) * .16f);
        float formation = projectile.Kind switch
        {
            PlayerProjectileKind.Sawblade => Mathf.Lerp(.4f, 1f, Mathf.Clamp(projectile.VisualAge / .10f, 0f, 1f)),
            PlayerProjectileKind.Homing or PlayerProjectileKind.Drill => Mathf.Lerp(.08f, 1f, Mathf.Clamp(projectile.VisualAge / .11f, 0f, 1f)),
            _ => 1f,
        };
        projectile.Mesh.Basis = basis.Scaled(Vector3.One * GetFpsAreaScale(projectile.SourceItem) * formation);
    }

    private Vector3 ProjectileVisualOffset(PlayerProjectileKind kind, Vector3 origin, Vector3 direction)
    {
        Vector3 shotOrigin = kind switch
        {
            PlayerProjectileKind.Homing => origin - direction * .9f,
            PlayerProjectileKind.Drill => origin - direction,
            _ => origin,
        };
        if (kind == PlayerProjectileKind.Sawblade) return Vector3.Zero;
        Vector3 muzzle = ResolvePolishMuzzle(shotOrigin);
        return muzzle.IsEqualApprox(shotOrigin) ? Vector3.Zero : muzzle - origin;
    }

    private void ShowPolishedWorldHit(PlayerProjectileEffect projectile, Vector3 start, Vector3 hit, Vector3 normal)
    {
        WeaponAttackType type = ProjectilePolishType(projectile.Kind);
        Color rarity = PolishRarity(projectile.SourceItem);
        _weaponPolish?.ProjectileTrail(type, start, hit, rarity, projectile.Radius);
        _weaponPolish?.Impact(type, hit + normal * .025f, normal, rarity, 1f, true);
        _weaponPolishAudio?.PlayImpact(type, hit, .7f);
    }

    private static WeaponAttackType ProjectilePolishType(PlayerProjectileKind kind) => kind switch
    {
        PlayerProjectileKind.Bullet => WeaponAttackType.Single,
        PlayerProjectileKind.Sawblade => WeaponAttackType.Sawblade,
        PlayerProjectileKind.Homing => WeaponAttackType.Homing,
        _ => WeaponAttackType.Drill,
    };

    private void SpawnPolishedPulse(WeaponAttackType type, Vector3 position, float radius, float duration,
        Color fallbackColor, EquipmentItem? source)
    {
        // Keep the original zero-damage pulse record: transforms can inspect or contract it.
        int previousCount = _playerZones.Count;
        SpawnPulse(position, radius, duration, fallbackColor);
        if (_playerZones.Count == previousCount) return;
        ZoneEffect pulse = _playerZones[^1];
        pulse.PolishType = type;
        pulse.PolishRarity = PolishRarity(source);
    }

    private void ShowPolishedEnemyHit(EquipmentItem? weapon, Vector3 hit, Vector3 direction, float damage, float healthBefore, bool critical)
    {
        if (weapon?.Shape == null || !FpsWeaponPolishEffects.Supports(weapon.Shape.AttackType)) return;
        float overkill = Mathf.Max(0f, damage - healthBefore) / Mathf.Max(1f, healthBefore);
        float intensity = Mathf.Min(2.4f, 1f + overkill * .45f + (critical ? .2f : 0f));
        _weaponPolish?.Impact(weapon.Shape.AttackType, hit, direction, PolishRarity(weapon), intensity);
        // A blast is one spatial sound, independent of how many bodies it hits.
        // Its visual impacts and weapon effects still execute for every victim.
        if (_explosionAudioDepth == 0) _weaponPolishAudio?.PlayImpact(weapon.Shape.AttackType, hit, intensity);
    }

    private int DamageEnemiesInExplosion(Vector3 center, float radius, float damage,
        EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        // Call with the final gameplay radius after area/range modifiers. Fields,
        // aura ticks and electrical chains remain ordinary radius/segment damage.
        _weaponPolishAudio?.PlayExplosion(center, radius);
        _explosionAudioDepth++;
        try { return DamageEnemiesInRadius(center, radius, damage, sourceItem, triggerModulatorOnHitEffects); }
        finally { _explosionAudioDepth--; }
    }

    private bool DrawPolishedZone(ZoneEffect zone)
    {
        if (_weaponPolish == null) return false;
        if (zone.Kind == ZoneEffectKind.Pulse && zone.PolishType.HasValue)
        {
            return _weaponPolish.DrawBurst(zone.PolishType.Value, zone.Position, zone.Radius, zone.Age, zone.Duration,
                zone.PolishRarity, GetFpsSpecialObjectState(zone).Contracting);
        }
        if (zone.SourceItem?.Shape == null) return false;
        // Render the actual attack pattern even when a clone retains a different source identity.
        if (zone.Kind == ZoneEffectKind.Beam)
            return _weaponPolish.DrawBeam(zone.Position, zone.Position + zone.Direction * zone.Range,
                zone.VisualOrigin, zone.Radius, zone.Age, zone.Duration, PolishRarity(zone.SourceItem));
        if (zone.Kind == ZoneEffectKind.Flame)
            return _weaponPolish.DrawFlame(zone.Position, zone.Direction, zone.Range, Mathf.Clamp(zone.Radius, .04f, 1.5f),
                zone.Age, zone.Duration, PolishRarity(zone.SourceItem));
        if (zone.Kind == ZoneEffectKind.Void)
            return _weaponPolish.DrawVoid(zone.Position, zone.Radius, zone.Age, zone.Duration, PolishRarity(zone.SourceItem));
        if (zone.Kind == ZoneEffectKind.Rift && zone.SourceItem.Shape.AttackType is WeaponAttackType.Drill or WeaponAttackType.Mortar)
            return _weaponPolish.DrawResidue(zone.SourceItem.Shape.AttackType, zone.Position, zone.Radius,
                zone.Age, zone.Duration, PolishRarity(zone.SourceItem));
        if (zone.Kind == ZoneEffectKind.Rift && zone.SourceItem.Shape.AttackType is WeaponAttackType.Rift or WeaponAttackType.ShadowClone)
            return _weaponPolish.DrawRift(zone.Position, zone.Radius, zone.Age, zone.Duration,
                zone.Tick, zone.TickInterval, PolishRarity(zone.SourceItem));
        if (zone.Kind == ZoneEffectKind.Rift && zone.SourceItem.Shape.AttackType == WeaponAttackType.FlameCone)
            return _weaponPolish.DrawFlame(zone.Position, Vector3.Up, Mathf.Max(.5f, zone.Radius), .65f,
                zone.Age, zone.Duration, PolishRarity(zone.SourceItem));
        return false;
    }
}
