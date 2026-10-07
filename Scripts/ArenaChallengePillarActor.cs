using Godot;

namespace SuperGatchaRebuild;

public partial class ArenaChallengePillarActor : Node3D
{
	public const float ActivationRadius = 7.5f;

	private Vector2 _position2D = Vector2.Zero;
	private float _time;
	private bool _activated;
	private bool _completed;

	private MeshInstance3D _pedestal = null!;
	private MeshInstance3D _beam = null!;
	private MeshInstance3D _core = null!;
	private MeshInstance3D _halo = null!;
	private StandardMaterial3D _pedestalMaterial = null!;
	private StandardMaterial3D _beamMaterial = null!;
	private StandardMaterial3D _coreMaterial = null!;
	private StandardMaterial3D _haloMaterial = null!;
	private OmniLight3D _light = null!;

	public float Radius => ActivationRadius;
	public bool IsActivated => _activated;
	public bool IsCompleted => _completed;

	public new Vector2 Position
	{
		get => _position2D;
		set
		{
			if (!RuntimeMath.IsFinite(value))
			{
				return;
			}

			_position2D = value;
			base.Position = new Vector3(value.X, 0f, value.Y);
		}
	}

	public override void _Ready()
	{
		BuildVisuals();
		Position = _position2D;
		UpdateVisuals();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		UpdateVisuals();
	}

	public void Configure(Vector2 position)
	{
		Position = position;
	}

	public void Activate()
	{
		if (_activated || _completed)
		{
			return;
		}

		_activated = true;
		UpdateVisuals();
	}

	public void Complete()
	{
		if (_completed)
		{
			return;
		}

		_completed = true;
		_activated = false;
		UpdateVisuals();
	}

	private void BuildVisuals()
	{
		_pedestalMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.09f, 0.16f, 0.20f, 1f),
			Metallic = 0.25f,
			Roughness = 0.28f,
			EmissionEnabled = true,
			Emission = new Color(0.20f, 0.92f, 0.80f, 1f),
			EmissionEnergyMultiplier = 0.9f,
		};

		_beamMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.24f, 1f, 0.84f, 0.26f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = new Color(0.24f, 1f, 0.84f, 1f),
			EmissionEnergyMultiplier = 1.55f,
			Roughness = 0.18f,
		};

		_coreMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.88f, 1f, 0.98f, 1f),
			Metallic = 0.15f,
			Roughness = 0.16f,
			EmissionEnabled = true,
			Emission = new Color(0.30f, 1f, 0.90f, 1f),
			EmissionEnergyMultiplier = 2.2f,
		};

		_haloMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.18f, 0.95f, 0.76f, 0.24f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = new Color(0.18f, 0.95f, 0.76f, 1f),
			EmissionEnergyMultiplier = 1.25f,
			Roughness = 0.12f,
		};

		_pedestal = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 2.9f,
				BottomRadius = 3.4f,
				Height = 1.4f,
			},
			MaterialOverride = _pedestalMaterial,
			Position = new Vector3(0f, 0.7f, 0f),
		};
		AddChild(_pedestal);

		_beam = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 0.42f,
				BottomRadius = 0.76f,
				Height = 1f,
			},
			MaterialOverride = _beamMaterial,
		};
		AddChild(_beam);

		_core = new MeshInstance3D
		{
			Mesh = new SphereMesh
			{
				Radius = 1.05f,
				Height = 2.1f,
			},
			MaterialOverride = _coreMaterial,
		};
		AddChild(_core);

		_halo = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 4.4f,
				BottomRadius = 4.4f,
				Height = 0.08f,
			},
			MaterialOverride = _haloMaterial,
		};
		AddChild(_halo);

		_light = new OmniLight3D
		{
			Position = new Vector3(0f, 5.5f, 0f),
			LightColor = new Color(0.22f, 1f, 0.82f, 1f),
			LightEnergy = 1.9f,
			OmniRange = 24f,
			ShadowEnabled = false,
		};
		AddChild(_light);
	}

	private void UpdateVisuals()
	{
		if (_pedestalMaterial == null || _beamMaterial == null || _coreMaterial == null || _haloMaterial == null)
		{
			return;
		}

		Color accent;
		float pulseSpeed;
		float beamHeight;
		float lightEnergy;
		float beamAlpha;

		if (_completed)
		{
			accent = new Color(1f, 0.84f, 0.34f, 1f);
			pulseSpeed = 8.5f;
			beamHeight = 16f;
			lightEnergy = 2.8f;
			beamAlpha = 0.34f;
		}
		else if (_activated)
		{
			accent = new Color(1f, 0.56f, 0.22f, 1f);
			pulseSpeed = 6.8f;
			beamHeight = 13f;
			lightEnergy = 2.25f;
			beamAlpha = 0.28f;
		}
		else
		{
			accent = new Color(0.22f, 1f, 0.82f, 1f);
			pulseSpeed = 4.1f;
			beamHeight = 10f;
			lightEnergy = 1.7f;
			beamAlpha = 0.22f;
		}

		float pulse = 0.5f + 0.5f * Mathf.Sin(_time * pulseSpeed);
		float scalePulse = 1f + pulse * 0.12f;

		_pedestalMaterial.AlbedoColor = new Color(accent.R * 0.34f, accent.G * 0.34f, accent.B * 0.34f, 1f);
		_pedestalMaterial.Emission = accent;
		_pedestalMaterial.EmissionEnergyMultiplier = 0.65f + pulse * 0.45f;

		_beamMaterial.AlbedoColor = new Color(accent.R, accent.G, accent.B, beamAlpha + pulse * 0.08f);
		_beamMaterial.Emission = accent;
		_beamMaterial.EmissionEnergyMultiplier = lightEnergy;
		_beam.Position = new Vector3(0f, 0.7f + beamHeight * 0.5f, 0f);
		_beam.Scale = new Vector3(scalePulse, beamHeight, scalePulse);

		_coreMaterial.AlbedoColor = new Color(1f, 1f, 1f, 1f);
		_coreMaterial.Emission = accent;
		_coreMaterial.EmissionEnergyMultiplier = lightEnergy + pulse * 0.75f;
		_core.Position = new Vector3(0f, 2.6f + pulse * 0.45f, 0f);
		_core.Scale = Vector3.One * (1f + pulse * 0.15f);

		_haloMaterial.AlbedoColor = new Color(accent.R, accent.G, accent.B, 0.18f + pulse * 0.12f);
		_haloMaterial.Emission = accent;
		_haloMaterial.EmissionEnergyMultiplier = 1.1f + pulse * 0.45f;
		_halo.Position = new Vector3(0f, 0.22f + pulse * 0.12f, 0f);
		_halo.Scale = new Vector3(1.1f + pulse * 0.22f, 1f, 1.1f + pulse * 0.22f);
		_halo.Rotation = new Vector3(0f, _time * (_activated || _completed ? 2.4f : 1.2f), 0f);

		_light.LightColor = accent;
		_light.LightEnergy = lightEnergy + pulse * 0.55f;
		_light.OmniRange = _completed ? 30f : _activated ? 26f : 22f;
	}
}
