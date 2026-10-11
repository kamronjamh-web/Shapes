using System;
using System.Collections.Generic;
using BitOperations = System.Numerics.BitOperations;
using Godot;

namespace SuperGatchaRebuild.Fps;

public readonly record struct FpsEnemyCorpseSnapshot(
	int Slot, FpsHordeHandle EnemyHandle, FpsEnemyRole Role, Transform3D WorldTransform,
	Vector3 LinearVelocity, Vector3 AngularVelocity, Vector3 InitialLaunchVelocity,
	float RemainingLifetime, float FatalDamage, float Overkill, bool Frozen);

/// <summary>Cosmetic, bounded single-body tumbling; no gameplay collision layer or damage events.</summary>
public partial class FpsEnemyCorpsePool : Node3D
{
	public FpsHordeSettings Settings { get; set; } = new();
	public PhysicsBody3D? PlayerBody { get; set; }
	public int ActiveCount { get; private set; }
	public int Capacity => Math.Clamp(Settings.CorpseCapacity, 1, FpsHordeSettings.MaximumCorpseCapacity);
	private readonly List<Corpse> _slots = new();
	private readonly List<PhysicsBody3D> _ignoredBodies = new();
	private long _sequence;
	private bool _running = true;
	private bool _corpsesDisabled;
	private readonly ulong[] _freeSlots = new ulong[(FpsHordeSettings.MaximumCorpseCapacity + 63) / 64];
	private int _oldest = -1, _newest = -1;
	private readonly Dictionary<(bool Pyramid, Godot.Vector3 Size), Shape3D> _shapes = new();
	private PhysicsMaterial? _physicsMaterial;

	private sealed class Corpse
	{
		public RigidBody3D? Body;
		public CollisionShape3D? Collider;
		public Shape3D? Shape;
		public int Slot, Previous = -1, Next = -1;
		public Transform3D Pose;
		public Godot.Vector3 Size;
		public Color Color;
		public Color LinearColor;
		public bool WasSleeping;
		public bool Active;
		public long Sequence;
		public float Remaining, FatalDamage, Overkill;
		public FpsHordeHandle EnemyHandle;
		public FpsEnemyRole Role;
		public Godot.Vector3 InitialVelocity, SavedLinearVelocity, SavedAngularVelocity;
	}

	public void IgnoreBody(PhysicsBody3D body)
	{
		if (!IsInstanceValid(body) || _ignoredBodies.Contains(body)) return;
		_ignoredBodies.Add(body);
		foreach (Corpse corpse in _slots) corpse.Body?.AddCollisionExceptionWith(body);
	}

	public void SpawnCorpse(FpsHordeDeath death)
	{
		if (!Settings.CorpseEnabled || !IsInsideTree()) return;
		if (!_backendInitialized) EnsureCorpseBackend();
		Godot.Vector3 scale = death.VisualTransform.Basis.Scale.Abs();
		if (!Finite(scale) || scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f
			|| !death.VisualTransform.Origin.IsFinite() || !death.VisualTransform.Basis.IsFinite()) return;
		TrimCapacity();
		Corpse? corpse = null;
		int free = FindFreeSlot();
		if (free >= 0) corpse = _slots[free];
		if (corpse == null && _slots.Count < Capacity)
		{
			corpse = CreateSlot(_slots.Count);
			_slots.Add(corpse);
			SetFree(corpse.Slot, true);
		}
		if (corpse == null)
		{
			corpse = _slots[_oldest];
			Retire(corpse);
		}
		corpse.Pose = new Transform3D(death.VisualTransform.Basis.Orthonormalized(), death.VisualTransform.Origin);
		corpse.Size = scale;
		corpse.Color = death.BodyColor;
		corpse.LinearColor = death.BodyColor.SrgbToLinear();
		corpse.EnemyHandle = death.Enemy.Handle;
		corpse.Role = death.Enemy.Role;
		corpse.FatalDamage = Safe(death.FatalDamage, 0f, 0f, float.MaxValue);
		corpse.Overkill = Safe(death.Overkill, 0f, 0f, float.MaxValue);
		corpse.Remaining = Safe(Settings.CorpseLifetimeSeconds, 4f, 0.1f, 15f);
		corpse.Sequence = ++_sequence;
		Godot.Vector3 direction = Finite(death.TravelDirection) && death.TravelDirection.LengthSquared() > 0.000001f
			? death.TravelDirection.Normalized() : Godot.Vector3.Zero;
		float maximum = Safe(Settings.CorpseMaxLaunchSpeed, 24f, 0f, 40f);
		float speed = Mathf.Min(maximum, Safe(Settings.CorpseBaseLaunchSpeed, 3f, 0f, 24f)
			+ Mathf.Sqrt(corpse.Overkill) * Safe(Settings.CorpseOverkillSpeedScale, 1.5f, 0f, 5f));
		corpse.InitialVelocity = direction * speed + Godot.Vector3.Up * Safe(Settings.CorpseUpwardSpeed, 2f, 0f, 10f);
		corpse.SavedLinearVelocity = corpse.InitialVelocity;
		Godot.Vector3 tumbleAxis = direction.Cross(Godot.Vector3.Up);
		if (tumbleAxis.LengthSquared() < 0.001f) tumbleAxis = Godot.Vector3.Right;
		corpse.SavedAngularVelocity = (tumbleAxis.Normalized() + Godot.Vector3.Up * 0.25f).Normalized()
			* Safe(Settings.CorpseAngularSpeed, 5f, 0f, 20f);
		corpse.Active = true;
		corpse.WasSleeping = false;
		SetFree(corpse.Slot, false);
		corpse.Previous = _newest;
		corpse.Next = -1;
		if (_newest >= 0) _slots[_newest].Next = corpse.Slot;
		else _oldest = corpse.Slot;
		_newest = corpse.Slot;
		ActiveCount++;
		if (UsingWorker) QueueWorkerSpawn(corpse);
		else ActivateGodotBody(corpse);
		MarkVisualDirty();
	}

	public void Tick(float dt, bool running)
	{
		if (!Settings.CorpseEnabled)
		{
			if (!_corpsesDisabled) Clear();
			_corpsesDisabled = true;
			_corpseWorker?.Advance(0, false);
			_running = running;
			return;
		}
		_corpsesDisabled = false;
		EnsureCorpseBackend();
		TrimCapacity();
		if (_running != running)
		{
			foreach (Corpse corpse in _slots)
			{
				if (!corpse.Active || corpse.Body == null) continue;
				if (!running)
				{
					SyncGodotPose(corpse, true);
					corpse.SavedLinearVelocity = corpse.Body.LinearVelocity;
					corpse.SavedAngularVelocity = corpse.Body.AngularVelocity;
					corpse.Body.Freeze = true;
				}
				else
				{
					corpse.Body.Freeze = false;
					corpse.Body.Sleeping = false;
					corpse.Body.LinearVelocity = corpse.SavedLinearVelocity;
					corpse.Body.AngularVelocity = corpse.SavedAngularVelocity;
					corpse.WasSleeping = false;
				}
			}
			_running = running;
		}
		if (running && float.IsFinite(dt) && dt > 0f) foreach (Corpse corpse in _slots)
		{
			if (!corpse.Active) continue;
			corpse.Remaining -= dt;
			if (corpse.Remaining <= 0f) Retire(corpse);
		}
		FlushWorkerCommands();
		_corpseWorker?.Advance(dt, running);
	}

	public void Clear()
	{
		foreach (Corpse corpse in _slots) Retire(corpse);
		ClearWorkerCorpses();
		ActiveCount = 0;
		if (_corpseCubes != null) _corpseCubes.VisibleInstanceCount = 0;
		if (_corpsePyramids != null) _corpsePyramids.VisibleInstanceCount = 0;
	}

	public void CopyActiveSnapshots(List<FpsEnemyCorpseSnapshot> result)
	{
		result.Clear();
		for (int slot = 0; slot < _slots.Count; slot++)
		{
			Corpse corpse = _slots[slot];
			if (!corpse.Active) continue;
			SyncGodotPose(corpse, true);
			result.Add(new(slot, corpse.EnemyHandle, corpse.Role, corpse.Pose,
				_running && corpse.Body != null ? corpse.Body.LinearVelocity : corpse.SavedLinearVelocity,
				_running && corpse.Body != null ? corpse.Body.AngularVelocity : corpse.SavedAngularVelocity,
				corpse.InitialVelocity, corpse.Remaining, corpse.FatalDamage, corpse.Overkill, !_running));
		}
	}

	private Corpse CreateSlot(int index)
	{
		Corpse corpse = new() { Slot = index };
		if (!UsingWorker) EnsureGodotBody(corpse);
		return corpse;
	}

	private void EnsureGodotBody(Corpse corpse)
	{
		if (corpse.Body != null) return;
		corpse.Body = new RigidBody3D
		{
			Name = $"Corpse{corpse.Slot:000}", CollisionLayer = 0, CollisionMask = 0,
			Freeze = true, Visible = false, ContinuousCd = true, Mass = 1f,
			LinearDamp = 0.15f, AngularDamp = 0.35f,
			PhysicsMaterialOverride = _physicsMaterial ??= new PhysicsMaterial { Friction = 0.65f, Bounce = 0.12f },
		};
		corpse.Collider = new CollisionShape3D { Disabled = true };
		corpse.Body.AddChild(corpse.Collider);
		AddChild(corpse.Body);
		foreach (PhysicsBody3D ignored in _ignoredBodies)
			if (IsInstanceValid(ignored)) corpse.Body.AddCollisionExceptionWith(ignored);
	}

	private void ActivateGodotBody(Corpse corpse)
	{
		EnsureGodotBody(corpse);
		RigidBody3D body = corpse.Body!;
		body.Freeze = true;
		body.GlobalTransform = corpse.Pose;
		Shape3D shape = GetShape(corpse.Role == FpsEnemyRole.Striker, corpse.Size);
		if (corpse.Shape != shape) { corpse.Collider!.Shape = shape; corpse.Shape = shape; }
		corpse.Collider!.Disabled = false;
		body.CollisionMask = 1;
		if (IsInstanceValid(PlayerBody)) body.AddCollisionExceptionWith(PlayerBody!);
		body.Visible = true;
		body.Freeze = !_running;
		body.Sleeping = false;
		body.LinearVelocity = corpse.SavedLinearVelocity;
		body.AngularVelocity = corpse.SavedAngularVelocity;
		corpse.WasSleeping = false;
	}

	private void TrimCapacity()
	{
		while (_slots.Count > Capacity)
		{
			Corpse corpse = _slots[^1];
			Retire(corpse);
			corpse.Body?.QueueFree();
			SetFree(corpse.Slot, false);
			_slots.RemoveAt(_slots.Count - 1);
		}
	}

	private void Retire(Corpse corpse)
	{
		if (!corpse.Active) return;
		ActiveCount--;
		corpse.Active = false;
		if (corpse.Previous >= 0) _slots[corpse.Previous].Next = corpse.Next;
		else _oldest = corpse.Next;
		if (corpse.Next >= 0) _slots[corpse.Next].Previous = corpse.Previous;
		else _newest = corpse.Previous;
		corpse.Previous = corpse.Next = -1;
		SetFree(corpse.Slot, true);
		if (UsingWorker) _workerDirty[corpse.Slot] = true;
		if (corpse.Body != null)
		{
			corpse.Body.Freeze = true;
			corpse.Body.LinearVelocity = Vector3.Zero;
			corpse.Body.AngularVelocity = Vector3.Zero;
			corpse.Body.CollisionMask = 0;
			corpse.Collider!.Disabled = true;
			corpse.Body.Visible = false;
		}
		MarkVisualDirty();
	}

	private int FindFreeSlot()
	{
		// At most 33 words for the bounded 2056-slot pool; preserve the original
		// lowest-inactive-slot selection without a population-sized object scan.
		for (int word = 0; word < _freeSlots.Length; word++)
			if (_freeSlots[word] != 0) return word * 64 + BitOperations.TrailingZeroCount(_freeSlots[word]);
		return -1;
	}
	private void SetFree(int slot, bool free)
	{
		ulong bit = 1UL << (slot & 63);
		if (free) _freeSlots[slot >> 6] |= bit;
		else _freeSlots[slot >> 6] &= ~bit;
	}
	private Shape3D GetShape(bool pyramid, Vector3 size)
	{
		var key = (pyramid, size);
		if (_shapes.TryGetValue(key, out Shape3D? shape)) return shape;
		// Bound historical retuning keys. Existing colliders retain their own
		// reference; cached shapes are immutable and never resized on reuse.
		if (_shapes.Count >= FpsHordeSettings.MaximumCorpseCapacity) _shapes.Clear();
		shape = pyramid ? new ConvexPolygonShape3D { Points = PyramidPoints(size) } : new BoxShape3D { Size = size };
		_shapes.Add(key, shape);
		return shape;
	}

	private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	private static float Safe(float value, float fallback, float min, float max) => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
	private static Vector3[] PyramidPoints(Vector3 scale) =>
	[
		new Vector3(-0.5f, -0.5f, -0.5f) * scale, new Vector3(0.5f, -0.5f, -0.5f) * scale,
		new Vector3(0.5f, -0.5f, 0.5f) * scale, new Vector3(-0.5f, -0.5f, 0.5f) * scale,
		new Vector3(0, 0.5f, 0) * scale,
	];

	private static Mesh CreatePyramid()
	{
		using SurfaceTool tool = new();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		Vector3[] points = PyramidPoints(Vector3.One);
		foreach (int index in new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 0, 3, 2, 0, 2, 1 }) tool.AddVertex(points[index]);
		tool.GenerateNormals();
		return tool.Commit();
	}
}
