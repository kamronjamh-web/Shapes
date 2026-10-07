using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsDefenseCore : StaticBody3D
{
	[Export] public float MaxHealth { get; set; } = 600f;
	public float CurrentHealth { get; private set; }
	public bool IsAlive => CurrentHealth > 0f;
	public event Action? Destroyed;
	private MeshInstance3D _crystal = null!;
	private StandardMaterial3D _material = null!;

	public override void _Ready()
	{
		MaxHealth = Mathf.Max(1f, MaxHealth);
		CurrentHealth = MaxHealth;
		AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 1.25f } });
		_material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.20f, 0.92f, 1f), EmissionEnabled = true,
			Emission = new Color(0.12f, 0.65f, 1f), EmissionEnergyMultiplier = 1.8f,
			Metallic = 0.5f, Roughness = 0.25f,
		};
		_crystal = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(1.9f, 1.9f, 1.9f) },
			RotationDegrees = new Vector3(24f, 45f, 16f), MaterialOverride = _material,
		};
		AddChild(_crystal);
	}

	public override void _Process(double delta)
	{
		_crystal.RotateY((float)delta * 0.25f);
		Color color = new Color(1f, 0.22f, 0.12f).Lerp(new Color(0.12f, 0.80f, 1f), CurrentHealth / MaxHealth);
		_material.AlbedoColor = color;
		_material.Emission = color;
	}

	public void ApplyDamage(float damage)
	{
		if (!IsAlive || damage <= 0f) return;
		CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
		if (!IsAlive) Destroyed?.Invoke();
	}

	public void Repair(float amount)
	{
		if (IsAlive) CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + Mathf.Max(0f, amount));
	}

	public void Retune(float maxHealth)
	{
		float ratio = CurrentHealth / Mathf.Max(1f, MaxHealth);
		MaxHealth = Mathf.Max(1f, maxHealth);
		CurrentHealth = MaxHealth * ratio;
	}
}
