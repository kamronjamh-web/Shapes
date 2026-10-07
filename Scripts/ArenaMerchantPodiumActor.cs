using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.Visuals;

namespace SuperGatchaRebuild;

public partial class ArenaMerchantPodiumActor : Node3D
{
	public const float PreviewRadius = 15f;
	public const float InteractRadius = 8.25f;

	private Vector2 _position2D = Vector2.Zero;
	private float _time;
	private string _title = "Offer";
	private string _summary = string.Empty;
	private int _cost;
	private Color _accentColor = new(1f, 0.84f, 0.48f, 1f);
	private bool _isContinuePedestal;
	private bool _isPurchased;
	private bool _previewVisible;
	private bool _isFocused;
	private bool _canInteract;

	private MeshInstance3D _pedestal = null!;
	private MeshInstance3D _pedestalTrim = null!;
	private MeshInstance3D _rarityPillar = null!;
	private MeshInstance3D _beaconCore = null!;
	private MeshInstance3D _beaconBaseRing = null!;
	private MeshInstance3D _offerCore = null!;
	private Node3D _offerPreviewRoot = null!;
	private Node3D? _offerPreviewVisual;
	private ModifierVisualController? _offerPreviewEffects;
	private bool _offerPreviewUsesNativeAnimation;
	private MeshInstance3D _offerHalo = null!;
	private Label3D _billboard = null!;
	private OmniLight3D _beaconLight = null!;
	private OmniLight3D _light = null!;
	private StandardMaterial3D _pedestalMaterial = null!;
	private StandardMaterial3D _trimMaterial = null!;
	private StandardMaterial3D _pillarMaterial = null!;
	private StandardMaterial3D _beaconCoreMaterial = null!;
	private StandardMaterial3D _beaconRingMaterial = null!;
	private StandardMaterial3D _coreMaterial = null!;
	private StandardMaterial3D _haloMaterial = null!;
	private Color _previewRarityColor = Colors.White;
	private int _previewRarityRank;

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

	public bool IsContinuePedestal => _isContinuePedestal;
	public bool IsPurchased => _isPurchased;

	public override void _Ready()
	{
		BuildVisuals();
		RefreshOfferCoreMesh();
		Position = _position2D;
		RefreshBillboard();
		UpdateVisuals();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		UpdateVisuals();
	}

	public void ConfigureOffer(Vector2 position, string title, string summary, int cost, Color accentColor, EquipmentItem? previewItem, bool useShapePreview = false)
	{
		_isContinuePedestal = false;
		_title = string.IsNullOrWhiteSpace(title) ? "Offer" : title.Trim();
		_summary = string.IsNullOrWhiteSpace(summary) ? "A clean reroute for this run's loadout." : summary.Trim();
		_cost = Mathf.Max(0, cost);
		_accentColor = accentColor;
		_isPurchased = false;
		Position = position;
		RefreshOfferPreview(previewItem, useShapePreview);
		RefreshOfferCoreMesh();
		RefreshBillboard();
		UpdateVisuals();
	}

	public void ConfigureContinue(Vector2 position)
	{
		_isContinuePedestal = true;
		_title = "Wave Gate";
		_summary = "Step away from the merchant and start the next wave when you're ready.";
		_cost = 0;
		_accentColor = new Color(0.42f, 0.92f, 1f, 1f);
		_isPurchased = false;
		Position = position;
		RefreshOfferPreview(null, useShapePreview: false);
		RefreshOfferCoreMesh();
		RefreshBillboard();
		UpdateVisuals();
	}

	public void SetInteractionState(bool previewVisible, bool focused, bool canInteract)
	{
		_previewVisible = previewVisible;
		_isFocused = focused;
		_canInteract = canInteract;
		RefreshBillboard();
	}

	public void MarkPurchased()
	{
		_isPurchased = true;
		RefreshBillboard();
		UpdateVisuals();
	}

	private void BuildVisuals()
	{
		_pedestalMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.14f, 0.11f, 0.08f, 1f),
			Metallic = 0.22f,
			Roughness = 0.28f,
			EmissionEnabled = true,
			Emission = _accentColor,
			EmissionEnergyMultiplier = 0.32f,
		};

		_trimMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.30f, 0.22f, 0.12f, 1f),
			Metallic = 0.12f,
			Roughness = 0.18f,
			EmissionEnabled = true,
			Emission = _accentColor,
			EmissionEnergyMultiplier = 0.84f,
		};

		_pillarMaterial = new StandardMaterial3D
		{
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 1.3f,
			Metallic = 0.05f,
			Roughness = 0.42f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_beaconCoreMaterial = new StandardMaterial3D
		{
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 2.6f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_beaconRingMaterial = new StandardMaterial3D
		{
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 2.0f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_coreMaterial = new StandardMaterial3D
		{
			AlbedoColor = Colors.White,
			Metallic = 0.10f,
			Roughness = 0.08f,
			EmissionEnabled = true,
			Emission = _accentColor,
			EmissionEnergyMultiplier = 1.6f,
		};

		_haloMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(_accentColor.R, _accentColor.G, _accentColor.B, 0.24f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = _accentColor,
			EmissionEnergyMultiplier = 1.1f,
			Roughness = 0.10f,
		};

		_pedestal = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 2.6f,
				BottomRadius = 3.2f,
				Height = 1.7f,
			},
			MaterialOverride = _pedestalMaterial,
			Position = new Vector3(0f, 0.85f, 0f),
		};
		AddChild(_pedestal);

		_pedestalTrim = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 2.9f,
				BottomRadius = 2.9f,
				Height = 0.22f,
			},
			MaterialOverride = _trimMaterial,
			Position = new Vector3(0f, 1.72f, 0f),
		};
		AddChild(_pedestalTrim);

		_rarityPillar = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 32 },
			MaterialOverride = _pillarMaterial,
			Visible = false,
		};
		AddChild(_rarityPillar);

		_beaconCore = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 24 },
			MaterialOverride = _beaconCoreMaterial,
			Visible = false,
		};
		AddChild(_beaconCore);

		_beaconBaseRing = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 48 },
			MaterialOverride = _beaconRingMaterial,
			Visible = false,
		};
		AddChild(_beaconBaseRing);

		_offerCore = new MeshInstance3D
		{
			MaterialOverride = _coreMaterial,
		};
		AddChild(_offerCore);

		_offerPreviewRoot = new Node3D();
		AddChild(_offerPreviewRoot);

		_offerHalo = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 3.5f,
				BottomRadius = 3.5f,
				Height = 0.08f,
			},
			MaterialOverride = _haloMaterial,
		};
		AddChild(_offerHalo);

		_billboard = new Label3D();
		_billboard.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		_billboard.NoDepthTest = true;
		_billboard.Shaded = false;
		_billboard.DoubleSided = true;
		_billboard.FontSize = 34;
		_billboard.PixelSize = 0.030f;
		_billboard.HorizontalAlignment = HorizontalAlignment.Center;
		_billboard.VerticalAlignment = VerticalAlignment.Center;
		_billboard.OutlineSize = 10;
		_billboard.OutlineModulate = new Color(0f, 0f, 0f, 0.88f);
		_billboard.RenderPriority = 80;
		_billboard.OutlineRenderPriority = 79;
		_billboard.Position = new Vector3(0f, 7.2f, 0f);
		_billboard.Visible = false;
		AddChild(_billboard);

		_beaconLight = new OmniLight3D
		{
			Visible = false,
			LightEnergy = 0f,
			OmniRange = 10f,
			ShadowEnabled = false,
		};
		AddChild(_beaconLight);

		_light = new OmniLight3D
		{
			Position = new Vector3(0f, 5f, 0f),
			LightColor = _accentColor,
			LightEnergy = 1.7f,
			OmniRange = 20f,
			ShadowEnabled = false,
		};
		AddChild(_light);
	}

	private void RefreshOfferCoreMesh()
	{
		if (!IsInstanceValid(_offerCore))
		{
			return;
		}

		_offerCore.Visible = _offerPreviewVisual == null;
		if (_offerPreviewVisual != null)
		{
			_offerCore.Mesh = null;
			return;
		}

		_offerCore.Mesh = _isContinuePedestal
			? new CylinderMesh
			{
				TopRadius = 1.0f,
				BottomRadius = 1.0f,
				Height = 1.8f,
			}
			: new SphereMesh
			{
				Radius = 0.75f,
				Height = 1.5f,
			};
	}

	private void RefreshOfferPreview(EquipmentItem? previewItem, bool useShapePreview)
	{
		if (!IsInstanceValid(_offerPreviewRoot))
		{
			return;
		}

		if (_offerPreviewEffects != null && IsInstanceValid(_offerPreviewEffects))
		{
			_offerPreviewEffects.QueueFree();
		}

		if (_offerPreviewVisual != null && IsInstanceValid(_offerPreviewVisual))
		{
			_offerPreviewVisual.QueueFree();
		}

		_offerPreviewEffects = null;
		_offerPreviewVisual = null;
		_offerPreviewUsesNativeAnimation = false;
		_previewRarityColor = _accentColor;
		_previewRarityRank = 2;

		if (_isContinuePedestal || !useShapePreview || previewItem?.Shape == null)
		{
			return;
		}

		_previewRarityColor = ResolveEquipmentRarityColor(previewItem.Rarity);
		_previewRarityRank = GetEquipmentRarityRank(previewItem.Rarity);

		Color previewTint = ResolvePreviewTint(previewItem);
		Node3D previewVisual = ShapeVisualFactory.CreateVisual(previewItem.Shape, previewTint);
		previewVisual.Position = Vector3.Zero;
		_offerPreviewRoot.AddChild(previewVisual);
		_offerPreviewVisual = previewVisual;
		_offerPreviewUsesNativeAnimation = ShapeVisualFactory.UsesNativeAnimation(previewVisual);

		if (ModifierVisualTheme.HasSpecialVisuals(previewItem))
		{
			_offerPreviewEffects = new ModifierVisualController();
			_offerPreviewRoot.AddChild(_offerPreviewEffects);
			_offerPreviewEffects.Configure(previewItem, previewVisual, previewItem.Shape, 1.0f, _light, 0.85f);
		}
	}

	private Color ResolvePreviewTint(EquipmentItem previewItem)
	{
		Color baseColor = previewItem.Rarity?.UiColor ?? _accentColor;
		return new Color(
			Mathf.Clamp(baseColor.R * 0.55f + _accentColor.R * 0.45f, 0f, 1f),
			Mathf.Clamp(baseColor.G * 0.55f + _accentColor.G * 0.45f, 0f, 1f),
			Mathf.Clamp(baseColor.B * 0.55f + _accentColor.B * 0.45f, 0f, 1f),
			1f);
	}

	private void RefreshBillboard()
	{
		if (!IsInstanceValid(_billboard))
		{
			return;
		}

		_billboard.Visible = _previewVisible;
		if (!_previewVisible)
		{
			return;
		}

		string promptText;
		if (_isContinuePedestal)
		{
			promptText = _canInteract ? "Press E to continue" : "Move closer";
		}
		else if (_isPurchased)
		{
			promptText = "Purchased";
		}
		else if (_canInteract)
		{
			promptText = $"Press E to buy | {_cost} coins";
		}
		else
		{
			promptText = $"Cost: {_cost} coins";
		}

		_billboard.Text = $"{_title}\n{_summary}\n{promptText}";
		Color textColor = _isContinuePedestal
			? new Color(0.84f, 0.96f, 1f, 1f)
			: _isPurchased
				? new Color(0.76f, 0.84f, 0.88f, 0.96f)
				: new Color(1f, 0.96f, 0.86f, 1f);
		_billboard.Modulate = textColor;
	}

	private void UpdateVisuals()
	{
		if (_pedestalMaterial == null || _trimMaterial == null || _coreMaterial == null || _haloMaterial == null || _pillarMaterial == null || _beaconCoreMaterial == null || _beaconRingMaterial == null)
		{
			return;
		}

		Color accent = _accentColor;
		if (_isPurchased)
		{
			accent = new Color(0.48f, 0.58f, 0.62f, 1f);
		}

		float pulseSpeed = _isFocused ? 4.6f : 2.2f;
		float pulse = 0.5f + 0.5f * Mathf.Sin(_time * pulseSpeed);
		float focusLift = _isFocused ? 0.34f : 0f;

		_pedestalMaterial.Emission = accent;
		_pedestalMaterial.EmissionEnergyMultiplier = 0.28f + pulse * (_isFocused ? 0.42f : 0.16f);

		_trimMaterial.Emission = accent;
		_trimMaterial.EmissionEnergyMultiplier = 0.72f + pulse * (_isFocused ? 0.66f : 0.26f);

		_coreMaterial.Emission = accent;
		_coreMaterial.EmissionEnergyMultiplier = _isPurchased ? 0.45f : 1.35f + pulse * (_isFocused ? 1.05f : 0.42f);
		_offerCore.Position = new Vector3(0f, 3.4f + pulse * 0.32f + focusLift, 0f);
		_offerCore.Rotation = _isContinuePedestal
			? Vector3.Zero
			: _offerPreviewVisual == null
				? new Vector3(0f, _time * 1.8f, 0f)
				: new Vector3(_time * 0.55f, 0f, 0f);
		_offerCore.Scale = Vector3.One * (_isFocused ? 1.12f : 1f);

		_offerPreviewRoot.Position = new Vector3(0f, 3.28f + pulse * 0.24f + focusLift, 0f);
		_offerPreviewRoot.Scale = Vector3.One * (_isFocused ? 1.18f : 1.04f);
		_offerPreviewRoot.Visible = _offerPreviewVisual != null;
		if (_offerPreviewVisual != null)
		{
			_offerPreviewRoot.Rotation = _offerPreviewUsesNativeAnimation
				? Vector3.Zero
				: new Vector3(0.22f, 0f, 0f);
		}

		bool useItemBeacon = !_isContinuePedestal;
		Color beaconColor = _isPurchased
			? new Color(0.56f, 0.62f, 0.68f, 1f)
			: _previewRarityColor;
		float slowPulse = 0.5f + 0.5f * Mathf.Sin(_time * 1.7f);
		float beamHeight = Mathf.Min(28f, GetLootBeamHeight(_previewRarityRank));
		float beamRadius = Mathf.Min(1.15f, GetLootBeamRadius(_previewRarityRank, pulse));
		float coreRadius = beamRadius * 0.30f;
		float ringRadius = Mathf.Min(2.5f, GetLootRingRadius(_previewRarityRank, slowPulse));
		float beamCenterY = beamHeight * 0.5f + 0.18f;
		float beaconVisibility = _previewVisible ? 1f : 0.58f;

		_rarityPillar.Visible = useItemBeacon;
		_rarityPillar.Position = new Vector3(0f, beamCenterY, 0f);
		_rarityPillar.Scale = new Vector3(beamRadius, beamHeight, beamRadius);
		_pillarMaterial.AlbedoColor = new Color(beaconColor.R, beaconColor.G, beaconColor.B, (0.08f + pulse * 0.05f) * beaconVisibility);
		_pillarMaterial.Emission = beaconColor;
		_pillarMaterial.EmissionEnergyMultiplier = (1.6f + _previewRarityRank * 0.16f + pulse * 0.42f) * beaconVisibility;

		_beaconCore.Visible = useItemBeacon;
		_beaconCore.Position = new Vector3(0f, beamCenterY, 0f);
		_beaconCore.Scale = new Vector3(coreRadius, beamHeight * 1.04f, coreRadius);
		_beaconCoreMaterial.AlbedoColor = new Color(beaconColor.R, beaconColor.G, beaconColor.B, (0.30f + pulse * 0.12f) * beaconVisibility);
		_beaconCoreMaterial.Emission = beaconColor.Lightened(0.28f);
		_beaconCoreMaterial.EmissionEnergyMultiplier = (2.2f + _previewRarityRank * 0.24f + pulse * 0.68f) * beaconVisibility;

		_beaconBaseRing.Visible = useItemBeacon;
		_beaconBaseRing.Position = new Vector3(0f, -0.37f, 0f);
		_beaconBaseRing.Scale = new Vector3(ringRadius, 0.03f, ringRadius);
		_beaconRingMaterial.AlbedoColor = new Color(beaconColor.R, beaconColor.G, beaconColor.B, (0.18f + slowPulse * 0.10f) * beaconVisibility);
		_beaconRingMaterial.Emission = beaconColor;
		_beaconRingMaterial.EmissionEnergyMultiplier = (1.8f + _previewRarityRank * 0.14f + slowPulse * 0.40f) * beaconVisibility;

		_offerHalo.Visible = _isContinuePedestal;
		if (_isContinuePedestal)
		{
			_haloMaterial.AlbedoColor = new Color(accent.R, accent.G, accent.B, _previewVisible ? 0.26f + pulse * 0.10f : 0.14f);
			_haloMaterial.Emission = accent;
			_haloMaterial.EmissionEnergyMultiplier = _previewVisible ? 1.3f + pulse * 0.42f : 0.68f;
			_offerHalo.Position = new Vector3(0f, 0.28f + pulse * 0.08f, 0f);
			_offerHalo.Scale = Vector3.One * (_isFocused ? 1.12f + pulse * 0.12f : 1f + pulse * 0.08f);
			_offerHalo.Rotation = new Vector3(0f, _time * (_isFocused ? 2.0f : 1.1f), 0f);
		}

		_beaconLight.Visible = useItemBeacon;
		_beaconLight.LightColor = beaconColor.Lightened(0.24f);
		_beaconLight.LightEnergy = (1.5f + _previewRarityRank * 0.36f + pulse * 0.65f) * beaconVisibility;
		_beaconLight.OmniRange = 12f + _previewRarityRank * 1.6f;
		_beaconLight.Position = new Vector3(0f, 4.5f + focusLift, 0f);

		_light.LightColor = useItemBeacon ? beaconColor.Lightened(0.35f) : accent;
		_light.LightEnergy = useItemBeacon
			? (2.0f + _previewRarityRank * 0.42f + pulse * 0.90f) * beaconVisibility
			: (_previewVisible ? 2.0f + pulse * 0.42f : 1.2f + pulse * 0.18f);
		_light.OmniRange = useItemBeacon ? 14f + _previewRarityRank * 1.6f : (_previewVisible ? 24f : 18f);
		_light.Position = useItemBeacon ? new Vector3(0f, 3.9f + focusLift, 0f) : new Vector3(0f, 5f, 0f);
	}

	private static Color ResolveEquipmentRarityColor(RarityData? rarity)
	{
		if (rarity != null)
		{
			Color color = rarity.UiColor;
			color.A = 1f;
			return color;
		}

		return Colors.White;
	}

	private static float GetLootBeamHeight(int rarityRank)
	{
		float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
		return Mathf.Lerp(10f, 104f, Mathf.Pow(t, 1.18f));
	}

	private static float GetLootBeamRadius(int rarityRank, float pulse)
	{
		float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
		return Mathf.Lerp(0.28f, 1.65f, Mathf.Pow(t, 1.08f)) + pulse * 0.05f;
	}

	private static float GetLootRingRadius(int rarityRank, float slowPulse)
	{
		float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
		return Mathf.Lerp(0.95f, 3.2f, Mathf.Pow(t, 1.04f)) + slowPulse * 0.16f;
	}

	private static int GetEquipmentRarityRank(RarityData? rarity)
	{
		return EquipmentCatalogDefaults.NormalizeRarityKey(rarity?.Key) switch
		{
			"uncommon" => 1,
			"rare" => 2,
			"legendary" => 3,
			"mythic" => 4,
			"divine" => 5,
			_ => 0,
		};
	}
}