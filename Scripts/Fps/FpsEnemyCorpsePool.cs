using System;
using System.Collections.Generic;
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
	public int Capacity => Math.Clamp(Settings.CorpseCapacity, 1, 256);
	private readonly List<Corpse> _slots = new();
	private readonly List<PhysicsBody3D> _ignoredBodies = new();
	private long _sequence;
	private bool _running = true;
	private Mesh? _cubeMesh;
	private Mesh? _pyramidMesh;

	private sealed class Corpse
	{
		public RigidBody3D Body = null!;
		public MeshInstance3D Visual = null!;
		public CollisionShape3D Collider = null!;
		public StandardMaterial3D Material = null!;
		public readonly BoxShape3D Box = new();
		public readonly ConvexPolygonShape3D Pyramid = new();
		public bool Active;
		public long Sequence;
		public float Remaining, FatalDamage, Overkill;
		public FpsHordeHandle EnemyHandle;
		public FpsEnemyRole Role;
		public Vector3 InitialVelocity, SavedLinearVelocity, SavedAngularVelocity;
	}

	public void IgnoreBody(PhysicsBody3D body)
	{
		if (!IsInstanceValid(body) || _ignoredBodies.Contains(body)) return;
		_ignoredBodies.Add(body);
		foreach (Corpse corpse in _slots) corpse.Body.AddCollisionExceptionWith(body);
	}

	public void SpawnCorpse(FpsHordeDeath death)
	{
		if (!Settings.CorpseEnabled || !IsInsideTree()) return;
		TrimCapacity();
		Corpse? corpse = null;
		foreach (Corpse candidate in _slots)
			if (!candidate.Active) { corpse = candidate; break; }
		if (corpse == null && _slots.Count < Capacity)
		{
			corpse = CreateSlot(_slots.Count);
			_slots.Add(corpse);
		}
		if (corpse == null)
		{
			corpse = _slots[0];
			foreach (Corpse candidate in _slots) if (candidate.Sequence < corpse.Sequence) corpse = candidate;
			Retire(corpse);
		}
		Vector3 scale = death.VisualTransform.Basis.Scale.Abs();
		if (!Finite(scale) || scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f) return;
		corpse.Body.Freeze = true;
		corpse.Body.GlobalTransform = new Transform3D(death.VisualTransform.Basis.Orthonormalized(), death.VisualTransform.Origin);
		corpse.Visual.Scale = scale;
		corpse.Visual.Mesh = death.Enemy.Role == FpsEnemyRole.Striker ? (_pyramidMesh ??= CreatePyramid()) : (_cubeMesh ??= new BoxMesh { Size = Vector3.One });
		if (death.Enemy.Role == FpsEnemyRole.Striker)
		{
			corpse.Pyramid.Points = PyramidPoints(scale);
			corpse.Collider.Shape = corpse.Pyramid;
		}
		else
		{
			corpse.Box.Size = scale;
			corpse.Collider.Shape = corpse.Box;
		}
		Color color = death.BodyColor;
		corpse.Material.AlbedoColor = color;
		corpse.Material.Emission = color;
		corpse.Material.EmissionEnergyMultiplier = 0.08f;
		corpse.EnemyHandle = death.Enemy.Handle;
		corpse.Role = death.Enemy.Role;
		corpse.FatalDamage = Safe(death.FatalDamage, 0f, 0f, float.MaxValue);
		corpse.Overkill = Safe(death.Overkill, 0f, 0f, float.MaxValue);
		corpse.Remaining = Safe(Settings.CorpseLifetimeSeconds, 4f, 0.1f, 15f);
		corpse.Sequence = ++_sequence;
		Vector3 direction = Finite(death.TravelDirection) && death.TravelDirection.LengthSquared() > 0.000001f
			? death.TravelDirection.Normalized() : Vector3.Zero;
		float maximum = Safe(Settings.CorpseMaxLaunchSpeed, 24f, 0f, 40f);
		float speed = Mathf.Min(maximum, Safe(Settings.CorpseBaseLaunchSpeed, 3f, 0f, 24f)
			+ Mathf.Sqrt(corpse.Overkill) * Safe(Settings.CorpseOverkillSpeedScale, 1.5f, 0f, 5f));
		corpse.InitialVelocity = direction * speed + Vector3.Up * Safe(Settings.CorpseUpwardSpeed, 2f, 0f, 10f);
		corpse.SavedLinearVelocity = corpse.InitialVelocity;
		Vector3 tumbleAxis = direction.Cross(Vector3.Up);
		if (tumbleAxis.LengthSquared() < 0.001f) tumbleAxis = Vector3.Right;
		corpse.SavedAngularVelocity = (tumbleAxis.Normalized() + Vector3.Up * 0.25f).Normalized()
			* Safe(Settings.CorpseAngularSpeed, 5f, 0f, 20f);
		corpse.Collider.Disabled = false;
		corpse.Body.CollisionMask = 1;
		if (IsInstanceValid(PlayerBody)) corpse.Body.AddCollisionExceptionWith(PlayerBody!);
		corpse.Body.Visible = true;
		corpse.Body.Freeze = !_running;
		corpse.Body.Sleeping = false;
		corpse.Body.LinearVelocity = corpse.SavedLinearVelocity;
		corpse.Body.AngularVelocity = corpse.SavedAngularVelocity;
		corpse.Active = true;
		ActiveCount++;
	}

	public void Tick(float dt, bool running)
	{
		if (!Settings.CorpseEnabled) { Clear(); _running = running; return; }
		TrimCapacity();
		if (_running != running)
		{
			foreach (Corpse corpse in _slots)
			{
				if (!corpse.Active) continue;
				if (!running)
				{
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
				}
			}
			_running = running;
		}
		if (!running || !float.IsFinite(dt) || dt <= 0f) return;
		foreach (Corpse corpse in _slots)
		{
			if (!corpse.Active) continue;
			corpse.Remaining -= dt;
			if (corpse.Remaining <= 0f) Retire(corpse);
		}
	}

	public void Clear()
	{
		foreach (Corpse corpse in _slots) Retire(corpse);
		ActiveCount = 0;
	}

	public void CopyActiveSnapshots(List<FpsEnemyCorpseSnapshot> result)
	{
		result.Clear();
		for (int slot = 0; slot < _slots.Count; slot++)
		{
			Corpse corpse = _slots[slot];
			if (!corpse.Active) continue;
			result.Add(new(slot, corpse.EnemyHandle, corpse.Role, corpse.Body.GlobalTransform,
				_running ? corpse.Body.LinearVelocity : corpse.SavedLinearVelocity,
				_running ? corpse.Body.AngularVelocity : corpse.SavedAngularVelocity,
				corpse.InitialVelocity, corpse.Remaining, corpse.FatalDamage, corpse.Overkill, corpse.Body.Freeze));
		}
	}

	private Corpse CreateSlot(int index)
	{
		Corpse corpse = new();
		corpse.Body = new RigidBody3D
		{
			Name = $"Corpse{index:000}", CollisionLayer = 0, CollisionMask = 0,
			Freeze = true, Visible = false, ContinuousCd = true, Mass = 1f,
			LinearDamp = 0.15f, AngularDamp = 0.35f,
			PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.65f, Bounce = 0.12f },
		};
		corpse.Material = new StandardMaterial3D { Roughness = 0.7f, EmissionEnabled = true };
		corpse.Visual = new MeshInstance3D { MaterialOverride = corpse.Material };
		corpse.Collider = new CollisionShape3D { Disabled = true };
		corpse.Body.AddChild(corpse.Visual);
		corpse.Body.AddChild(corpse.Collider);
		AddChild(corpse.Body);
		foreach (PhysicsBody3D ignored in _ignoredBodies)
			if (IsInstanceValid(ignored)) corpse.Body.AddCollisionExceptionWith(ignored);
		return corpse;
	}

	private void TrimCapacity()
	{
		while (_slots.Count > Capacity)
		{
			Corpse corpse = _slots[^1];
			Retire(corpse);
			corpse.Body.QueueFree();
			_slots.RemoveAt(_slots.Count - 1);
		}
	}

	private void Retire(Corpse corpse)
	{
		if (corpse.Active) ActiveCount--;
		corpse.Active = false;
		corpse.Body.Freeze = true;
		corpse.Body.LinearVelocity = Vector3.Zero;
		corpse.Body.AngularVelocity = Vector3.Zero;
		corpse.Body.CollisionMask = 0;
		corpse.Collider.Disabled = true;
		corpse.Body.Visible = false;
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
