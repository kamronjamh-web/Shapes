using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    internal const int GroundLootPhysicsCapacity = 64;
    private const float GroundLootFlightSeconds = 2.5f;
    private readonly List<DefensePickup> _airborneGroundLoot = new();
    private readonly RandomNumberGenerator _lootMotionRng = new();
    private bool _lootMotionSeeded;
    private static SphereShape3D? _lootDropCollision;
    private static PhysicsMaterial? _lootDropPhysicsMaterial;

    private void SpawnBurstWeaponLoot(Vector3 position, EquipmentItem weapon)
    {
        // Cosmetic motion never consumes the reward RNG or changes the rolled item.
        if (!_lootMotionSeeded) { _lootMotionRng.Randomize(); _lootMotionSeeded = true; }
        AddDefensePickup(position, 0, weapon, null);
        DefensePickup pickup = _defensePickups[^1];
        while (_airborneGroundLoot.Count >= GroundLootPhysicsCapacity)
            SettleGroundLootBody(_airborneGroundLoot[0]);
        _groundLootRoot ??= new Node3D { Name = "GroundLoot" };
        if (_groundLootRoot.GetParent() == null) AddChild(_groundLootRoot);
        _lootDropCollision ??= new SphereShape3D { Radius = .4f };
        _lootDropPhysicsMaterial ??= new PhysicsMaterial { Bounce = .28f, Friction = .8f };
        RigidBody3D body = new()
        {
            Name = $"WeaponDropBody{pickup.Id}", Mass = .35f,
            CollisionLayer = 0, CollisionMask = 1, ContinuousCd = true,
            LinearDamp = .55f, AngularDamp = 1.4f,
            PhysicsMaterialOverride = _lootDropPhysicsMaterial,
        };
        body.AddChild(new CollisionShape3D { Shape = _lootDropCollision });
        // Configure the launch before entering physics, and keep it clear of terrain.
        body.Position = _groundLootRoot.ToLocal(new Vector3(position.X,
            Mathf.Max(position.Y + .65f, pickup.Position.Y + .65f), position.Z));
        float angle = _lootMotionRng.RandfRange(0f, Mathf.Tau);
        float speed = _lootMotionRng.RandfRange(2.5f, 5f);
        body.LinearVelocity = new Vector3(Mathf.Cos(angle) * speed, _lootMotionRng.RandfRange(3f, 5.5f), Mathf.Sin(angle) * speed);
        body.AngularVelocity = new Vector3(_lootMotionRng.RandfRange(-4f, 4f),
            _lootMotionRng.RandfRange(-5f, 5f), _lootMotionRng.RandfRange(-4f, 4f));
        _groundLootRoot.AddChild(body);
        if (_player != null && IsInstanceValid(_player)) body.AddCollisionExceptionWith(_player);
        foreach (FpsSliceEnemy enemy in _enemies)
            if (IsInstanceValid(enemy)) body.AddCollisionExceptionWith(enemy);
        pickup.DropBody = body;
        _airborneGroundLoot.Add(pickup);
        _groundVisualRefresh = 0f;
    }

    private void TickGroundLootPhysics(float dt)
    {
        bool paused = _pauseRequested || _defeat || (_victory && !_lootingAfterVictory);
        for (int index = _airborneGroundLoot.Count - 1; index >= 0; index--)
        {
            DefensePickup pickup = _airborneGroundLoot[index];
            RigidBody3D? body = pickup.DropBody;
            if (body == null || !IsInstanceValid(body)) { RemoveGroundLootBody(pickup); continue; }
            if (paused != pickup.DropBodyPaused)
            {
                if (paused)
                {
                    pickup.DropPausedVelocity = body.LinearVelocity;
                    pickup.DropPausedSpin = body.AngularVelocity;
                    body.Freeze = true;
                }
                else
                {
                    body.Freeze = false;
                    body.LinearVelocity = pickup.DropPausedVelocity;
                    body.AngularVelocity = pickup.DropPausedSpin;
                }
                pickup.DropBodyPaused = paused;
            }
            if (paused) continue;
            pickup.DropAge += Mathf.Max(0f, dt);
            Vector3 position = body.GlobalPosition;
            if (!position.IsFinite() || position.Y < -100f)
            { SettleGroundLootBody(pickup); continue; }
            Vector3 ground = ResolveGroundLootPosition(position);
            if (pickup.Position.DistanceSquaredTo(ground) > .0004f)
            {
                pickup.Position = ground;
                _groundPillarsDirty = true;
            }
            SyncGroundLootBodyVisual(pickup);
            if (pickup.DropAge >= GroundLootFlightSeconds || (pickup.DropAge > .35f && body.Sleeping))
                SettleGroundLootBody(pickup);
        }
    }

    private static void SyncGroundLootBodyVisual(DefensePickup pickup)
    {
        if (pickup.DropBody == null || pickup.Visual == null || !IsInstanceValid(pickup.DropBody)) return;
        Transform3D body = pickup.DropBody.GlobalTransform;
        pickup.Visual.GlobalTransform = new Transform3D(body.Basis, body.Origin - body.Basis * pickup.DropVisualCenter);
    }

    private void SettleGroundLootBody(DefensePickup pickup)
    {
        RigidBody3D? body = pickup.DropBody;
        if (body != null && IsInstanceValid(body) && body.GlobalPosition.IsFinite() && body.GlobalPosition.Y > -100f)
        {
            pickup.Position = ResolveSettledLootSupport(body.GlobalPosition);
        }
        RemoveGroundLootBody(pickup);
        if (pickup.Visual != null)
            pickup.Visual.GlobalTransform = new Transform3D(Basis.Identity, pickup.Position);
        _groundPillarsDirty = true;
        _groundVisualRefresh = 0f;
    }

    private void RemoveGroundLootBody(DefensePickup pickup)
    {
        if (pickup.DropBody != null && IsInstanceValid(pickup.DropBody))
        {
            pickup.DropBody.Freeze = true;
            pickup.DropBody.CollisionMask = 0;
            pickup.DropBody.QueueFree();
        }
        pickup.DropBody = null;
        _airborneGroundLoot.Remove(pickup);
    }

    private Vector3 ResolveSettledLootSupport(Vector3 position)
    {
        Vector3 terrain = ResolveGroundLootPosition(position);
        Vector3 from = new(position.X, Mathf.Max(position.Y, terrain.Y) + .5f, position.Z);
        var ray = PhysicsRayQueryParameters3D.Create(from, terrain + Vector3.Down * 64f, 1);
        // Actors share the world's collision layer, but are not permanent loot supports.
        var excluded = new Godot.Collections.Array<Rid>();
        if (_player != null && IsInstanceValid(_player)) excluded.Add(_player.GetRid());
        foreach (FpsSliceEnemy enemy in _enemies)
            if (IsInstanceValid(enemy)) excluded.Add(enemy.GetRid());
        ray.Exclude = excluded;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.Count > 0 && ((Vector3)hit["normal"]).Y > .25f ? (Vector3)hit["position"] : terrain;
    }
}
