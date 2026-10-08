using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsEnemyProjectile : Node3D
{
	public float Speed { get; private set; } = 24f;
	public float Damage { get; private set; } = 10f;

	private readonly Godot.Collections.Array<Rid> _excludeRids = new();
	private FpsPlayerController? _player;
	private MeshInstance3D _mesh = null!;
	private bool _combatEnabled = true;
	private Vector3 _direction = Vector3.Forward;
	private float _lifeRemaining = 4f;

	public override void _Ready()
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = new Color(1f, 0.56f, 0.18f, 1f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.52f, 0.12f, 1f),
			EmissionEnergyMultiplier = 1.7f,
			Metallic = 0.02f,
			Roughness = 0.25f,
		};

		_mesh = new MeshInstance3D
		{
			Mesh = new SphereMesh
			{
				Radius = 0.14f,
				Height = 0.28f,
			},
			MaterialOverride = material,
		};
		AddChild(_mesh);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_combatEnabled || _player == null)
		{
			return;
		}

		float dt = (float)delta;
		Vector3 start = GlobalPosition;
		Vector3 end = start + (_direction * Speed * dt);

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(start, end);
		query.CollideWithAreas = false;
		query.Exclude = _excludeRids;
		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count > 0)
		{
			GodotObject? collider = result["collider"].AsGodotObject();
			if (collider == _player)
			{
				_player.ApplyDamage(Damage);
			}
			else if (collider is FpsDefenseCore core)
			{
				core.ApplyDamage(Damage);
			}
			else if (collider is FpsDefenseGate gate)
			{
				gate.ApplyEnemyDamage(Damage);
			}

			QueueFree();
			return;
		}

		GlobalPosition = end;
		_lifeRemaining -= dt;
		if (_lifeRemaining <= 0f)
		{
			QueueFree();
		}
	}

	public void Configure(FpsPlayerController player, Vector3 direction, float speed, float damage, Rid ignoredRid)
	{
		_player = player;
		_direction = direction.LengthSquared() <= 0.0001f ? Vector3.Forward : direction.Normalized();
		Speed = Mathf.Max(1f, speed);
		Damage = Mathf.Max(0f, damage);
		_excludeRids.Clear();
		_excludeRids.Add(ignoredRid);
	}

	public void SetCombatEnabled(bool enabled)
	{
		_combatEnabled = enabled;
	}
}
