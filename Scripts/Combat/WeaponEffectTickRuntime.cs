using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild;

public partial class Main
{
    private void TickLegacyWeaponEffects(float delta)
    {
        _modulatorCritCascadeCooldown = Mathf.Max(0f, _modulatorCritCascadeCooldown - delta);
        TickLegacyHomingBullets(delta);
        TickLegacyBeams(delta);
        TickLegacyMines(delta);
        TickLegacyNovas(delta);
        TickLegacyFlames(delta);
        TickLegacyOrbitals(delta);
        TickLegacyRifts(delta);
        TickLegacyMortars(delta);
        TickLegacyChains(delta);
        TickLegacyDrills(delta);
        TickLegacyVoids(delta);
        TickLegacyWrathAura(delta);
        TickLegacyClones(delta);
        TickLegacyEchoes(delta);
        TickModulatorEffectState(delta);
        TickLegacyVisualPulses(delta);
    }

    private void TickLegacyHomingBullets(float delta)
    {
        List<BulletActor>? removeList = null;
        foreach (KeyValuePair<BulletActor, HomingBulletState> pair in _legacyHomingBullets)
        {
            BulletActor bullet = pair.Key;
            if (!IsInstanceValid(bullet) || !bullet.Active)
            {
                removeList ??= new List<BulletActor>();
                removeList.Add(bullet);
                continue;
            }

            if (!TryFindNearestTarget(bullet.Position, 160f, out Vector2 targetPosition))
            {
                continue;
            }

            Vector2 desired = RuntimeMath.SafeDirection(targetPosition - bullet.Position, bullet.Velocity);
            if (desired.LengthSquared() <= 0.0001f)
            {
                continue;
            }

            float currentAngle = Mathf.Atan2(bullet.Velocity.Y, bullet.Velocity.X);
            float targetAngle = Mathf.Atan2(desired.Y, desired.X);
            float angleDelta = WrapAngle(targetAngle - currentAngle);
            float turnRate = pair.Value.TurnRate > 0f ? pair.Value.TurnRate : LegacySeekerTurnRate;
            float turn = Mathf.Clamp(angleDelta, -turnRate * delta, turnRate * delta);
            Vector2 newDirection = AngleToDir(currentAngle + turn);
            bullet.Velocity = newDirection * pair.Value.Speed;
        }

        if (removeList == null)
        {
            return;
        }

        foreach (BulletActor bullet in removeList)
        {
            _legacyHomingBullets.Remove(bullet);
        }
    }

    private void TickLegacyBeams(float delta)
    {
        for (int beamIndex = _legacyBeams.Count - 1; beamIndex >= 0; beamIndex--)
        {
            TimedBeamEffect beam = _legacyBeams[beamIndex];
            beam.Age += delta;
            beam.Tick -= delta;

            if (beam.Tick <= 0f)
            {
                beam.Tick = LegacyLaserTickRate;
                float tickDamage = beam.Damage * LegacyLaserTickRate / Mathf.Max(0.01f, beam.Duration);
                DamageTargetsAlongSegment(beam.Start, beam.End, beam.HalfWidth, tickDamage, beam.IsCritical, beam.SourceItem, false);
            }

            if (beam.PrismForkEnabled && !beam.PrismForkFired && beam.Age >= beam.PrismForkDelay)
            {
                beam.PrismForkFired = true;
                Vector2 axis = RuntimeMath.SafeDirection(beam.End - beam.Start, Vector2.Right);
                Vector2 left = axis.Rotated(-0.45f);
                Vector2 right = axis.Rotated(0.45f);
                float forkLength = (beam.End - beam.Start).Length() * 0.65f;
                float forkDamage = beam.Damage * beam.PrismForkDamageScale;
                SpawnLegacyLaser(beam.End, left, forkLength, forkDamage, beam.HalfWidth * 0.72f, beam.Color, beam.IsCritical, beam.SourceItem);
                SpawnLegacyLaser(beam.End, right, forkLength, forkDamage, beam.HalfWidth * 0.72f, beam.Color, beam.IsCritical, beam.SourceItem);
            }

            if (beam.Age >= beam.Duration)
            {
                if (IsInstanceValid(beam.Actor))
                {
                    beam.Actor.QueueFree();
                }
                _legacyBeams.RemoveAt(beamIndex);
            }
        }
    }

    private void TickLegacyMines(float delta)
    {
        for (int mineIndex = _legacyMines.Count - 1; mineIndex >= 0; mineIndex--)
        {
            MineEffect mine = _legacyMines[mineIndex];
            mine.Age += delta;
            float mineLife = mine.Life > 0f ? mine.Life : LegacyMineLife;

            bool expired = mine.Age >= mineLife;
            bool armed = mine.Age >= LegacyMineArmTime;
            bool triggered = armed && HasHostileInRadius(mine.Position, LegacyMineTriggerRadius);

            if (triggered)
            {
                DamageTargetsInRadius(mine.Position, mine.Radius, mine.Damage, mine.IsCritical, mine.SourceItem, false);
                SpawnVisualPulse(mine.Position, mine.Radius * 0.35f, mine.Radius * 1.15f, mine.Color, 0.35f);

                if (mine.SpawnClusterOnDetonation && mine.ClusterChildCount > 0)
                {
                    for (int childIndex = 0; childIndex < mine.ClusterChildCount; childIndex++)
                    {
                        float angle = (childIndex / (float)mine.ClusterChildCount) * Mathf.Tau;
                        Vector2 offset = AngleToDir(angle) * Mathf.Lerp(1.5f, 3.5f, childIndex / (float)Mathf.Max(1, mine.ClusterChildCount - 1));
                        Vector2 childPosition = ClampResolve(mine.Position + offset, 1f);
                        MeshInstance3D childMesh = CreateDiskVisual(childPosition, mine.Radius * 0.14f, mine.Color, 0.32f, 0.14f);
                        _legacyMines.Add(new MineEffect
                        {
                            Position = childPosition,
                            Life = Mathf.Max(0.6f, mine.Life * 0.35f),
                            Age = LegacyMineArmTime,
                            Damage = mine.Damage * mine.ClusterChildDamageScale,
                            Radius = mine.Radius * 0.55f,
                            Color = mine.Color,
                            IsCritical = mine.IsCritical,
                            SourceItem = mine.SourceItem,
                            Mesh = childMesh,
                            Material = (StandardMaterial3D)childMesh.MaterialOverride,
                        });
                    }
                }
            }

            if (expired || triggered)
            {
                QueueFreeIfValid(mine.Mesh);
                _legacyMines.RemoveAt(mineIndex);
                continue;
            }

            float pulse = armed ? 1f + Mathf.Sin(mine.Age * 8f) * 0.15f : Mathf.Lerp(0.45f, 1f, mine.Age / LegacyMineArmTime);
            mine.Mesh.Scale = new Vector3(pulse, armed ? 1f : 0.5f, pulse);
            mine.Mesh.Rotation = new Vector3(0f, mine.Age * 0.5f, 0f);
            SetMaterialAlpha(mine.Material, armed ? 0.42f : 0.22f);
        }
    }

    private void TickLegacyNovas(float delta)
    {
        for (int novaIndex = _legacyNovas.Count - 1; novaIndex >= 0; novaIndex--)
        {
            NovaEffect nova = _legacyNovas[novaIndex];
            nova.Age += delta;
            float novaDuration = nova.Duration > 0f ? nova.Duration : LegacyNovaDuration;
            float progress = nova.Age / novaDuration;
            if (progress >= 1f)
            {
                if (nova.ReturnPulseDamageScale > 0f && !nova.ReturnPulseTriggered)
                {
                    nova.ReturnPulseTriggered = true;
                    DamageTargetsInRadius(nova.Position, nova.MaxRadius * 0.92f, nova.Damage * nova.ReturnPulseDamageScale, nova.IsCritical, nova.SourceItem, false);
                    SpawnVisualPulse(nova.Position, nova.MaxRadius * 0.8f, nova.MaxRadius * 0.15f, nova.Color.Lightened(0.08f), 0.24f);
                }

                QueueFreeIfValid(nova.Mesh);
                _legacyNovas.RemoveAt(novaIndex);
                continue;
            }

            float ringRadius = Mathf.Sqrt(progress) * nova.MaxRadius;
            float innerRadius = Mathf.Max(0f, ringRadius - 4f);
            foreach (EnemyActor enemy in _enemies)
            {
                if (!enemy.IsAlive || nova.HitEnemies.Contains(enemy))
                {
                    continue;
                }

                float distance = (enemy.Position - nova.Position).Length();
                if (distance <= ringRadius + enemy.Radius && distance >= innerRadius - enemy.Radius)
                {
                    nova.HitEnemies.Add(enemy);
                    DealDamageToEnemy(enemy, nova.Damage, nova.Position, false, nova.IsCritical, true, nova.SourceItem, false);
                }
            }

            if (!nova.HitBoss && _boss != null && _boss.IsAlive)
            {
                float bossDistance = (_boss.Position - nova.Position).Length();
                if (bossDistance <= ringRadius + _boss.Radius && bossDistance >= innerRadius - _boss.Radius)
                {
                    nova.HitBoss = true;
                    DealDamageToBoss(nova.Damage, nova.IsCritical, true, nova.SourceItem, false);
                }
            }

            nova.Mesh.Scale = new Vector3(ringRadius, 1f, ringRadius);
            nova.Mesh.Rotation = new Vector3(0f, nova.Age * 2f, 0f);
            SetMaterialAlpha(nova.Material, 0.34f * (1f - progress * progress));
        }
    }

    private void TickLegacyFlames(float delta)
    {
        for (int flameIndex = _legacyFlames.Count - 1; flameIndex >= 0; flameIndex--)
        {
            FlameEffect flame = _legacyFlames[flameIndex];
            flame.Age += delta;
            flame.Tick -= delta;
            float flameDuration = flame.Duration > 0f ? flame.Duration : LegacyFlameDuration;

            if (flame.Tick <= 0f)
            {
                flame.Tick = LegacyFlameTickRate;
                float tickDamage = flame.Damage * LegacyFlameTickRate / flameDuration;
                DamageTargetsInCone(flame.Origin, flame.Direction, flame.Range, LegacyFlameHalfAngle, tickDamage, flame.IsCritical, flame.SourceItem, false);

                if (flame.CorridorDamageScale > 0f && flame.SourceItem != null)
                {
                    flame.CorridorTick -= LegacyFlameTickRate;
                    if (flame.CorridorTick <= 0f)
                    {
                        flame.CorridorTick = 0.20f;
                        Vector2 corridorCenter = flame.Origin + flame.Direction * flame.Range * 0.55f;
                        SpawnModulatorLingeringZone(
                            corridorCenter,
                            flame.Damage * flame.CorridorDamageScale,
                            flame.IsCritical,
                            flame.SourceItem,
                            new Equipment.Data.WeaponModulatorEffectData
                            {
                                Type = Equipment.Data.ModulatorEffectType.LingeringZone,
                                Trigger = Equipment.Data.ModulatorEffectTrigger.OnHit,
                                Magnitude = flame.CorridorDamageScale,
                                MagnitudeB = flame.Range * 0.35f,
                                MagnitudeC = Mathf.Max(0.2f, flame.CorridorDuration),
                            });
                    }
                }
            }

            if (flame.Age >= flameDuration)
            {
                QueueFreeIfValid(flame.Mesh);
                _legacyFlames.RemoveAt(flameIndex);
                continue;
            }

            float fade = 1f - flame.Age / flameDuration;
            SetMaterialAlpha(flame.Material, 0.28f * fade);
            flame.Mesh.Scale = new Vector3(1f + (1f - fade) * 0.35f, 1f, 1f + (1f - fade) * 0.08f);
        }
    }

    private void TickLegacyOrbitals(float delta)
    {
        for (int orbitalIndex = _legacyOrbitals.Count - 1; orbitalIndex >= 0; orbitalIndex--)
        {
            OrbitalEffect orbital = _legacyOrbitals[orbitalIndex];
            orbital.Age += delta;
            if (orbital.Age >= orbital.Life)
            {
                QueueFreeIfValid(orbital.Mesh);
                _legacyOrbitals.RemoveAt(orbitalIndex);
                continue;
            }

            orbital.Angle += orbital.Speed * delta;
            if (orbital.ReverseInterval > 0f)
            {
                orbital.ReverseTimer -= delta;
                if (orbital.ReverseTimer <= 0f)
                {
                    orbital.ReverseTimer = orbital.ReverseInterval;
                    orbital.Speed *= -1f;
                }
            }

            Vector2 position = _player.Position + AngleToDir(orbital.Angle) * orbital.OrbitRadius;
            orbital.Mesh.Position = new Vector3(position.X, 2.5f, position.Y);
            orbital.Mesh.Rotation = new Vector3(orbital.Age * 2f, orbital.Age * 3f, orbital.Age * 1.5f);

            if (orbital.TrailDamageScale > 0f)
            {
                orbital.TrailTick -= delta;
                if (orbital.TrailTick <= 0f)
                {
                    orbital.TrailTick = 0.20f;
                    DamageTargetsInRadius(position, Mathf.Max(1.2f, LegacyOrbitalHitRadius * orbital.SizeScale), orbital.Damage * orbital.TrailDamageScale * 0.20f, orbital.IsCritical, orbital.SourceItem, false);
                }
            }

            float hitRadius = LegacyOrbitalHitRadius * orbital.SizeScale;
            foreach (EnemyActor enemy in _enemies)
            {
                if (!enemy.IsAlive)
                {
                    continue;
                }

                if ((enemy.Position - position).LengthSquared() > Mathf.Pow(hitRadius + enemy.Radius, 2f))
                {
                    continue;
                }

                if (!CanOrbitalHit(orbital, enemy))
                {
                    continue;
                }

                orbital.LastHit[enemy] = orbital.Age;
                DealDamageToEnemy(enemy, orbital.Damage, position, false, orbital.IsCritical, true, orbital.SourceItem, false);
            }

            if (_boss != null && _boss.IsAlive && (_boss.Position - position).LengthSquared() <= Mathf.Pow(hitRadius + _boss.Radius, 2f) && CanOrbitalHit(orbital, _boss))
            {
                orbital.LastHit[_boss] = orbital.Age;
                DealDamageToBoss(orbital.Damage, orbital.IsCritical, true, orbital.SourceItem, false);
            }
        }
    }

    private static bool CanOrbitalHit(OrbitalEffect orbital, Node3D target)
    {
        return !orbital.LastHit.TryGetValue(target, out float lastHit) || orbital.Age - lastHit >= LegacyOrbitalHitCooldown;
    }

    private void TickLegacyRifts(float delta)
    {
        for (int riftIndex = _legacyRifts.Count - 1; riftIndex >= 0; riftIndex--)
        {
            RiftEffect rift = _legacyRifts[riftIndex];
            rift.Age += delta;
            rift.Tick -= delta;
            float riftDuration = rift.Duration > 0f ? rift.Duration : LegacyRiftDuration;

            if (rift.Age >= riftDuration)
            {
                QueueFreeIfValid(rift.Mesh);
                _legacyRifts.RemoveAt(riftIndex);
                continue;
            }

            if (rift.Tick <= 0f)
            {
                rift.Tick = rift.PulseInterval > 0f ? rift.PulseInterval : LegacyRiftPulseRate;
                float pulseInterval = rift.Tick;
                DamageTargetsInRadius(rift.Position, rift.Radius, rift.Damage * pulseInterval / riftDuration, rift.IsCritical, rift.SourceItem, false);
                if (rift.AlternatingPushPull)
                {
                    float force = 28f;
                    if (rift.PushPhase)
                    {
                        PushEnemiesAway(rift.Position, rift.Radius, force, pulseInterval);
                    }
                    else
                    {
                        PullEnemiesToward(rift.Position, rift.Radius, force, pulseInterval);
                    }

                    rift.PushPhase = !rift.PushPhase;
                }
            }

            float fadeIn = Mathf.Min(1f, rift.Age * 5f);
            float fadeOut = Mathf.Min(1f, (riftDuration - rift.Age) * 3f);
            float pulse = 0.9f + Mathf.Sin(rift.Age * 15f) * 0.12f;
            rift.Mesh.Scale = new Vector3(rift.Radius * pulse, 1f, rift.Radius * pulse);
            rift.Mesh.Rotation = new Vector3(0f, rift.Age * 3f, 0f);
            SetMaterialAlpha(rift.Material, 0.26f * fadeIn * fadeOut);
        }
    }

    private void TickLegacyMortars(float delta)
    {
        for (int mortarIndex = _legacyMortars.Count - 1; mortarIndex >= 0; mortarIndex--)
        {
            MortarEffect mortar = _legacyMortars[mortarIndex];
            mortar.Age += delta;
            float flightTime = mortar.FlightTime > 0f ? mortar.FlightTime : LegacyMortarFlightTime;
            float progress = Mathf.Clamp(mortar.Age / flightTime, 0f, 1f);
            Vector2 flatPosition = mortar.Start.Lerp(mortar.Target, progress);
            float arcHeight = Mathf.Sin(progress * Mathf.Pi) * LegacyMortarArcHeight;
            mortar.Mesh.Position = new Vector3(flatPosition.X, 1.1f + arcHeight, flatPosition.Y);

            if (progress >= 1f)
            {
                DamageTargetsInRadius(mortar.Target, mortar.Radius, mortar.Damage, mortar.IsCritical, mortar.SourceItem, false);
                SpawnVisualPulse(mortar.Target, mortar.Radius * 0.25f, mortar.Radius * 1.25f, mortar.Color, 0.38f);
                QueueFreeIfValid(mortar.Mesh);
                _legacyMortars.RemoveAt(mortarIndex);
            }
        }
    }

    private void TickLegacyChains(float delta)
    {
        for (int chainIndex = _legacyChains.Count - 1; chainIndex >= 0; chainIndex--)
        {
            ChainEffect chain = _legacyChains[chainIndex];
            chain.Age += delta;
            if (chain.Age < LegacyChainDuration)
            {
                continue;
            }

            foreach (LaserBeamActor beam in chain.Beams)
            {
                QueueFreeIfValid(beam);
            }

            _legacyChains.RemoveAt(chainIndex);
        }
    }

    private void TickLegacyDrills(float delta)
    {
        for (int drillIndex = _legacyDrills.Count - 1; drillIndex >= 0; drillIndex--)
        {
            DrillEffect drill = _legacyDrills[drillIndex];
            drill.Age += delta;
            float drillLife = drill.Life > 0f ? drill.Life : LegacyDrillLife;
            if (drill.Age >= drillLife)
            {
                QueueFreeIfValid(drill.Mesh);
                _legacyDrills.RemoveAt(drillIndex);
                continue;
            }

            if (drill.TargetEnemy != null)
            {
                TickBurrowedDrill(drill, delta);
            }
            else
            {
                TickFlyingDrill(drill, delta);
            }

            if (drill.WakeDamageScale > 0f)
            {
                drill.WakeTick -= delta;
                if (drill.WakeTick <= 0f)
                {
                    drill.WakeTick = 0.20f;
                    SpawnModulatorLingeringZone(
                        drill.Position,
                        drill.Damage * drill.WakeDamageScale,
                        drill.IsCritical,
                        drill.SourceItem!,
                        new Equipment.Data.WeaponModulatorEffectData
                        {
                            Type = Equipment.Data.ModulatorEffectType.LingeringZone,
                            Trigger = Equipment.Data.ModulatorEffectTrigger.OnHit,
                            Magnitude = 0.28f,
                            MagnitudeB = 1.7f,
                            MagnitudeC = Mathf.Max(0.2f, drill.WakeDuration),
                        });
                }
            }

            float heading = Mathf.Atan2(drill.Velocity.Y, drill.Velocity.X);
            drill.Mesh.Position = new Vector3(drill.Position.X, 2.2f, drill.Position.Y);
            drill.Mesh.Rotation = new Vector3(0f, -heading + Mathf.Pi / 2f, drill.Age * 20f);
        }
    }

    private void TickFlyingDrill(DrillEffect drill, float delta)
    {
        float drillLife = drill.Life > 0f ? drill.Life : LegacyDrillLife;
        drill.Position += drill.Velocity * delta;
        if (Mathf.Abs(drill.Position.X) > ArenaHalfW + 20f || Mathf.Abs(drill.Position.Y) > ArenaHalfH + 20f || IsPointBlockedByPillar(drill.Position, LegacyDrillHitRadius))
        {
            drill.Age = drillLife;
            return;
        }

        if (_boss != null && _boss.IsAlive && (_boss.Position - drill.Position).LengthSquared() <= Mathf.Pow(_boss.Radius + LegacyDrillHitRadius, 2f))
        {
            DealDamageToBoss(drill.Damage * 0.3f, drill.IsCritical, true, drill.SourceItem);
            drill.Age = drillLife;
            return;
        }

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive || (enemy.Position - drill.Position).LengthSquared() > Mathf.Pow(enemy.Radius + LegacyDrillHitRadius, 2f))
            {
                continue;
            }

            drill.TargetEnemy = enemy;
            drill.BurrowAge = 0f;
            drill.Tick = 0f;
            DealDamageToEnemy(enemy, drill.Damage * 0.3f, drill.Position, true, drill.IsCritical, true, drill.SourceItem);
            return;
        }
    }

    private void TickBurrowedDrill(DrillEffect drill, float delta)
    {
        EnemyActor target = drill.TargetEnemy!;
        if (!IsInstanceValid(target) || !target.IsAlive)
        {
            drill.TargetEnemy = null;
            drill.BurrowAge = 0f;
            return;
        }

        drill.Position = target.Position;
        drill.BurrowAge += delta;
        drill.Tick -= delta;
        if (drill.Tick <= 0f)
        {
            drill.Tick = LegacyDrillTickRate;
            DealDamageToEnemy(target, drill.Damage * LegacyDrillTickRate / LegacyDrillBurrowDuration, drill.Position, false, drill.IsCritical, true, drill.SourceItem, false);
        }

        if (drill.BurrowAge < LegacyDrillBurrowDuration)
        {
            return;
        }

        drill.TargetEnemy = null;
        drill.BurrowAge = 0f;
        if (TryFindNearestTarget(drill.Position, 120f, out Vector2 targetPosition))
        {
            float projectileSpeedMultiplier = drill.SourceItem == null ? 1f : GetWeaponItemProjectileSpeedMultiplier(drill.SourceItem);
            drill.Velocity = RuntimeMath.SafeDirection(targetPosition - drill.Position, drill.Velocity) * (LegacyDrillSpeed * projectileSpeedMultiplier);
        }
    }

    private void TickLegacyVoids(float delta)
    {
        for (int voidIndex = _legacyVoids.Count - 1; voidIndex >= 0; voidIndex--)
        {
            VoidEffect voidEffect = _legacyVoids[voidIndex];
            voidEffect.Age += delta;
            voidEffect.Tick -= delta;
            float voidDuration = voidEffect.Duration > 0f ? voidEffect.Duration : LegacyVoidDuration;

            if (voidEffect.Age >= voidDuration)
            {
                if (voidEffect.EndBurstEnabled)
                {
                    float burstDamage = voidEffect.Damage * voidEffect.EndBurstMultiplier;
                    DamageTargetsInRadius(voidEffect.Position, voidEffect.DamageRadius * 1.25f, burstDamage, voidEffect.IsCritical, voidEffect.SourceItem, false);
                    SpawnVisualPulse(voidEffect.Position, voidEffect.DamageRadius * 0.4f, voidEffect.DamageRadius * 1.5f, voidEffect.Color.Lightened(0.18f), 0.26f);

                    int shardCount = Mathf.Max(1, voidEffect.EndShardCount);
                    for (int shardIndex = 0; shardIndex < shardCount; shardIndex++)
                    {
                        float angle = (shardIndex / (float)shardCount) * Mathf.Tau;
                        Vector2 direction = AngleToDir(angle);
                        SpawnLegacyBullet(voidEffect.Position, direction * 26f, voidEffect.Damage * voidEffect.EndShardDamageScale, voidEffect.Color, 0.9f, 2.4f, 0, voidEffect.IsCritical, voidEffect.SourceItem);
                    }
                }

                QueueFreeIfValid(voidEffect.Mesh);
                _legacyVoids.RemoveAt(voidIndex);
                continue;
            }

            float fadeIn = Mathf.Min(1f, voidEffect.Age * 4f);
            float fadeOut = Mathf.Min(1f, (voidDuration - voidEffect.Age) * 2f);
            float intensity = fadeIn * fadeOut;
            PullEnemiesToward(voidEffect.Position, voidEffect.PullRadius, LegacyVoidPullForce * voidEffect.PullStrengthMultiplier * intensity, delta);

            if (voidEffect.Tick <= 0f)
            {
                voidEffect.Tick = LegacyVoidTickRate;
                float tickDamage = voidEffect.Damage * LegacyVoidTickRate / voidDuration * 2f;
                DamageTargetsInRadius(voidEffect.Position, voidEffect.DamageRadius, tickDamage, voidEffect.IsCritical, voidEffect.SourceItem, false);
            }

            float spinPulse = 0.92f + Mathf.Sin(voidEffect.Age * 10f) * 0.10f;
            voidEffect.Mesh.Scale = new Vector3(voidEffect.PullRadius * spinPulse, 1f, voidEffect.PullRadius * spinPulse);
            voidEffect.Mesh.Rotation = new Vector3(0f, voidEffect.Age * 5f, 0f);
            SetMaterialAlpha(voidEffect.Material, 0.34f * intensity);
        }
    }

    private void TickLegacyWrathAura(float delta)
    {
        if (_weaponKind != WeaponKind.Klein)
        {
            _legacyWrathTick = 0f;
            return;
        }

        _legacyWrathTick -= delta;
        if (_legacyWrathTick > 0f)
        {
            return;
        }

        _legacyWrathTick = LegacyWrathTickRate;
        float healthRatio = _player.MaxHp > 0f ? _player.Hp / _player.MaxHp : 1f;
        float wrathMultiplier = 1f + 2f * (1f - healthRatio);
        float radius = LegacyWrathBaseRadius + 7.2f;
        float damage = _player.BulletDamage * 0.8f * wrathMultiplier * LegacyWrathTickRate;
        if (_bloodPriceStoredDamage > 0f)
        {
            _bloodPricePulseTimer -= delta;
            if (_bloodPricePulseTimer <= 0f)
            {
                _bloodPricePulseTimer = 1.5f;
                damage += _bloodPriceStoredDamage * 0.30f;
                _bloodPriceStoredDamage = Mathf.Max(0f, _bloodPriceStoredDamage * 0.55f);
            }
        }

        Color color = GetWeaponShotColor(WeaponKind.Klein, _weaponRarity);

        DamageTargetsInRadius(_player.Position, radius, damage, false, _equippedLoadout[0], false);
        SpawnVisualPulse(_player.Position, radius * 0.7f, radius, color, 0.22f);
    }

    private void TickLegacyClones(float delta)
    {
        for (int cloneIndex = _legacyClones.Count - 1; cloneIndex >= 0; cloneIndex--)
        {
            CloneEffect clone = _legacyClones[cloneIndex];
            clone.Age += delta;
            float cloneLife = clone.Life > 0f ? clone.Life : LegacyCloneLife;
            if (clone.Age >= cloneLife)
            {
                QueueFreeIfValid(clone.Mesh);
                _legacyClones.RemoveAt(cloneIndex);
                continue;
            }

            clone.Angle += LegacyCloneOrbitSpeed * delta;
            float expand = Mathf.Min(1f, clone.Age * 2f);
            float fade = Mathf.Min(1f, (cloneLife - clone.Age) * 3f) * expand;
            Vector2 position = clone.Center + AngleToDir(clone.Angle) * (LegacyCloneOrbitDistance * expand);

            clone.Mesh.Position = new Vector3(position.X, 1.55f, position.Y);
            clone.Mesh.Rotation = new Vector3(0f, clone.Angle * 2f, 0f);
            clone.Mesh.Scale = new Vector3(fade, fade, fade);
            SetMaterialAlpha(clone.Material, 0.45f * fade);

            clone.FireTimer -= delta;
            if (clone.FireTimer > 0f || !TryFindNearestTarget(position, 150f, out Vector2 targetPosition))
            {
                continue;
            }

            clone.FireTimer = 0.65f;
            Vector2 direction = RuntimeMath.SafeDirection(targetPosition - position, Vector2.Right);
            if (clone.MimicPrimaryShot)
            {
                FireLegacyWeaponForKind(_weaponKind, direction, position, Mathf.Clamp(clone.MimicDamageScale, 0.1f, 2f), true, clone.SourceDamage, clone.SourceRarity, clone.SourceItem);
            }
            else
            {
                FireLegacyWeaponForKind(WeaponKind.Square, direction, position, LegacyCloneDamageMult, true, clone.SourceDamage, clone.SourceRarity, clone.SourceItem);
            }
        }
    }

    private void TickLegacyEchoes(float delta)
    {
        for (int echoIndex = _legacyEchoes.Count - 1; echoIndex >= 0; echoIndex--)
        {
            DelayedEcho echo = _legacyEchoes[echoIndex];
            echo.TimeLeft -= delta;
            if (echo.TimeLeft > 0f)
            {
                continue;
            }

            FireLegacyWeaponForKind(echo.Kind, echo.Direction, echo.Origin, echo.DamageScale, true, echo.SourceDamage, echo.SourceRarity, echo.SourceItem);
            _legacyEchoes.RemoveAt(echoIndex);
        }
    }

    private void PushEnemiesAway(Vector2 center, float radius, float force, float delta)
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
            Vector2 pushDirection = toEnemy / distance;
            float pushStrength = force * (1f - distance / radius);
            enemy.Position = ClampResolve(enemy.Position + pushDirection * pushStrength * delta, enemy.Radius);
        }
    }

    private void TickLegacyVisualPulses(float delta)
    {
        for (int pulseIndex = _legacyVisualPulses.Count - 1; pulseIndex >= 0; pulseIndex--)
        {
            VisualPulseEffect pulse = _legacyVisualPulses[pulseIndex];
            pulse.Age += delta;
            float progress = pulse.Duration <= 0f ? 1f : pulse.Age / pulse.Duration;
            if (progress >= 1f)
            {
                QueueFreeIfValid(pulse.Mesh);
                _legacyVisualPulses.RemoveAt(pulseIndex);
                continue;
            }

            float radius = Mathf.Lerp(pulse.StartRadius, pulse.EndRadius, progress);
            pulse.Mesh.Scale = new Vector3(radius, 1f, radius);
            SetMaterialAlpha(pulse.Material, pulse.Color.A * (1f - progress));
        }
    }

    private bool TryConsumeLegacyBulletHit(BulletActor bullet, Vector2 hitPosition)
    {
        if (_legacySawReturnLeft.TryGetValue(bullet, out int sawReturnLeft) && sawReturnLeft > 0)
        {
            _legacySawReturnLeft[bullet] = sawReturnLeft - 1;
            Vector2 returnDirection = RuntimeMath.SafeDirection(_player.Position - hitPosition, -bullet.Velocity);
            float speed = Mathf.Max(1f, bullet.Velocity.Length());
            bullet.Position = hitPosition + returnDirection * (bullet.Radius + 6f);
            bullet.Velocity = returnDirection * speed;
            bullet.Damage *= 0.90f;
            return true;
        }

        if (!_legacyPierceLeft.TryGetValue(bullet, out int pierceLeft) || pierceLeft <= 0)
        {
            return false;
        }

        pierceLeft--;
        if (pierceLeft <= 0)
        {
            _legacyPierceLeft.Remove(bullet);

            // Serration Bloom splits only when the sawblade reaches its last pierce.
            TrySpawnSerrationBloom(bullet, hitPosition);

            return false;
        }

        _legacyPierceLeft[bullet] = pierceLeft;
        Vector2 direction = bullet.Velocity.LengthSquared() > 0.001f ? bullet.Velocity.Normalized() : Vector2.Right;
        bullet.Position = hitPosition + direction * (bullet.Radius + 5f);
        bullet.Damage *= 0.92f;
        return true;
    }

    private void CleanupLegacyBulletState(BulletActor bullet)
    {
        _legacyPierceLeft.Remove(bullet);
        _legacySawReturnLeft.Remove(bullet);
        _legacyHomingBullets.Remove(bullet);
    }

    private void ResetLegacyWeaponEffects()
    {
        _legacyPierceLeft.Clear();
        _legacySawReturnLeft.Clear();
        _legacyHomingBullets.Clear();
        _legacyEchoes.Clear();
        _legacyWrathTick = 0f;
        _modulatorCritCascadeCooldown = 0f;
        _packHunterMarkTimeLeft = 0f;
        _packHunterMarkPosition = Vector2.Zero;
        _bloodPriceStoredDamage = 0f;
        _bloodPricePulseTimer = 0f;
        _omniFeedbackHitCounter = 0;
        ResetModulatorEffectState();

        foreach (TimedBeamEffect beam in _legacyBeams)
        {
            QueueFreeIfValid(beam.Actor);
        }

        foreach (MineEffect mine in _legacyMines) QueueFreeIfValid(mine.Mesh);
        foreach (NovaEffect nova in _legacyNovas) QueueFreeIfValid(nova.Mesh);
        foreach (FlameEffect flame in _legacyFlames) QueueFreeIfValid(flame.Mesh);
        foreach (OrbitalEffect orbital in _legacyOrbitals) QueueFreeIfValid(orbital.Mesh);
        foreach (RiftEffect rift in _legacyRifts) QueueFreeIfValid(rift.Mesh);
        foreach (MortarEffect mortar in _legacyMortars) QueueFreeIfValid(mortar.Mesh);
        foreach (ChainEffect chain in _legacyChains)
        {
            foreach (LaserBeamActor beam in chain.Beams)
            {
                QueueFreeIfValid(beam);
            }
        }
        foreach (DrillEffect drill in _legacyDrills) QueueFreeIfValid(drill.Mesh);
        foreach (VoidEffect voidEffect in _legacyVoids) QueueFreeIfValid(voidEffect.Mesh);
        foreach (CloneEffect clone in _legacyClones) QueueFreeIfValid(clone.Mesh);
        foreach (VisualPulseEffect pulse in _legacyVisualPulses) QueueFreeIfValid(pulse.Mesh);

        _legacyBeams.Clear();
        _legacyMines.Clear();
        _legacyNovas.Clear();
        _legacyFlames.Clear();
        _legacyOrbitals.Clear();
        _legacyRifts.Clear();
        _legacyMortars.Clear();
        _legacyChains.Clear();
        _legacyDrills.Clear();
        _legacyVoids.Clear();
        _legacyClones.Clear();
        _legacyVisualPulses.Clear();
    }
}