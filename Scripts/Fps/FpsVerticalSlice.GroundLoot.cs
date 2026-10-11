using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    private Node3D? _groundLootRoot;
    private FpsGroundItemCard? _groundItemCard;
    private DefensePickup? _focusedGroundPickup;
    private DefensePickup? _shownGroundPickup;
    private int _groundCardShapeCount = -1, _groundCardPartCount = -1;
    private readonly List<DefensePickup> _groundVisualCandidates = new();
    private readonly HashSet<DefensePickup> _groundVisualSelection = new();
    private FpsLootPillarRenderer? _lootPillarRenderer;
    private readonly List<FpsLootPillarInstance> _groundPillarInstances = new();
    private bool _groundPillarsDirty = true;
    private float _groundVisualRefresh;
    private int _groundLootSerial, _groundCoinCount;
    private bool _lootingAfterVictory;
    private const float GroundLootViewDistance = 120f;

    private Vector3 ResolveGroundLootPosition(Vector3 position)
    {
        position.Y = IsHillMap ? SampleHillCrowdSupportHeight(position, position, .12f, .3f) : 0f;
        return position;
    }

    private Vector3 PlayerGroundDropPosition()
    {
        if (_player == null || !IsInstanceValid(_player)) return DefenseSettings?.CorePosition ?? Vector3.Zero;
        Vector3 forward = -_player.Camera.GlobalBasis.Z;
        forward.Y = 0f;
        Vector3 start = _player.GlobalPosition;
        Vector3 end = start + forward.Normalized() * 1.3f;
        // Keep inventory drops on the player's side of a nearby wall.
        var query = PhysicsRayQueryParameters3D.Create(start + Vector3.Up * .4f, end + Vector3.Up * .4f, 1);
        query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
        if (GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0) end = start;
        return ResolveGroundLootPosition(end);
    }

    internal void SpawnGroundShape(EquipmentItem weapon) => AddDefensePickup(PlayerGroundDropPosition(), 0, weapon, null);
    internal void SpawnGroundModulator(WeaponModulatorData part) => AddDefensePickup(PlayerGroundDropPosition(), 0, null, part);

    private void BuildGroundLootHud(Control hudRoot)
    {
        _groundItemCard = new FpsGroundItemCard();
        hudRoot.AddChild(_groundItemCard);
    }

    private bool CanInteractGroundLoot => !_pauseRequested && !_weaponInspectionOpen && !_shopOpen
        && !_gameplayTuningVisible && !_debugPanelVisible && !_defeat && (!_victory || _lootingAfterVictory);

    private void TickGroundLoot(float dt)
    {
        if (_player == null || !IsInstanceValid(_player)) return;
        _groundVisualRefresh -= dt;
        if (_groundVisualRefresh <= 0f)
        {
            _groundVisualRefresh = .15f;
            RefreshGroundLootVisuals();
        }
        if (!CanInteractGroundLoot)
        {
            _focusedGroundPickup = null;
            _groundItemCard?.HideItem();
            return;
        }
        _focusedGroundPickup = FindFocusedGroundPickup();
        if (_focusedGroundPickup == null) { _groundItemCard?.HideItem(); return; }
        DefensePickup item = _focusedGroundPickup;
        int shapeCount = EquippedShapeCount + ReserveShapeCount;
        if (_shownGroundPickup == item && _groundItemCard?.Visible == true
            && _groundCardShapeCount == shapeCount && _groundCardPartCount == _recoveredModulators.Count) return;
        _shownGroundPickup = item;
        _groundCardShapeCount = shapeCount; _groundCardPartCount = _recoveredModulators.Count;
        bool full = item.Weapon != null ? EquippedShapeCount + ReserveShapeCount >= EquippedShapeCapacity + ReserveShapeCapacity
            : _recoveredModulators.Count >= StoredModulatorCapacity;
        _groundItemCard?.ShowItem(item.Weapon, item.Modulator, full
            ? "Inventory full · Tab to drop an item"
            : item.Weapon != null ? $"F · Pick up  |  Shapes {EquippedShapeCount + ReserveShapeCount}/7"
            : $"F · Pick up  |  Modulators {_recoveredModulators.Count}/100");
    }

    private DefensePickup? FindFocusedGroundPickup()
    {
        if (!CanInteractGroundLoot) return null;
        Vector3 origin = _player.Camera.GlobalPosition;
        Vector3 direction = -_player.Camera.GlobalBasis.Z;
        float reach = Mathf.Max(2.5f, DefenseSettings?.PickupRadius ?? 2.5f) + 1f;
        DefensePickup? nearest = null;
        float best = float.MaxValue;
        foreach (DefensePickup pickup in _defensePickups)
        {
            if (pickup.Weapon == null && pickup.Modulator == null) continue;
            Vector3 target = pickup.DropBody != null && IsInstanceValid(pickup.DropBody)
                ? pickup.DropBody.GlobalPosition : pickup.Position + Vector3.Up * .45f;
            Vector3 offset = target - origin;
            float depth = offset.Dot(direction);
            if (depth <= .1f || offset.LengthSquared() > reach * reach) continue;
            // Aim at the item's model, with a little forgiveness for narrow cartridges.
            float miss = (offset - direction * depth).LengthSquared();
            if (miss > .45f * .45f) continue;
            float score = miss + depth * .015f;
            if (score >= best) continue;
            var query = PhysicsRayQueryParameters3D.Create(origin, target, 1);
            query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
            if (GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0) continue;
            nearest = pickup; best = score;
        }
        return nearest;
    }

    private bool TryInteractGroundLoot()
    {
        // Re-evaluate at the key press so stale focus cannot collect through a wall or menu.
        DefensePickup? pickup = FindFocusedGroundPickup();
        if (pickup == null) return false;
        CollectDefensePickup(pickup);
        TickGroundLoot(0f);
        return true;
    }

    private void RefreshGroundLootVisuals()
    {
        _groundLootRoot ??= new Node3D { Name = "GroundLoot" };
        if (_groundLootRoot.GetParent() == null) AddChild(_groundLootRoot);
        RefreshGroundLootPillars();
        _groundVisualCandidates.Clear();
        foreach (DefensePickup item in _defensePickups)
        {
            item.DistanceSquared = item.Position.DistanceSquaredTo(_player.GlobalPosition);
            if (item.DistanceSquared <= GroundLootViewDistance * GroundLootViewDistance) _groundVisualCandidates.Add(item);
        }
        _groundVisualCandidates.Sort((a, b) => {
            int equipment = (a.Coins > 0).CompareTo(b.Coins > 0);
            if (equipment != 0) return equipment;
            int distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            return distance != 0 ? distance : a.Id.CompareTo(b.Id);
        });
        _groundVisualSelection.Clear();
        int budget = Mathf.Clamp(DefenseSettings?.MaxLoosePickups ?? 128, 8, 256);
        for (int index = 0; index < Mathf.Min(budget, _groundVisualCandidates.Count); index++)
            _groundVisualSelection.Add(_groundVisualCandidates[index]);
        int created = 0;
        foreach (DefensePickup item in _defensePickups)
        {
            if (!_groundVisualSelection.Contains(item) && item.Visual != null)
            { item.Visual.QueueFree(); item.Visual = null; }
        }
        foreach (DefensePickup item in _groundVisualCandidates)
        {
            if (!_groundVisualSelection.Contains(item)) break;
            if (item.Visual != null || created >= 8) continue;
            if (item.Weapon != null || item.Modulator != null)
            {
                item.Visual = FpsGroundItemVisual.Create(item.Weapon, item.Modulator);
                _groundLootRoot.AddChild(item.Visual);
                item.Visual.Position = item.Position;
                item.DropVisualCenter = FpsWeaponVisualFactory.GetVisualBounds(item.Visual)?.GetCenter() ?? Vector3.Up * .4f;
                SyncGroundLootBodyVisual(item);
            }
            else item.Visual = AddDefenseVisual(new SphereMesh { Radius = .22f, Height = .44f, RadialSegments = 8, Rings = 4 },
                item.Position + Vector3.Up * .22f, new Color(1f, .76f, .25f), parent: _groundLootRoot);
            created++;
        }
    }

    private void RegroundEquipmentLoot()
    {
        while (_airborneGroundLoot.Count > 0) SettleGroundLootBody(_airborneGroundLoot[^1]);
        foreach (DefensePickup pickup in _defensePickups)
        {
            pickup.Position = ResolveSettledLootSupport(pickup.Position);
            if (pickup.Visual != null) pickup.Visual.Position = pickup.Position + (pickup.Coins > 0 ? Vector3.Up * .22f : Vector3.Zero);
        }
        _groundVisualRefresh = 0f;
        _groundPillarsDirty = true;
    }

    private void RefreshGroundLootPillars()
    {
        if (!_groundPillarsDirty) return;
        _lootPillarRenderer ??= new FpsLootPillarRenderer { Name = "LootPillars" };
        if (_lootPillarRenderer.GetParent() == null) _groundLootRoot!.AddChild(_lootPillarRenderer);
        _groundPillarInstances.Clear();
        foreach (DefensePickup item in _defensePickups)
        {
            if (item.Weapon == null && item.Modulator == null) continue;
            // Stable phase avoids synchronized flicker and survives model culling/recreation.
            float phase = (item.Id * 0.61803398875f) % 1f;
            _groundPillarInstances.Add(FpsGroundItemVisual.GetPillarInstance(item.Weapon, item.Modulator, item.Position, phase)
                with { EmitSparks = item.DropBody == null });
        }
        _lootPillarRenderer.SetPillars(_groundPillarInstances);
        _groundPillarsDirty = false;
    }
}
