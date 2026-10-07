using Godot;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.Visuals;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const float ArenaWeaponOrbitRadius = 5.2f;
    private const float ArenaWeaponFloatHeight = 2.55f;
    private const float ArenaWeaponBobHeight = 0.28f;
    private const float ArenaWeaponVisualScale = 1.18f;
    private const float ArenaWeaponSpinSpeed = 0.78f;
    private const float ArenaWeaponFollowSharpness = 7.5f;
    private const float ArenaWeaponDriftRadius = 0.42f;
    private const float ArenaWeaponTransitionSharpness = 8.5f;
    private const float ArenaWeaponAimSharpness = 10.0f;
    private const float ArenaWeaponCombatSharpness = 5.4f;
    private const float ArenaWeaponRecoilReturnSharpness = 11.0f;
    private const float ArenaWeaponRecoilDistance = 0.45f;
    private const float ArenaWeaponEquipRiseHeight = 1.35f;
    private const float ArenaWeaponExitRiseHeight = 1.8f;
    private const float ArenaWeaponCombatHoldDuration = 0.48f;
    private const float ArenaWeaponBrotherLeashDistance = 26f;
    private const float ArenaWeaponBrotherOrbitRadius = 7.5f;
    private const float ArenaWeaponBrotherTargetRange = 150f;

    private readonly Dictionary<int, ArenaWeaponVisual> _arenaWeaponVisualsBySlot = new();
    private readonly List<ArenaWeaponVisual> _arenaWeaponExitingVisuals = new();
    private float _arenaWeaponVisualTime;
    private float _arenaWeaponCombatBlend;
    private float _arenaWeaponCombatHoldTimer;
    private Vector2 _arenaWeaponLastAimDirection = Vector2.Right;

    private sealed class ArenaWeaponVisual
    {
        public int SlotIndex;
        public string ItemInstanceId = string.Empty;
        public EquipmentItem Item = null!;
        public Vector2 Offset;
        public Vector2 AnchorPosition2D;
        public Vector2 Position2D;
        public bool HasPosition;
        public Node3D Root = null!;
        public Node3D Model = null!;
        public OmniLight3D GlowLight = null!;
        public ModifierVisualController? ModifierVisual;
        public Vector3 BaseModelRotation;
        public bool UsesNativeAnimation;
        public float DriftPhase;
        public float HeightPhase;
        public float TiltPhase;
        public Vector2 DisplayAimDirection;
        public float Recoil;
        public float Transition;
        public bool IsExiting;
    }

    private void RebuildArenaWeaponVisuals()
    {
        if (_player == null)
        {
            ClearArenaWeaponVisuals();
            return;
        }

        List<(int SlotIndex, EquipmentItem Item)> equippedWeapons = new();
        for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? item = _equippedLoadout[slotIndex];
            if (item?.Shape != null)
            {
                equippedWeapons.Add((slotIndex, item));
            }
        }

        HashSet<int> desiredSlots = new();
        Vector2[] offsets = BuildArenaWeaponOffsets(equippedWeapons.Count);
        for (int weaponIndex = 0; weaponIndex < equippedWeapons.Count; weaponIndex++)
        {
            (int slotIndex, EquipmentItem item) = equippedWeapons[weaponIndex];
            desiredSlots.Add(slotIndex);

            if (_arenaWeaponVisualsBySlot.TryGetValue(slotIndex, out ArenaWeaponVisual? existingVisual))
            {
                if (string.Equals(existingVisual.ItemInstanceId, item.InstanceId, System.StringComparison.Ordinal))
                {
                    existingVisual.Item = item;
                    existingVisual.Offset = offsets[weaponIndex];
                    existingVisual.IsExiting = false;
                    continue;
                }

                StartArenaWeaponExit(existingVisual);
                _arenaWeaponVisualsBySlot.Remove(slotIndex);
            }

            ArenaWeaponVisual visual = CreateArenaWeaponVisual(slotIndex, item, offsets[weaponIndex]);
            _arenaWeaponVisualsBySlot[slotIndex] = visual;
        }

        List<int> staleSlots = new();
        foreach (KeyValuePair<int, ArenaWeaponVisual> pair in _arenaWeaponVisualsBySlot)
        {
            if (!desiredSlots.Contains(pair.Key))
            {
                staleSlots.Add(pair.Key);
            }
        }

        for (int staleIndex = 0; staleIndex < staleSlots.Count; staleIndex++)
        {
            int slotIndex = staleSlots[staleIndex];
            StartArenaWeaponExit(_arenaWeaponVisualsBySlot[slotIndex]);
            _arenaWeaponVisualsBySlot.Remove(slotIndex);
        }
    }

    private void TickArenaWeaponVisuals(float deltaSeconds)
    {
        if (_player == null)
        {
            return;
        }

        float dt = Mathf.Max(0f, RuntimeMath.FiniteOr(deltaSeconds, 0f));
        _arenaWeaponVisualTime += dt;
        UpdateArenaWeaponCombatState(dt, out bool hasTarget, out Vector2 targetPosition);

        List<int> invalidActiveSlots = new();
        foreach (KeyValuePair<int, ArenaWeaponVisual> pair in _arenaWeaponVisualsBySlot)
        {
            ArenaWeaponVisual visual = pair.Value;
            if (!IsInstanceValid(visual.Root) || !IsInstanceValid(visual.Model))
            {
                invalidActiveSlots.Add(pair.Key);
                continue;
            }

            TickArenaWeaponVisual(visual, dt, hasTarget, targetPosition);
        }

        for (int i = 0; i < invalidActiveSlots.Count; i++)
        {
            _arenaWeaponVisualsBySlot.Remove(invalidActiveSlots[i]);
        }

        for (int visualIndex = _arenaWeaponExitingVisuals.Count - 1; visualIndex >= 0; visualIndex--)
        {
            ArenaWeaponVisual visual = _arenaWeaponExitingVisuals[visualIndex];
            if (!IsInstanceValid(visual.Root) || !IsInstanceValid(visual.Model))
            {
                _arenaWeaponExitingVisuals.RemoveAt(visualIndex);
                continue;
            }

            TickArenaWeaponVisual(visual, dt, false, targetPosition);
            if (visual.Transition <= 0.02f)
            {
                QueueFreeIfValid(visual.Root);
                _arenaWeaponExitingVisuals.RemoveAt(visualIndex);
            }
        }
    }

    private Vector2 GetArenaWeaponFireOrigin(int slotIndex, Vector2 direction)
    {
        if (_arenaWeaponVisualsBySlot.TryGetValue(slotIndex, out ArenaWeaponVisual? visual) && IsInstanceValid(visual.Root))
        {
            Vector2 safeDirection = RuntimeMath.SafeDirection(direction, Vector2.Right);
            return RuntimeMath.FiniteOr(visual.Position2D + safeDirection * 0.9f, visual.Position2D);
        }

        return _player != null ? _player.Position : Vector2.Zero;
    }

    private void ResolveArenaWeaponShot(int slotIndex, Vector2 targetPosition, Vector2 fallbackDirection, out Vector2 origin, out Vector2 direction)
    {
        Vector2 safeFallbackDirection = RuntimeMath.SafeDirection(fallbackDirection, Vector2.Right);

        if (_arenaWeaponVisualsBySlot.TryGetValue(slotIndex, out ArenaWeaponVisual? visual) && IsInstanceValid(visual.Root))
        {
            Vector2 initialDirection = RuntimeMath.SafeDirection(targetPosition - visual.Position2D, safeFallbackDirection);
            origin = GetArenaWeaponFireOrigin(slotIndex, initialDirection);
            direction = RuntimeMath.SafeDirection(targetPosition - origin, initialDirection);
            return;
        }

        origin = _player != null ? _player.Position : Vector2.Zero;
        direction = RuntimeMath.SafeDirection(targetPosition - origin, safeFallbackDirection);
    }

    private void NotifyArenaWeaponFired(int slotIndex, Vector2 direction)
    {
        _arenaWeaponCombatHoldTimer = Mathf.Max(_arenaWeaponCombatHoldTimer, ArenaWeaponCombatHoldDuration);
        _arenaWeaponLastAimDirection = RuntimeMath.SafeDirection(direction, _arenaWeaponLastAimDirection);

        if (!_arenaWeaponVisualsBySlot.TryGetValue(slotIndex, out ArenaWeaponVisual? visual) || !IsInstanceValid(visual.Root))
        {
            return;
        }

        visual.Recoil = 1f;
        visual.DisplayAimDirection = RuntimeMath.SafeDirection(direction, visual.DisplayAimDirection);
    }

    private ArenaWeaponVisual CreateArenaWeaponVisual(int slotIndex, EquipmentItem item, Vector2 offset)
    {
        Color tint = ResolveArenaWeaponTint(item);
        Node3D root = new()
        {
            Name = $"ArenaWeaponSlot{slotIndex}",
        };
        AddChild(root);

        Node3D model = ShapeVisualFactory.CreateVisual(item.Shape, tint);
        model.Name = "WeaponModel";
        model.Scale = RuntimeMath.FiniteOr(model.Scale, Vector3.One) * ArenaWeaponVisualScale;
        root.AddChild(model);

        OmniLight3D glowLight = new()
        {
            Name = "WeaponGlow",
            Position = new Vector3(0f, 0.3f, 0f),
            OmniRange = 7f,
            LightColor = tint.Lightened(0.18f),
            LightEnergy = 1.35f,
            ShadowEnabled = false,
        };
        root.AddChild(glowLight);

        ModifierVisualController? modifierVisual = null;
        if (item.Shape != null && ModifierVisualTheme.HasSpecialVisuals(item))
        {
            modifierVisual = new ModifierVisualController
            {
                Name = "WeaponModifierVisual",
                Intensity = 0.92f,
            };
            root.AddChild(modifierVisual);
            modifierVisual.Configure(item, model, item.Shape, ArenaWeaponVisualScale, glowLight, 0.92f);
            modifierVisual.CollectEnvironmentElectricGlobalAnchors = anchors => CollectArenaWeaponElectricEnvironmentAnchors(root, anchors);
            modifierVisual.CollectCharacterElectricGlobalAnchors = CollectArenaWeaponElectricCharacterAnchors;
            modifierVisual.ResolveElectricEnvironmentCollision = ResolveArenaWeaponElectricEnvironmentCollision;
        }

        float driftPhase = 0.7f + slotIndex * 1.73f;
        Vector2 initialAimDirection = RuntimeMath.SafeDirection(offset, _arenaWeaponLastAimDirection);

        return new ArenaWeaponVisual
        {
            SlotIndex = slotIndex,
            ItemInstanceId = item.InstanceId,
            Item = item,
            Offset = offset,
            AnchorPosition2D = _player.Position,
            Position2D = _player.Position,
            HasPosition = true,
            Root = root,
            Model = model,
            GlowLight = glowLight,
            ModifierVisual = modifierVisual,
            BaseModelRotation = model.Rotation,
            UsesNativeAnimation = ShapeVisualFactory.UsesNativeAnimation(model),
            DriftPhase = driftPhase,
            HeightPhase = 1.1f + slotIndex * 1.37f,
            TiltPhase = 0.45f + slotIndex * 1.11f,
            DisplayAimDirection = initialAimDirection,
            Transition = 0f,
        };
    }

    private void CollectArenaWeaponElectricEnvironmentAnchors(Node3D weaponRoot, ICollection<Vector3> anchors)
    {
        Vector3 rootOrigin = weaponRoot.GlobalTransform.Origin;
        Vector2 sourceXZ = new(rootOrigin.X, rootOrigin.Z);
        Vector2 outward = _player != null && IsInstanceValid(_player)
            ? sourceXZ - _player.Position
            : sourceXZ;
        if (outward.LengthSquared() < 0.0001f)
        {
            outward = Vector2.Right;
        }

        outward = outward.Normalized();
        Vector2 tangent = new(-outward.Y, outward.X);

        AddArenaElectricGroundAnchor(anchors, sourceXZ.X, sourceXZ.Y);
        AddArenaElectricGroundAnchor(anchors, sourceXZ.X + outward.X * 1.10f, sourceXZ.Y + outward.Y * 1.10f);
        AddArenaElectricGroundAnchor(anchors, sourceXZ.X + outward.X * 1.95f + tangent.X * 0.72f, sourceXZ.Y + outward.Y * 1.95f + tangent.Y * 0.72f);
        AddArenaElectricGroundAnchor(anchors, sourceXZ.X + outward.X * 2.25f - tangent.X * 0.86f, sourceXZ.Y + outward.Y * 2.25f - tangent.Y * 0.86f);
        AddArenaElectricGroundAnchor(anchors, outward.X * (ArenaHalfW - 0.75f), outward.Y * (ArenaHalfH - 0.75f));
    }

    private void CollectArenaWeaponElectricCharacterAnchors(ICollection<Vector3> anchors)
    {
        if (_player != null && IsInstanceValid(_player))
        {
            Vector3 playerOrigin = _player.GlobalTransform.Origin;
            anchors.Add(playerOrigin + new Vector3(0f, -0.18f, 0.18f));
            anchors.Add(playerOrigin + new Vector3(0f, 0.95f, -0.08f));
            anchors.Add(playerOrigin + new Vector3(0.18f, 0.42f, 0f));
        }
    }

    private static void AddArenaElectricGroundAnchor(ICollection<Vector3> anchors, float x, float z)
    {
        anchors.Add(new Vector3(
            Mathf.Clamp(x, -ArenaHalfW + 0.45f, ArenaHalfW - 0.45f),
            0.02f,
            Mathf.Clamp(z, -ArenaHalfH + 0.45f, ArenaHalfH - 0.45f)));
    }

    private static ModifierVisualController.ElectricEnvironmentHit? ResolveArenaWeaponElectricEnvironmentCollision(Vector3 from, Vector3 to)
    {
        ModifierVisualController.ElectricEnvironmentHit? bestHit = TryResolveArenaElectricFloorHit(from, to);
        float bestDistance = bestHit.HasValue ? from.DistanceSquaredTo(bestHit.Value.Position) : float.PositiveInfinity;

        if (TryResolveArenaElectricWallHit(from, to, out ModifierVisualController.ElectricEnvironmentHit wallHit))
        {
            float wallDistance = from.DistanceSquaredTo(wallHit.Position);
            if (!bestHit.HasValue || wallDistance < bestDistance)
            {
                bestHit = wallHit;
            }
        }

        return bestHit;
    }

    private static ModifierVisualController.ElectricEnvironmentHit? TryResolveArenaElectricFloorHit(Vector3 from, Vector3 to)
    {
        float deltaY = to.Y - from.Y;
        if (Mathf.Abs(deltaY) < 0.0001f)
        {
            return null;
        }

        float t = (0f - from.Y) / deltaY;
        if (t < 0f || t > 1f)
        {
            return null;
        }

        Vector3 hitPosition = from.Lerp(to, t);
        if (hitPosition.X < -ArenaHalfW || hitPosition.X > ArenaHalfW || hitPosition.Z < -ArenaHalfH || hitPosition.Z > ArenaHalfH)
        {
            return null;
        }

        return new ModifierVisualController.ElectricEnvironmentHit(hitPosition, Vector3.Up);
    }

    private static bool TryResolveArenaElectricWallHit(Vector3 from, Vector3 to, out ModifierVisualController.ElectricEnvironmentHit hit)
    {
        hit = default;
        float bestT = float.PositiveInfinity;
        bool hasHit = false;

        if (TryResolveArenaWallPlaneHit(from, to, -ArenaHalfW, true, Vector3.Right, out ModifierVisualController.ElectricEnvironmentHit westHit, out float westT) && westT < bestT)
        {
            bestT = westT;
            hit = westHit;
            hasHit = true;
        }

        if (TryResolveArenaWallPlaneHit(from, to, ArenaHalfW, true, Vector3.Left, out ModifierVisualController.ElectricEnvironmentHit eastHit, out float eastT) && eastT < bestT)
        {
            bestT = eastT;
            hit = eastHit;
            hasHit = true;
        }

        if (TryResolveArenaWallPlaneHit(from, to, -ArenaHalfH, false, Vector3.Back, out ModifierVisualController.ElectricEnvironmentHit northHit, out float northT) && northT < bestT)
        {
            bestT = northT;
            hit = northHit;
            hasHit = true;
        }

        if (TryResolveArenaWallPlaneHit(from, to, ArenaHalfH, false, Vector3.Forward, out ModifierVisualController.ElectricEnvironmentHit southHit, out float southT) && southT < bestT)
        {
            hit = southHit;
            hasHit = true;
        }

        return hasHit;
    }

    private static bool TryResolveArenaWallPlaneHit(
        Vector3 from,
        Vector3 to,
        float plane,
        bool useX,
        Vector3 normal,
        out ModifierVisualController.ElectricEnvironmentHit hit,
        out float t)
    {
        hit = default;
        t = 0f;

        float startCoord = useX ? from.X : from.Z;
        float endCoord = useX ? to.X : to.Z;
        float delta = endCoord - startCoord;
        if (Mathf.Abs(delta) < 0.0001f)
        {
            return false;
        }

        t = (plane - startCoord) / delta;
        if (t < 0f || t > 1f)
        {
            return false;
        }

        Vector3 candidate = from.Lerp(to, t);
        if (candidate.Y < 0f || candidate.Y > ArenaLayout.WallHeight)
        {
            return false;
        }

        float otherCoord = useX ? candidate.Z : candidate.X;
        if (useX)
        {
            if (otherCoord < -ArenaHalfH || otherCoord > ArenaHalfH)
            {
                return false;
            }
        }
        else if (otherCoord < -ArenaHalfW || otherCoord > ArenaHalfW)
        {
            return false;
        }

        hit = new ModifierVisualController.ElectricEnvironmentHit(candidate, normal);
        return true;
    }

    private void TickArenaWeaponVisual(ArenaWeaponVisual visual, float deltaSeconds, bool hasCombatTarget, Vector2 targetPosition)
    {
        float combatBlend = visual.IsExiting ? 0f : _arenaWeaponCombatBlend;
        float transitionTarget = visual.IsExiting ? 0f : 1f;
        float transitionWeight = GetArenaWeaponSmoothingWeight(ArenaWeaponTransitionSharpness, deltaSeconds);
        visual.Transition = Mathf.Lerp(visual.Transition, transitionTarget, transitionWeight);
        float transitionT = Mathf.Clamp(visual.Transition, 0f, 1f);
        float easedTransition = transitionT * transitionT * (3f - 2f * transitionT);

        float offsetScale = Mathf.Lerp(1.08f, 0.94f, combatBlend);
        Vector2 formationOffset = visual.Offset * offsetScale;
        float driftRadius = ArenaWeaponDriftRadius * Mathf.Lerp(1.18f, 0.64f, combatBlend);
        Vector2 drift = new Vector2(
            Mathf.Sin(_arenaWeaponVisualTime * (1.07f + visual.SlotIndex * 0.035f) + visual.DriftPhase),
            Mathf.Cos(_arenaWeaponVisualTime * (0.89f + visual.SlotIndex * 0.028f) + visual.DriftPhase * 1.29f)) * driftRadius;
        drift += new Vector2(
            Mathf.Cos(_arenaWeaponVisualTime * 1.58f + visual.TiltPhase),
            Mathf.Sin(_arenaWeaponVisualTime * 1.24f + visual.HeightPhase)) * (driftRadius * 0.38f);

        Vector2 formationAnchor = RuntimeMath.FiniteOr(_player.Position + formationOffset + drift, _player.Position + formationOffset);
        Vector2 targetAnchor = !visual.IsExiting && HasWingModulatorKey(visual.Item.Modulators, WingBrotherModulatorKey)
            ? GetWingBrotherAnchor(visual, formationAnchor)
            : formationAnchor;
        if (visual.IsExiting)
        {
            Vector2 exitDirection = RuntimeMath.SafeDirection(visual.Offset, visual.DisplayAimDirection);
            targetAnchor = RuntimeMath.FiniteOr(visual.AnchorPosition2D + exitDirection * (2.4f * deltaSeconds), visual.AnchorPosition2D);
        }

        if (!visual.HasPosition)
        {
            visual.AnchorPosition2D = visual.IsExiting ? visual.Position2D : _player.Position;
            visual.HasPosition = true;
        }

        float followSharpness = ArenaWeaponFollowSharpness * Mathf.Lerp(0.78f, 1.18f, combatBlend);
        float followWeight = GetArenaWeaponSmoothingWeight(followSharpness, deltaSeconds);
        visual.AnchorPosition2D = RuntimeMath.FiniteOr(visual.AnchorPosition2D.Lerp(targetAnchor, followWeight), targetAnchor);

        Vector2 outward = RuntimeMath.SafeDirection(visual.Offset, _arenaWeaponLastAimDirection);
        float idleTwist = Mathf.Sin(_arenaWeaponVisualTime * 0.92f + visual.TiltPhase) * 0.20f;
        Vector2 restDirection = RuntimeMath.SafeDirection(outward.Rotated(idleTwist), outward);

        Vector2 desiredAimDirection;
        if (visual.IsExiting)
        {
            desiredAimDirection = visual.DisplayAimDirection;
        }
        else if (HasWingModulatorKey(visual.Item.Modulators, WingBrotherModulatorKey)
            && TryResolveWingBrotherAimDirection(visual, restDirection, out Vector2 brotherAimDirection))
        {
            desiredAimDirection = brotherAimDirection;
        }
        else
        {
            desiredAimDirection = restDirection;
        }

        float aimSharpness = ArenaWeaponAimSharpness * Mathf.Lerp(0.82f, 1.45f, combatBlend);
        if (visual.Recoil > 0.01f)
        {
            aimSharpness *= 1.18f;
        }

        float aimWeight = GetArenaWeaponSmoothingWeight(aimSharpness, deltaSeconds);
        visual.DisplayAimDirection = RuntimeMath.SafeDirection(visual.DisplayAimDirection.Lerp(desiredAimDirection, aimWeight), desiredAimDirection);
        visual.Recoil = Mathf.Lerp(visual.Recoil, 0f, GetArenaWeaponSmoothingWeight(ArenaWeaponRecoilReturnSharpness, deltaSeconds));

        Vector2 recoilOffset = -visual.DisplayAimDirection * (ArenaWeaponRecoilDistance * visual.Recoil);
        visual.Position2D = RuntimeMath.FiniteOr(visual.AnchorPosition2D + recoilOffset, visual.AnchorPosition2D);

        float bobAmplitude = ArenaWeaponBobHeight * (0.82f + 0.12f * Mathf.Sin(visual.HeightPhase));
        float bob = Mathf.Sin(_arenaWeaponVisualTime * (2.18f + visual.SlotIndex * 0.065f) + visual.HeightPhase) * bobAmplitude;
        bob += Mathf.Cos(_arenaWeaponVisualTime * (1.33f + visual.SlotIndex * 0.035f) + visual.HeightPhase * 1.17f) * (bobAmplitude * 0.42f);

        float pulse = 1f + Mathf.Sin(_arenaWeaponVisualTime * (1.74f + visual.SlotIndex * 0.04f) + visual.TiltPhase) * 0.03f;
        float stanceScale = Mathf.Lerp(0.96f, 1.05f, combatBlend);
        float scale = Mathf.Lerp(0.55f, 1f, easedTransition) * stanceScale * pulse;
        float transitionRise = (1f - easedTransition) * (visual.IsExiting ? ArenaWeaponExitRiseHeight : ArenaWeaponEquipRiseHeight);
        float stanceHeight = Mathf.Lerp(ArenaWeaponFloatHeight - 0.06f, ArenaWeaponFloatHeight + 0.24f, combatBlend);

        visual.Root.Position = new Vector3(visual.Position2D.X, stanceHeight + bob + transitionRise, visual.Position2D.Y);
        visual.Root.Scale = Vector3.One * scale;

        float aimYaw = Mathf.Atan2(restDirection.X, restDirection.Y);
        float pitch = Mathf.DegToRad(Mathf.Sin(_arenaWeaponVisualTime * 1.42f + visual.TiltPhase) * Mathf.Lerp(7f, 3.6f, combatBlend));
        pitch += Mathf.DegToRad(-5.5f * combatBlend + 11f * visual.Recoil);
        float roll = Mathf.DegToRad(Mathf.Cos(_arenaWeaponVisualTime * 1.15f + visual.TiltPhase * 1.33f) * Mathf.Lerp(9f, 4.2f, combatBlend));
        visual.Root.Rotation = new Vector3(pitch, aimYaw, roll);

        if (!visual.UsesNativeAnimation)
        {
            float spin = _arenaWeaponVisualTime * (ArenaWeaponSpinSpeed + visual.SlotIndex * 0.045f) * (1f - combatBlend * 0.58f);
            visual.Model.Rotation = visual.BaseModelRotation + new Vector3(0f, spin, 0f);
        }
        else
        {
            visual.Model.Rotation = visual.BaseModelRotation;
        }

        visual.GlowLight.LightColor = ResolveArenaWeaponTint(visual.Item).Lerp(Colors.White, 0.10f + combatBlend * 0.10f + visual.Recoil * 0.08f);
        visual.GlowLight.LightEnergy = Mathf.Lerp(1.05f, 2.15f, combatBlend) * easedTransition * (1f + visual.Recoil * 0.42f);

        if (visual.ModifierVisual != null && IsInstanceValid(visual.ModifierVisual))
        {
            visual.ModifierVisual.Intensity = Mathf.Lerp(0.82f, 1.12f, combatBlend) * Mathf.Lerp(0.72f, 1f, easedTransition);
        }
    }

    private Vector2 GetWingBrotherAnchor(ArenaWeaponVisual visual, Vector2 formationAnchor)
    {
        Vector2 searchOrigin = visual.HasPosition ? visual.Position2D : formationAnchor;
        if (!TryFindNearestTarget(searchOrigin, ArenaWeaponBrotherTargetRange, out Vector2 targetPosition))
        {
            float roamAngle = _arenaWeaponVisualTime * 0.92f + visual.SlotIndex * 1.21f;
            Vector2 roamOffset = AngleToDir(roamAngle) * (ArenaWeaponOrbitRadius + 6f);
            return ClampWingBrotherAnchor(formationAnchor + roamOffset * 0.35f);
        }

        float orbitAngle = _arenaWeaponVisualTime * 1.35f + visual.SlotIndex * 1.19f;
        float orbitRadius = ArenaWeaponBrotherOrbitRadius + Mathf.Sin(_arenaWeaponVisualTime * 1.75f + visual.SlotIndex) * 1.2f;
        Vector2 orbitOffset = AngleToDir(orbitAngle) * orbitRadius;
        return ClampWingBrotherAnchor(targetPosition + orbitOffset);
    }

    private bool TryResolveWingBrotherAimDirection(ArenaWeaponVisual visual, Vector2 fallbackDirection, out Vector2 aimDirection)
    {
        Vector2 searchOrigin = visual.HasPosition ? visual.Position2D : _player.Position;
        if (TryFindNearestTarget(searchOrigin, ArenaWeaponBrotherTargetRange, out Vector2 targetPosition))
        {
            aimDirection = RuntimeMath.SafeDirection(targetPosition - searchOrigin, fallbackDirection);
            return true;
        }

        aimDirection = fallbackDirection;
        return false;
    }

    private Vector2 ClampWingBrotherAnchor(Vector2 position)
    {
        Vector2 tether = position - _player.Position;
        float leashDistanceSquared = ArenaWeaponBrotherLeashDistance * ArenaWeaponBrotherLeashDistance;
        if (tether.LengthSquared() > leashDistanceSquared)
        {
            position = _player.Position + tether.Normalized() * ArenaWeaponBrotherLeashDistance;
        }

        return ClampResolve(position, 0.9f);
    }

    private void ClearArenaWeaponVisuals()
    {
        foreach (ArenaWeaponVisual visual in _arenaWeaponVisualsBySlot.Values)
        {
            QueueFreeIfValid(visual.Root);
        }

        for (int visualIndex = 0; visualIndex < _arenaWeaponExitingVisuals.Count; visualIndex++)
        {
            QueueFreeIfValid(_arenaWeaponExitingVisuals[visualIndex].Root);
        }

        _arenaWeaponVisualsBySlot.Clear();
        _arenaWeaponExitingVisuals.Clear();
    }

    private void StartArenaWeaponExit(ArenaWeaponVisual visual)
    {
        if (visual.IsExiting)
        {
            return;
        }

        visual.IsExiting = true;
        _arenaWeaponExitingVisuals.Add(visual);
    }

    private void UpdateArenaWeaponCombatState(float deltaSeconds, out bool hasCombatTarget, out Vector2 targetPosition)
    {
        hasCombatTarget = TryFindCurrentWeaponTarget(out Vector2 liveTargetPosition);
        targetPosition = liveTargetPosition;

        if (hasCombatTarget)
        {
            _arenaWeaponLastAimDirection = RuntimeMath.SafeDirection(liveTargetPosition - _player.Position, _arenaWeaponLastAimDirection);
        }
        else if (_playerVel.LengthSquared() > 1f)
        {
            _arenaWeaponLastAimDirection = RuntimeMath.SafeDirection(_playerVel, _arenaWeaponLastAimDirection);
        }
        else
        {
            targetPosition = _player.Position + _arenaWeaponLastAimDirection * 24f;
        }

        if (_arenaWeaponCombatHoldTimer > 0f)
        {
            _arenaWeaponCombatHoldTimer = Mathf.Max(0f, _arenaWeaponCombatHoldTimer - deltaSeconds);
        }

        bool combatActive = hasCombatTarget || _arenaWeaponCombatHoldTimer > 0f;
        _arenaWeaponCombatBlend = Mathf.Lerp(
            _arenaWeaponCombatBlend,
            combatActive ? 1f : 0f,
            GetArenaWeaponSmoothingWeight(ArenaWeaponCombatSharpness, deltaSeconds));
    }

    private static float GetArenaWeaponSmoothingWeight(float sharpness, float deltaSeconds)
    {
        return Mathf.Clamp(1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * Mathf.Max(0f, deltaSeconds)), 0f, 1f);
    }

    private static Vector2[] BuildArenaWeaponOffsets(int weaponCount)
    {
        float[] clockPositions = weaponCount switch
        {
            1 => new[] { 6f },
            2 => new[] { 9f, 3f },
            3 => new[] { 10f, 2f, 6f },
            4 => new[] { 10.5f, 1.5f, 7.5f, 4.5f },
            5 => new[] { 2f, 4f, 6f, 8f, 10f },
            _ => System.Array.Empty<float>(),
        };

        Vector2[] offsets = new Vector2[clockPositions.Length];
        for (int clockIndex = 0; clockIndex < clockPositions.Length; clockIndex++)
        {
            offsets[clockIndex] = ClockDirection(clockPositions[clockIndex]) * ArenaWeaponOrbitRadius;
        }

        return offsets;
    }

    private static Vector2 ClockDirection(float clockHour)
    {
        float normalizedHour = Mathf.PosMod(clockHour, 12f);
        float angle = normalizedHour / 12f * Mathf.Tau;
        return new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
    }

    private static Color ResolveArenaWeaponTint(EquipmentItem item)
    {
        if (item.Rarity != null)
        {
            Color rarityColor = item.Rarity.UiColor;
            rarityColor.A = 1f;
            return rarityColor;
        }

        return new Color(0.2f, 0.95f, 1f, 1f);
    }
}