using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>A friendly, destructible doorway obstacle. Its origin is the centre of its feet.</summary>
public partial class FpsDefenseGate : StaticBody3D
{
	[Signal] public delegate void GateDestroyedEventHandler(int sideIndex);
	[Export] public int SideIndex { get; set; }
	[Export] public bool AllowFriendlyDamage { get; set; }
	[Export] public float MaxHealth { get => _maxHealth; set => Retune(value); }
	[Export] public Vector3 Dimensions
	{
		get => _dimensions;
		set { _dimensions = SanitizeDimensions(value); if (_visualsBuilt) UpdateDimensions(); }
	}
	public float CurrentHealth { get; private set; }
	public bool IsAlive => CurrentHealth > 0f;
	public float HealthFraction => _healthInitialized ? Mathf.Clamp(CurrentHealth / _maxHealth, 0f, 1f) : 1f;
	public bool DebugInvulnerable { get; set; }
	public event Action<float, float>? HealthChanged;
	public event Action<float>? Damaged;
	public event Action? Destroyed;

	private float _maxHealth = 1000f;
	private Vector3 _dimensions = new(4.572f, 6.096f, 1f);
	private bool _healthInitialized, _visualsBuilt, _destroyedNotified;
	private bool? _collisionAlive;
	private CollisionShape3D _collider = null!;
	private MeshInstance3D _body = null!, _barBackground = null!, _barFill = null!;
	private Label3D _label = null!;
	private StandardMaterial3D _bodyMaterial = null!, _fillMaterial = null!;

	public override void _Ready()
	{
		if (!_healthInitialized) { CurrentHealth = _maxHealth; _healthInitialized = true; }
		CollisionLayer = 1;
		CollisionMask = 0;
		_bodyMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.24f, 0.38f, 0.48f), Roughness = 0.65f, Metallic = 0.45f,
		};
		_body = new MeshInstance3D { Name = "GatePanel", Mesh = new BoxMesh(), MaterialOverride = _bodyMaterial };
		_collider = new CollisionShape3D { Name = "GateCollision", Shape = new BoxShape3D() };
		AddChild(_body);
		AddChild(_collider);
		var backgroundMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.025f, 0.035f, 0.045f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
		};
		_fillMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.24f, 0.92f, 0.48f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
		};
		_barBackground = new MeshInstance3D { Name = "HealthBarBackground", Mesh = new BoxMesh(), MaterialOverride = backgroundMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_barFill = new MeshInstance3D { Name = "HealthBarFill", Mesh = new BoxMesh(), MaterialOverride = _fillMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_label = new Label3D
		{
			Name = "GateHealthLabel", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 40, PixelSize = 0.012f, OutlineSize = 8, NoDepthTest = false,
		};
		AddChild(_barBackground);
		AddChild(_barFill);
		AddChild(_label);
		_visualsBuilt = true;
		UpdateDimensions();
		UpdateHealthVisuals();
	}

	/// <summary>Imports a previous gate's fraction across replacement nodes; zero keeps a breach open.</summary>
	public void Reconfigure(Vector3 dimensions, float maxHealth, int sideIndex, float? healthFraction = null)
	{
		float fraction = healthFraction is float supplied && float.IsFinite(supplied) ? Mathf.Clamp(supplied, 0f, 1f) : HealthFraction;
		SideIndex = sideIndex;
		_maxHealth = SanitizeMaxHealth(maxHealth);
		CurrentHealth = _maxHealth * fraction;
		_healthInitialized = true;
		_destroyedNotified = !IsAlive;
		Dimensions = dimensions;
		UpdateHealthVisuals();
		HealthChanged?.Invoke(CurrentHealth, _maxHealth);
	}

	public void Retune(float maxHealth)
	{
		float fraction = HealthFraction;
		_maxHealth = SanitizeMaxHealth(maxHealth);
		if (!_healthInitialized) return;
		CurrentHealth = _maxHealth * fraction;
		UpdateHealthVisuals();
		HealthChanged?.Invoke(CurrentHealth, _maxHealth);
	}

	public void ResetHealth()
	{
		_healthInitialized = true;
		CurrentHealth = _maxHealth;
		_destroyedNotified = false;
		UpdateHealthVisuals();
		HealthChanged?.Invoke(CurrentHealth, _maxHealth);
	}

	public void ApplyDamage(float damage) => ApplyEnemyDamage(damage);
	public void ApplyEnemyDamage(float damage)
	{
		if (DebugInvulnerable || !IsAlive || !float.IsFinite(damage) || damage <= 0f) return;
		float actual = Mathf.Min(CurrentHealth, damage);
		CurrentHealth -= actual;
		bool destroyedNow = !IsAlive && !_destroyedNotified;
		if (destroyedNow) _destroyedNotified = true;
		UpdateHealthVisuals();
		Damaged?.Invoke(actual);
		HealthChanged?.Invoke(CurrentHealth, _maxHealth);
		if (destroyedNow)
		{
			Destroyed?.Invoke();
			EmitSignal(SignalName.GateDestroyed, SideIndex);
		}
	}

	public bool ApplyPlayerDamage(float damage)
	{
		if (!AllowFriendlyDamage || DebugInvulnerable || !IsAlive || !float.IsFinite(damage) || damage <= 0f) return false;
		ApplyEnemyDamage(damage);
		return true;
	}

	public void Repair(float amount)
	{
		if (!IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
		CurrentHealth = Mathf.Min(_maxHealth, CurrentHealth + amount);
		UpdateHealthVisuals();
		HealthChanged?.Invoke(CurrentHealth, _maxHealth);
	}

	private void UpdateDimensions()
	{
		Vector3 centre = Vector3.Up * (_dimensions.Y * 0.5f);
		((BoxMesh)_body.Mesh).Size = _dimensions;
		((BoxShape3D)_collider.Shape).Size = _dimensions;
		_body.Position = _collider.Position = centre;
		float barWidth = Mathf.Clamp((SideIndex % 2 == 0 ? _dimensions.X : _dimensions.Z) * 0.8f, 2f, 10f);
		((BoxMesh)_barBackground.Mesh).Size = new Vector3(barWidth + 0.10f, 0.30f, 0.04f);
		((BoxMesh)_barFill.Mesh).Size = new Vector3(barWidth, 0.20f, 0.05f);
		_barBackground.Position = new Vector3(0f, _dimensions.Y + 0.70f, 0f);
		_label.Position = new Vector3(0f, _dimensions.Y + 1.25f, 0f);
		UpdateHealthVisuals();
	}

	private void UpdateHealthVisuals()
	{
		if (!_visualsBuilt) return;
		bool alive = IsAlive;
		if (_collisionAlive != alive)
		{
			_collisionAlive = alive;
			// Layer removal immediately makes new projectile queries pass through the breach.
			CollisionLayer = alive ? 1u : 0u;
			_collider.SetDeferred(CollisionShape3D.PropertyName.Disabled, !alive);
			_body.Visible = alive;
			_barBackground.Visible = _barFill.Visible = alive;
		}
		_label.Text = IsAlive ? $"GATE {SideIndex + 1}  {Mathf.CeilToInt(CurrentHealth)}/{Mathf.CeilToInt(_maxHealth)}" : $"GATE {SideIndex + 1}  BREACHED";
		_label.Modulate = IsAlive ? Colors.White : new Color(1f, 0.46f, 0.22f);
		float fraction = HealthFraction;
		float width = Mathf.Clamp((SideIndex % 2 == 0 ? _dimensions.X : _dimensions.Z) * 0.8f, 2f, 10f);
		_barFill.Scale = new Vector3(Mathf.Max(0.001f / width, fraction), 1f, 1f);
		_barFill.Position = new Vector3(-(width - width * fraction) * 0.5f, _dimensions.Y + 0.70f, 0.03f);
		_fillMaterial.AlbedoColor = new Color(1f, 0.26f, 0.16f).Lerp(new Color(0.24f, 0.92f, 0.48f), fraction);
		_bodyMaterial.AlbedoColor = new Color(0.58f, 0.22f, 0.16f).Lerp(new Color(0.24f, 0.38f, 0.48f), fraction);
	}

	private static float SanitizeMaxHealth(float value) => float.IsFinite(value) ? Mathf.Max(1f, value) : 1000f;
	private static Vector3 SanitizeDimensions(Vector3 value) => new(
		float.IsFinite(value.X) ? Mathf.Max(0.1f, value.X) : 4.572f,
		float.IsFinite(value.Y) ? Mathf.Max(0.1f, value.Y) : 6.096f,
		float.IsFinite(value.Z) ? Mathf.Max(0.1f, value.Z) : 1f);
}
