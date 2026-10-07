using Godot;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild;

public partial class Main
{
    private void DamageTargetsInRadius(Vector2 center, float radius, float damage, bool critical = false, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float hitRadius = radius + enemy.Radius;
            if ((enemy.Position - center).LengthSquared() <= hitRadius * hitRadius)
            {
                DealDamageToEnemy(enemy, damage, center, false, critical, true, sourceItem, triggerModulatorOnHitEffects);
            }
        }

        if (_boss != null && _boss.IsAlive)
        {
            float hitRadius = radius + _boss.Radius;
            if ((_boss.Position - center).LengthSquared() <= hitRadius * hitRadius)
            {
                DealDamageToBoss(damage, critical, true, sourceItem, triggerModulatorOnHitEffects);
            }
        }
    }

    private void DamageTargetsAlongSegment(Vector2 start, Vector2 end, float halfWidth, float damage, bool critical = false, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            if (DistancePointToSegment(enemy.Position, start, end) <= halfWidth + enemy.Radius)
            {
                DealDamageToEnemy(enemy, damage, enemy.Position, false, critical, true, sourceItem, triggerModulatorOnHitEffects);
            }
        }

        if (_boss != null && _boss.IsAlive && DistancePointToSegment(_boss.Position, start, end) <= halfWidth + _boss.Radius)
        {
            DealDamageToBoss(damage, critical, true, sourceItem, triggerModulatorOnHitEffects);
        }
    }

    private void DamageTargetsInCone(Vector2 origin, Vector2 direction, float range, float halfAngle, float damage, bool critical = false, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        float cosLimit = Mathf.Cos(halfAngle);
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            Vector2 toEnemy = enemy.Position - origin;
            float distance = toEnemy.Length();
            if (distance > range + enemy.Radius || distance <= 0.001f)
            {
                continue;
            }

            if (direction.Dot(toEnemy / distance) >= cosLimit)
            {
                DealDamageToEnemy(enemy, damage, enemy.Position, false, critical, true, sourceItem, triggerModulatorOnHitEffects);
            }
        }

        if (_boss != null && _boss.IsAlive)
        {
            Vector2 toBoss = _boss.Position - origin;
            float distance = toBoss.Length();
            if (distance <= range + _boss.Radius && distance > 0.001f && direction.Dot(toBoss / distance) >= cosLimit)
            {
                DealDamageToBoss(damage, critical, true, sourceItem, triggerModulatorOnHitEffects);
            }
        }
    }

    private void PullEnemiesToward(Vector2 center, float radius, float force, float delta)
    {
        float radiusSq = radius * radius;
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            Vector2 toEnemy = enemy.Position - center;
            float distanceSq = toEnemy.LengthSquared();
            if (distanceSq > radiusSq || distanceSq < 1f)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSq);
            Vector2 pullDirection = -toEnemy / distance;
            float pullStrength = force * (1f - distance / radius);
            enemy.Position = ClampResolve(enemy.Position + pullDirection * pullStrength * delta, enemy.Radius);
        }
    }

    private void DealDamageToEnemy(EnemyActor enemy, float damage, Vector2? sourcePosition = null, bool triggerEquipmentModifiers = true, bool critical = false, bool triggerVolitileExplosion = true, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        if (damage <= 0f || !enemy.IsAlive)
        {
            return;
        }

        damage = ApplyModulatorEnemyDamageMultipliers(enemy, damage, sourceItem);
        enemy.TakeDamage(damage);
        SpawnEnemyDamageNumber(enemy, damage, critical);
        ApplyEquipmentLifeSteal(damage);
        ApplyArenaDebugLifeSteal(damage);
        if (triggerVolitileExplosion)
        {
            TriggerVolitileExplosion(enemy.Position, damage, enemy, critical);
        }
        if (triggerEquipmentModifiers)
        {
            ApplyEquipmentOnEnemyHit(enemy, damage, sourcePosition ?? enemy.Position);
        }

        if (triggerModulatorOnHitEffects)
        {
            ApplyModulatorOnEnemyHitEffects(enemy, damage, sourcePosition ?? enemy.Position, sourceItem, critical);
        }

        if (sourceItem != null)
        {
            ApplyModulatorKnockback(enemy, sourcePosition ?? enemy.Position, sourceItem);
            TryTriggerModulatorCriticalCascade(enemy, sourcePosition ?? enemy.Position, damage, critical, sourceItem);
            TryTriggerOmniFeedback(enemy.Position, damage, critical, sourceItem);
        }
    }

    private void DealDamageToBoss(float damage, bool critical = false, bool triggerVolitileExplosion = true, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
    {
        if (damage <= 0f || _boss == null || !_boss.IsAlive)
        {
            return;
        }

        damage = ApplyModulatorBossDamageMultipliers(damage, sourceItem);
        _boss.TakeDamage(damage);
        SpawnBossDamageNumber(_boss, damage, critical);
        ApplyEquipmentLifeSteal(damage);
        ApplyArenaDebugLifeSteal(damage);
        if (triggerVolitileExplosion)
        {
            TriggerVolitileExplosion(_boss.Position, damage, null, critical, includeBoss: true);
        }

        if (triggerModulatorOnHitEffects)
        {
            ApplyModulatorOnBossHitEffects(damage, _boss.Position, sourceItem, critical);
        }

        if (sourceItem != null)
        {
            TryTriggerModulatorCriticalCascade(null, _boss.Position, damage, critical, sourceItem);
            TryTriggerOmniFeedback(_boss.Position, damage, critical, sourceItem);
        }
    }

    private void ApplyModulatorKnockback(EnemyActor enemy, Vector2 sourcePosition, EquipmentItem sourceItem)
    {
        float knockbackMultiplier = GetWeaponItemKnockbackMultiplier(sourceItem);
        if (knockbackMultiplier <= 1.001f)
        {
            return;
        }

        Vector2 pushDirection = RuntimeMath.SafeDirection(enemy.Position - sourcePosition, enemy.Position - _player.Position);
        float pushDistance = 1.4f * (knockbackMultiplier - 1f);
        enemy.Position = ClampResolve(enemy.Position + pushDirection * pushDistance, enemy.Radius);
    }

    private void TryTriggerModulatorCriticalCascade(EnemyActor? originEnemy, Vector2 originPosition, float damage, bool critical, EquipmentItem sourceItem)
    {
        if (!critical || _modulatorCritCascadeCooldown > 0f || !Combat.WeaponModulatorSharedRuntime.TryGetOnCritChainProc(sourceItem, out Combat.WeaponModulatorSharedRuntime.OnCritChainProcConfig procConfig))
        {
            return;
        }

        _modulatorCritCascadeCooldown = procConfig.CooldownSeconds;
        SpawnLegacyChainLightning(
            originPosition,
            damage * procConfig.DamageScale,
            procConfig.ArcCount,
            GetWeaponShotColor(MapEquipmentShapeToWeapon(sourceItem.Shape?.Key), MapEquipmentRarity(sourceItem.Rarity?.Key)),
            true,
            rangeOverride: procConfig.Range,
            sourceItem: sourceItem);
    }

    private void TryTriggerOmniFeedback(Vector2 originPosition, float damage, bool critical, EquipmentItem sourceItem)
    {
        if (!Combat.WeaponModulatorSharedRuntime.TryGetOmniFeedbackConfig(sourceItem, out Combat.WeaponModulatorSharedRuntime.OmniFeedbackConfig omniConfig))
        {
            return;
        }

        _omniFeedbackHitCounter++;
        if (_omniFeedbackHitCounter < omniConfig.HitCadence)
        {
            return;
        }

        _omniFeedbackHitCounter = 0;
        EnemyActor? spectralTarget = FindNearestEnemyActor(originPosition, omniConfig.SearchRange);
        if (spectralTarget != null)
        {
            DealDamageToEnemy(spectralTarget, damage * omniConfig.DamageScale, originPosition, false, critical, false, sourceItem, false);
            SpawnVisualPulse(spectralTarget.Position, 0.8f, 3.4f, new Color(0.56f, 0.84f, 1f, 0.34f), 0.22f);
            return;
        }

        if (_boss != null && _boss.IsAlive && (_boss.Position - originPosition).Length() <= omniConfig.SearchRange)
        {
            DealDamageToBoss(damage * omniConfig.DamageScale, critical, false, sourceItem, false);
            SpawnVisualPulse(_boss.Position, 0.8f, 3.8f, new Color(0.56f, 0.84f, 1f, 0.34f), 0.22f);
        }
    }

    private void SpawnVisualPulse(Vector2 position, float startRadius, float endRadius, Color color, float duration)
    {
        MeshInstance3D mesh = CreateDiskVisual(position, startRadius, color, Mathf.Clamp(color.A, 0.16f, 0.45f), 0.06f);
        _legacyVisualPulses.Add(new VisualPulseEffect
        {
            Position = position,
            StartRadius = startRadius,
            EndRadius = endRadius,
            Duration = duration,
            Color = new Color(color.R, color.G, color.B, Mathf.Clamp(color.A, 0.16f, 0.45f)),
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private MeshInstance3D CreateDiskVisual(Vector2 position, float radius, Color color, float alpha, float height)
    {
        position = RuntimeMath.FiniteOr(position, Vector2.Zero);
        radius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, 1f), 0.01f, 250f);
        height = Mathf.Max(0.01f, RuntimeMath.FiniteOr(height, 0.1f));
        MeshInstance3D mesh = new()
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 1f,
                BottomRadius = 1f,
                Height = height,
                RadialSegments = 48,
            },
            MaterialOverride = CreateLegacyMaterial(color, alpha, 1.8f),
            Position = new Vector3(position.X, 0.12f, position.Y),
            Scale = new Vector3(radius, 1f, radius),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        return mesh;
    }

    private MeshInstance3D CreateBoxVisual(Vector2 position, Vector3 size, Color color, float alpha, float height)
    {
        position = RuntimeMath.FiniteOr(position, Vector2.Zero);
        size = RuntimeMath.FiniteOr(size, Vector3.One);
        size = new Vector3(
            Mathf.Clamp(Mathf.Abs(size.X), 0.01f, 300f),
            Mathf.Clamp(Mathf.Abs(size.Y), 0.01f, 300f),
            Mathf.Clamp(Mathf.Abs(size.Z), 0.01f, 300f));
        height = Mathf.Max(0.01f, RuntimeMath.FiniteOr(height, 0.1f));
        MeshInstance3D mesh = new()
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, height, size.Z) },
            MaterialOverride = CreateLegacyMaterial(color, alpha, 1.6f),
            Position = new Vector3(position.X, 0.2f, position.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        return mesh;
    }

    private MeshInstance3D CreateCubeVisual(Vector2 position, float size, Color color, float alpha)
    {
        position = RuntimeMath.FiniteOr(position, Vector2.Zero);
        size = Mathf.Clamp(RuntimeMath.FiniteOr(size, 1f), 0.01f, 80f);
        MeshInstance3D mesh = new()
        {
            Mesh = new BoxMesh { Size = new Vector3(size, size, size) },
            MaterialOverride = CreateLegacyMaterial(color, alpha, 1.4f),
            Position = new Vector3(position.X, 1.6f, position.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(mesh);
        return mesh;
    }

    private MeshInstance3D CreateSphereVisual(Vector2 position, float radius, Color color, float alpha, float emission)
    {
        position = RuntimeMath.FiniteOr(position, Vector2.Zero);
        radius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, 1f), 0.01f, 80f);
        MeshInstance3D mesh = new()
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            MaterialOverride = CreateLegacyMaterial(color, alpha, emission),
            Position = new Vector3(position.X, 1.2f, position.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        return mesh;
    }

    private MeshInstance3D CreateDrillVisual(Vector2 position, Color color)
    {
        position = RuntimeMath.FiniteOr(position, Vector2.Zero);
        MeshInstance3D mesh = new()
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.05f,
                BottomRadius = 0.8f,
                Height = 2.2f,
                RadialSegments = 8,
            },
            MaterialOverride = CreateLegacyMaterial(color, 0.95f, 2.1f),
            Position = new Vector3(position.X, 2.2f, position.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(mesh);
        return mesh;
    }

    private static StandardMaterial3D CreateLegacyMaterial(Color color, float alpha, float emissionEnergy)
    {
        Color albedo = new(color.R, color.G, color.B, alpha);
        return new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(color.R, color.G, color.B, 1f),
            EmissionEnergyMultiplier = emissionEnergy,
            Metallic = 0.05f,
            Roughness = 0.3f,
        };
    }

    private static void SetMaterialAlpha(StandardMaterial3D material, float alpha)
    {
        Color color = material.AlbedoColor;
        material.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(alpha, 0f, 1f));
    }

    private void QueueFreeIfValid(Node? node)
    {
        if (node != null && IsInstanceValid(node))
        {
            node.QueueFree();
        }
    }
}