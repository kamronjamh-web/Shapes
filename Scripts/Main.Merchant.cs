using Godot;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild;

public partial class Main
{
	private const float MerchantEncounterChance = 0.45f;
	private const float MerchantShapeOfferChance = 0.18f;
	private const int MerchantOfferCount = 3;
	private const float MerchantContinueOffset = 12.5f;
	private const float MerchantPodiumArcRadius = 15.5f;
	private const float MerchantPodiumArcSpanDegrees = 140f;

	private enum MerchantOfferKind
	{
		Modulator = 0,
		Shape = 1,
	}

	private Control _arenaMerchantOverlay = null!;
	private Label _arenaMerchantCoinsLabel = null!;
	private VBoxContainer _arenaMerchantOfferList = null!;
	private Label _arenaMerchantFeedbackLabel = null!;
	private readonly List<MerchantOffer> _merchantOffers = new();
	private readonly List<ArenaMerchantPodiumActor> _merchantPodiums = new();
	private ArenaMerchantPodiumActor? _merchantContinuePodium;
	private int _merchantDecisionWave = -1;
	private bool _shouldOfferMerchantThisWave;
	private bool _merchantEncounterActive;
	private bool _merchantInteractHeld;

	private sealed class MerchantOffer
	{
		public MerchantOfferKind Kind;
		public WeaponModulatorData? Modulator;
		public EquipmentItem? ShapeItem;
		public int Cost;
		public bool Purchased;
		public int PreviewSlotIndex = -1;
		public int PreviewReplaceIndex = -1;
		public EquipmentItem? PreviewItem;
	}

	private void BuildArenaMerchantOverlay(Control shell)
	{
		_arenaMerchantOverlay = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(1f, 0.82f, 0.42f, 0.28f),
			new Color(0.08f, 0.06f, 0.02f, 0.97f));
		_arenaMerchantOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_arenaMerchantOverlay.OffsetLeft = 36f;
		_arenaMerchantOverlay.OffsetTop = 36f;
		_arenaMerchantOverlay.OffsetRight = -36f;
		_arenaMerchantOverlay.OffsetBottom = -36f;
		_arenaMerchantOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
		_arenaMerchantOverlay.Visible = false;
		_arenaMerchantOverlay.ZIndex = 3;
		shell.AddChild(_arenaMerchantOverlay);

		Label title = new();
		title.Position = new Vector2(24f, 20f);
		title.Size = new Vector2(420f, 32f);
		title.Text = "TRAVELING MERCHANT";
		StyleHudLabel(title, 26, new Color(1f, 0.96f, 0.84f, 1f));
		_arenaMerchantOverlay.AddChild(title);

		Label subtitle = new();
		subtitle.Position = new Vector2(24f, 54f);
		subtitle.Size = new Vector2(520f, 18f);
		subtitle.Text = "The wave waits while you buy mostly modulators, with the occasional shape offer.";
		subtitle.AddThemeFontSizeOverride("font_size", 12);
		subtitle.AddThemeColorOverride("font_color", new Color(0.92f, 0.84f, 0.62f, 0.84f));
		_arenaMerchantOverlay.AddChild(subtitle);

		_arenaMerchantCoinsLabel = new Label();
		_arenaMerchantCoinsLabel.Position = new Vector2(0f, 22f);
		_arenaMerchantCoinsLabel.AnchorLeft = 1f;
		_arenaMerchantCoinsLabel.AnchorRight = 1f;
		_arenaMerchantCoinsLabel.OffsetLeft = -370f;
		_arenaMerchantCoinsLabel.OffsetRight = -168f;
		_arenaMerchantCoinsLabel.Size = new Vector2(202f, 24f);
		_arenaMerchantCoinsLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_arenaMerchantCoinsLabel.AddThemeFontSizeOverride("font_size", 14);
		_arenaMerchantCoinsLabel.AddThemeColorOverride("font_color", new Color(1f, 0.94f, 0.70f, 0.94f));
		_arenaMerchantOverlay.AddChild(_arenaMerchantCoinsLabel);

		Button closeButton = new();
		closeButton.Position = new Vector2(0f, 18f);
		closeButton.AnchorLeft = 1f;
		closeButton.AnchorRight = 1f;
		closeButton.OffsetLeft = -154f;
		closeButton.OffsetRight = -22f;
		closeButton.Size = new Vector2(132f, 40f);
		closeButton.Text = "Done";
		closeButton.Pressed += CloseArenaMerchantOverlay;
		StyleArenaPauseButton(
			closeButton,
			new Color(0.32f, 0.20f, 0.06f, 0.96f),
			new Color(1f, 0.84f, 0.42f, 0.54f),
			new Color(0.44f, 0.26f, 0.08f, 1f),
			new Color(1f, 0.92f, 0.62f, 0.88f),
			new Color(1f, 0.98f, 0.90f, 1f));
		_arenaMerchantOverlay.AddChild(closeButton);

		Button workshopButton = new();
		workshopButton.Position = new Vector2(0f, 64f);
		workshopButton.AnchorLeft = 1f;
		workshopButton.AnchorRight = 1f;
		workshopButton.OffsetLeft = -194f;
		workshopButton.OffsetRight = -22f;
		workshopButton.Size = new Vector2(172f, 40f);
		workshopButton.Text = "Open Workshop";
		workshopButton.Pressed += OpenRunModWorkshop;
		StyleArenaPauseButton(
			workshopButton,
			new Color(0.10f, 0.28f, 0.22f, 0.96f),
			new Color(0.56f, 1f, 0.82f, 0.46f),
			new Color(0.14f, 0.40f, 0.30f, 1f),
			new Color(0.74f, 1f, 0.90f, 0.86f),
			new Color(0.94f, 1f, 0.97f, 1f));
		_arenaMerchantOverlay.AddChild(workshopButton);

		VBoxContainer content = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		content.OffsetLeft = 24f;
		content.OffsetTop = 104f;
		content.OffsetRight = -24f;
		content.OffsetBottom = -24f;
		content.AddThemeConstantOverride("separation", 12);
		_arenaMerchantOverlay.AddChild(content);

		Label offersHeader = new();
		offersHeader.Text = "Offers";
		offersHeader.AddThemeFontSizeOverride("font_size", 15);
		offersHeader.AddThemeColorOverride("font_color", new Color(1f, 0.90f, 0.64f, 0.90f));
		content.AddChild(offersHeader);

		_arenaMerchantOfferList = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_arenaMerchantOfferList.AddThemeConstantOverride("separation", 10);
		content.AddChild(_arenaMerchantOfferList);

		_arenaMerchantFeedbackLabel = new Label();
		_arenaMerchantFeedbackLabel.CustomMinimumSize = new Vector2(0f, 32f);
		_arenaMerchantFeedbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_arenaMerchantFeedbackLabel.AddThemeFontSizeOverride("font_size", 12);
		_arenaMerchantFeedbackLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.84f, 0.70f, 0.82f));
		content.AddChild(_arenaMerchantFeedbackLabel);
	}

	private bool IsArenaMerchantVisible()
	{
		return IsInstanceValid(_arenaMerchantOverlay) && _arenaMerchantOverlay.Visible;
	}

	private void ResetMerchantEncounterState()
	{
		_merchantDecisionWave = -1;
		_shouldOfferMerchantThisWave = false;
		_merchantEncounterActive = false;
		_merchantInteractHeld = false;
		_merchantInspectHeld = false;
		CloseArenaMerchantInspect();
		_merchantOffers.Clear();
		ClearMerchantPodiums();
		if (IsInstanceValid(_arenaMerchantOverlay))
		{
			_arenaMerchantOverlay.Visible = false;
		}
	}

	private bool TryStartMerchantEncounterForWave(int wave)
	{
		if (!ShouldOfferMerchantEncounterForWave(wave))
		{
			return false;
		}

		StartMerchantEncounter(wave, "A roadside market unfolds between the waves.");
		return true;
	}

	public void SpawnMerchantShop()
	{
		if (_merchantEncounterActive)
		{
			ShowStatusBanner("Merchant already active.");
			return;
		}

		ClearChallengePillar();
		StartMerchantEncounter(Mathf.Max(1, _waveNum), "Debug shop spawned.");
	}

	private bool ShouldOfferMerchantEncounterForWave(int wave)
	{
		if (!ShouldSpawnChallengePillarForWave(wave))
		{
			return false;
		}

		if (_merchantDecisionWave != wave)
		{
			_merchantDecisionWave = wave;
			_shouldOfferMerchantThisWave = _rng.Randf() < MerchantEncounterChance;
		}

		return _shouldOfferMerchantThisWave;
	}

	private void StartMerchantEncounter(int wave, string bannerText)
	{
		_merchantEncounterActive = true;
		_merchantInteractHeld = false;
		_merchantInspectHeld = false;
		CloseArenaMerchantInspect();
		GenerateMerchantOffers(Mathf.Max(1, wave));
		SpawnMerchantPodiums();
		ShowStatusBanner(bannerText);
	}

	private void OpenArenaMerchantOverlay()
	{
		if (!IsInstanceValid(_arenaMerchantOverlay))
		{
			return;
		}

		_arenaMerchantOverlay.Visible = true;
		SetMerchantFeedback(string.Empty, new Color(0.90f, 0.84f, 0.70f, 0.82f));
		RefreshArenaMerchantOverlay();
	}

	private void CloseArenaMerchantOverlay()
	{
		if (!IsInstanceValid(_arenaMerchantOverlay))
		{
			return;
		}

		_arenaMerchantOverlay.Visible = false;
		SetMerchantFeedback(string.Empty, new Color(0.90f, 0.84f, 0.70f, 0.82f));
	}

	private void RefreshArenaMerchantOverlay()
	{
		if (!IsArenaMerchantVisible())
		{
			return;
		}

		_arenaMerchantCoinsLabel.Text = $"Run Coins: {_coins}";

		for (int childIndex = _arenaMerchantOfferList.GetChildCount() - 1; childIndex >= 0; childIndex--)
		{
			_arenaMerchantOfferList.GetChild(childIndex).QueueFree();
		}

		for (int offerIndex = 0; offerIndex < _merchantOffers.Count; offerIndex++)
		{
			MerchantOffer offer = _merchantOffers[offerIndex];
			Control card = CreateHudCard(
				Vector2.Zero,
				Vector2.Zero,
				new Color(1f, 0.84f, 0.50f, 0.22f),
				new Color(0.12f, 0.08f, 0.02f, 0.92f));
			card.CustomMinimumSize = new Vector2(0f, 138f);
			card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			card.MouseFilter = Control.MouseFilterEnum.Ignore;
			_arenaMerchantOfferList.AddChild(card);

			Label nameLabel = new();
			nameLabel.Position = new Vector2(18f, 16f);
			nameLabel.Size = new Vector2(520f, 24f);
			nameLabel.Text = GetMerchantOfferDisplayName(offer);
			nameLabel.AddThemeFontSizeOverride("font_size", 18);
			nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.84f, 1f));
			card.AddChild(nameLabel);

			Label metaLabel = new();
			metaLabel.Position = new Vector2(18f, 42f);
			metaLabel.Size = new Vector2(540f, 18f);
			metaLabel.Text = $"{GetMerchantOfferCategoryLabel(offer)}  |  Cost {offer.Cost} coins";
			metaLabel.AddThemeFontSizeOverride("font_size", 12);
			metaLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.86f, 0.62f, 0.86f));
			card.AddChild(metaLabel);

			Label descriptionLabel = new();
			descriptionLabel.Position = new Vector2(18f, 64f);
			descriptionLabel.Size = new Vector2(640f, 54f);
			descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			descriptionLabel.Text = BuildMerchantOfferDescriptionText(offer);
			descriptionLabel.AddThemeFontSizeOverride("font_size", 12);
			descriptionLabel.AddThemeColorOverride("font_color", new Color(0.94f, 0.92f, 0.84f, 0.86f));
			card.AddChild(descriptionLabel);

			Button buyButton = new();
			buyButton.Position = new Vector2(0f, 88f);
			buyButton.AnchorLeft = 1f;
			buyButton.AnchorRight = 1f;
			buyButton.OffsetLeft = -170f;
			buyButton.OffsetRight = -18f;
			buyButton.Size = new Vector2(152f, 36f);
			buyButton.Text = offer.Purchased ? "Purchased" : $"Buy ({offer.Cost})";
			buyButton.Disabled = offer.Purchased || _coins < offer.Cost;
			int capturedOfferIndex = offerIndex;
			buyButton.Pressed += () => PurchaseMerchantOffer(capturedOfferIndex);
			StyleArenaPauseButton(
				buyButton,
				new Color(0.26f, 0.18f, 0.04f, 0.96f),
				new Color(1f, 0.86f, 0.42f, 0.52f),
				new Color(0.38f, 0.24f, 0.06f, 1f),
				new Color(1f, 0.94f, 0.64f, 0.86f),
				new Color(1f, 0.98f, 0.90f, 1f));
			card.AddChild(buyButton);
		}
	}

	private void GenerateMerchantOffers(int wave)
	{
		_merchantOffers.Clear();
		HashSet<string> seenOfferKeys = new(System.StringComparer.Ordinal);
		bool shapeOffered = _rng.Randf() < MerchantShapeOfferChance;
		int shapeOfferIndex = shapeOffered ? _rng.RandiRange(0, MerchantOfferCount - 1) : -1;

		for (int offerIndex = 0; offerIndex < MerchantOfferCount; offerIndex++)
		{
			if (offerIndex == shapeOfferIndex)
			{
				EquipmentItem shapeItem = CreateMerchantOfferShape(wave, seenOfferKeys);
				seenOfferKeys.Add(BuildMerchantShapeSignature(shapeItem));
				_merchantOffers.Add(new MerchantOffer
				{
					Kind = MerchantOfferKind.Shape,
					ShapeItem = shapeItem,
					Cost = GetMerchantOfferCost(shapeItem, wave),
				});
				continue;
			}

			WeaponModulatorData modulator = CreateMerchantOfferModulator(wave, seenOfferKeys);
			seenOfferKeys.Add(BuildMerchantModulatorSignature(modulator));
			_merchantOffers.Add(new MerchantOffer
			{
				Kind = MerchantOfferKind.Modulator,
				Modulator = modulator,
				Cost = GetMerchantOfferCost(modulator, wave),
			});
		}
	}

	private WeaponModulatorData CreateMerchantOfferModulator(int wave, HashSet<string> seenOfferKeys)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			WeaponModulatorData offer = CreateRandomModulatorDrop();
			string signature = BuildMerchantModulatorSignature(offer);
			if (!seenOfferKeys.Contains(signature))
			{
				return offer;
			}
		}

		List<WeaponModulatorData> modulators = ModulatorCatalog.CreateWeightedRollPool();
		WeaponModulatorData fallback = PickWeighted(modulators, static modulator => modulator.Weight);
		if (!fallback.UsesTierPresentation)
		{
			return fallback;
		}

		float enhancedBias = Mathf.Clamp((wave - ChallengePillarWaveInterval) * 0.04f, 0f, 0.20f);
		float roll = _rng.Randf();
		WeaponModulatorTier tier = roll < 0.18f
			? WeaponModulatorTier.Flawed
			: roll < 0.72f - enhancedBias
				? WeaponModulatorTier.Baseline
				: roll < 0.95f
					? WeaponModulatorTier.Enhanced
					: WeaponModulatorTier.Flawless;
		return ModulatorCatalog.CreateByKey(fallback.Key, tier);
	}

	private EquipmentItem CreateMerchantOfferShape(int wave, HashSet<string> seenOfferKeys)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			EquipmentItem offer = CreateRandomShapeDropItem(PickMerchantShapeRarityKey(wave));
			if (!seenOfferKeys.Contains(BuildMerchantShapeSignature(offer)))
			{
				return offer;
			}
		}

		return CreateRandomShapeDropItem(PickMerchantShapeRarityKey(wave));
	}

	private string PickMerchantShapeRarityKey(int wave)
	{
		float roll = _rng.Randf();
		if (wave >= ChallengePillarWaveInterval * 3)
		{
			if (roll < 0.12f)
			{
				return "legendary";
			}

			if (roll < 0.52f)
			{
				return "rare";
			}

			return "uncommon";
		}

		return roll < 0.36f ? "rare" : "uncommon";
	}

	private static int GetMerchantOfferCost(WeaponModulatorData modulator, int wave)
	{
		int baseCost = modulator.Tier switch
		{
			WeaponModulatorTier.Flawed => 18,
			WeaponModulatorTier.Enhanced => 44,
			WeaponModulatorTier.Flawless => 64,
			_ => 30,
		};

		int wavePremium = Mathf.Max(0, wave - ChallengePillarWaveInterval) * 3;
		int wingPremium = modulator.IsWingModulator ? 10 : 0;
		return baseCost + wavePremium + wingPremium;
	}

	private static int GetMerchantOfferCost(EquipmentItem item, int wave)
	{
		int baseCost = EquipmentCatalogDefaults.NormalizeRarityKey(item.Rarity?.Key) switch
		{
			"rare" => 110,
			"legendary" => 165,
			"mythic" => 220,
			"divine" => 300,
			_ => 78,
		};

		int wavePremium = Mathf.Max(0, wave - ChallengePillarWaveInterval) * 5;
		return baseCost + wavePremium;
	}

	private void PurchaseMerchantOffer(int offerIndex)
	{
		if (offerIndex < 0 || offerIndex >= _merchantOffers.Count)
		{
			return;
		}

		MerchantOffer offer = _merchantOffers[offerIndex];
		if (offer.Purchased)
		{
			SetMerchantFeedback("That offer is already gone.", new Color(1f, 0.78f, 0.66f, 0.92f));
			return;
		}

		if (_coins < offer.Cost)
		{
			SetMerchantFeedback("You do not have enough run coins for that offer.", new Color(1f, 0.78f, 0.66f, 0.92f));
			RefreshArenaMerchantOverlay();
			return;
		}

		if (!TryPurchaseMerchantOffer(offer, out string failureText))
		{
			SetMerchantFeedback(failureText, new Color(1f, 0.78f, 0.66f, 0.92f));
			return;
		}

		_coins -= offer.Cost;
		offer.Purchased = true;
		if (offerIndex < _merchantPodiums.Count && IsInstanceValid(_merchantPodiums[offerIndex]))
		{
			_merchantPodiums[offerIndex].MarkPurchased();
		}

		if (_merchantInspectOfferIndex == offerIndex)
		{
			CloseArenaMerchantInspect();
		}

		RefreshMerchantPodiumStates();
		UpdateArenaPauseOverlayState();
		RefreshArenaMerchantOverlay();
		if (IsRunModWorkshopVisible())
		{
			RefreshRunModWorkshopView();
		}

		ShowStatusBanner($"Bought {GetMerchantOfferDisplayName(offer)}");
		SetMerchantFeedback(BuildMerchantPurchaseFeedback(offer), new Color(0.92f, 1f, 0.82f, 0.92f));
	}

	private bool TryPurchaseMerchantOffer(MerchantOffer offer, out string failureText)
	{
		failureText = "The merchant could not hand over that offer.";

		if (offer.Kind == MerchantOfferKind.Shape)
		{
			if (offer.ShapeItem == null)
			{
				return false;
			}

			_inventoryManager ??= GetNodeOrNull<InventoryManager>("/root/InventoryManager");
			if (_inventoryManager == null)
			{
				failureText = "Inventory service unavailable for shape purchase.";
				return false;
			}

			if (!_inventoryManager.TryAddItem(CloneRunEquipmentItem(offer.ShapeItem)))
			{
				failureText = "Inventory is full. Make room before buying a shape.";
				return false;
			}

			return true;
		}

		if (offer.Modulator == null)
		{
			return false;
		}

		if (!TryStoreRunModulator(offer.Modulator))
		{
			failureText = "The merchant could not hand over that modulator.";
			return false;
		}

		return true;
	}

	private static string GetMerchantOfferDisplayName(MerchantOffer offer)
	{
		if (offer.Kind == MerchantOfferKind.Shape)
		{
			EquipmentItem? item = offer.ShapeItem;
			string modifier = item?.Modifier != null && item.Modifier.Key != ModifierKey.Normal
				? $"{item.Modifier.DisplayName} "
				: string.Empty;
			return $"{modifier}{item?.Rarity?.DisplayName ?? "Common"} {item?.Shape?.DisplayName ?? "Shape"}";
		}

		return offer.Modulator?.DisplayName ?? "Modulator";
	}

	private static string GetMerchantOfferCategoryLabel(MerchantOffer offer)
	{
		return offer.Kind == MerchantOfferKind.Shape
			? "Shape Offer"
			: offer.Modulator == null
				? "Modulator"
				: WeaponModulatorData.GetCategoryDisplayName(offer.Modulator.Category);
	}

	private static Color GetMerchantOfferAccentColor(MerchantOffer offer)
	{
		return offer.Kind == MerchantOfferKind.Shape
			? offer.ShapeItem?.Rarity?.UiColor ?? new Color(0.92f, 0.82f, 0.44f, 1f)
			: offer.Modulator?.UiColor ?? new Color(0.42f, 0.84f, 1f, 1f);
	}

	private static string BuildMerchantOfferDescriptionText(MerchantOffer offer)
	{
		if (offer.Kind == MerchantOfferKind.Shape)
		{
			EquipmentItem? item = offer.ShapeItem;
			if (item == null)
			{
				return "A shaped weapon pulled out of the merchant's reserve.";
			}

			string? detail = item.Modifier != null && item.Modifier.Key != ModifierKey.Normal && !string.IsNullOrWhiteSpace(item.Modifier.PrimaryEffectDescription)
				? item.Modifier.PrimaryEffectDescription
				: item.Shape?.SpecialDescription;
			return string.IsNullOrWhiteSpace(detail)
				? "A shaped weapon offer that goes straight to your inventory."
				: detail;
		}

		return string.IsNullOrWhiteSpace(offer.Modulator?.Description)
			? "A clean reroute for this run's loadout."
			: offer.Modulator.Description;
	}

	private static string BuildMerchantPurchaseFeedback(MerchantOffer offer)
	{
		return offer.Kind == MerchantOfferKind.Shape
			? "Purchased. The shape went straight into your inventory."
			: "Purchased. The modulator is now waiting in your run stash.";
	}

	private static string BuildMerchantModulatorSignature(WeaponModulatorData modulator)
	{
		return $"mod:{modulator.NormalizedKey}:{modulator.Tier}";
	}

	private static string BuildMerchantShapeSignature(EquipmentItem item)
	{
		return $"shape:{item.Shape?.Key}:{item.Rarity?.Key}:{item.Modifier?.Key}";
	}

	private void SetMerchantFeedback(string text, Color color)
	{
		if (!string.IsNullOrWhiteSpace(text) && !IsArenaMerchantVisible())
		{
			ShowStatusBanner(text);
		}

		if (!IsInstanceValid(_arenaMerchantFeedbackLabel))
		{
			return;
		}

		_arenaMerchantFeedbackLabel.Text = text;
		_arenaMerchantFeedbackLabel.AddThemeColorOverride("font_color", color);
	}

	private void SpawnMerchantPodiums()
	{
		ClearMerchantPodiums();

		Vector2 center = GetChallengePillarSpawnPosition();
		Vector2 gatePosition = ClampResolve(center + Vector2.Down * MerchantContinueOffset, 3.2f);
		float startAngle = Mathf.DegToRad(270f - MerchantPodiumArcSpanDegrees * 0.5f);
		float endAngle = Mathf.DegToRad(270f + MerchantPodiumArcSpanDegrees * 0.5f);
		for (int offerIndex = 0; offerIndex < _merchantOffers.Count; offerIndex++)
		{
			ArenaMerchantPodiumActor podium = new();
			AddChild(podium);

			float t = _merchantOffers.Count <= 1 ? 0.5f : offerIndex / (float)(_merchantOffers.Count - 1);
			float angle = Mathf.Lerp(startAngle, endAngle, t);
			Vector2 arcOffset = new(Mathf.Cos(angle) * MerchantPodiumArcRadius, Mathf.Sin(angle) * MerchantPodiumArcRadius);
			Vector2 position = ClampResolve(gatePosition + arcOffset, 3.2f);
			podium.Position = position;
			_merchantPodiums.Add(podium);
		}

		RefreshMerchantOfferPreviews();

		_merchantContinuePodium = new ArenaMerchantPodiumActor();
		AddChild(_merchantContinuePodium);
		_merchantContinuePodium.ConfigureContinue(gatePosition);

		TriggerArenaInteractableSpawnCue(gatePosition);
		RefreshMerchantPodiumStates();
	}

	private void ClearMerchantPodiums()
	{
		for (int index = 0; index < _merchantPodiums.Count; index++)
		{
			ArenaMerchantPodiumActor podium = _merchantPodiums[index];
			if (IsInstanceValid(podium))
			{
				podium.QueueFree();
			}
		}

		_merchantPodiums.Clear();

		if (_merchantContinuePodium != null && IsInstanceValid(_merchantContinuePodium))
		{
			_merchantContinuePodium.QueueFree();
		}

		_merchantContinuePodium = null;
	}

	private void TickMerchantEncounter()
	{
		if (!_merchantEncounterActive)
		{
			_merchantInteractHeld = false;
			_merchantInspectHeld = false;
			return;
		}

		int focusedOfferIndex = GetFocusedMerchantOfferIndex(out bool focusedOfferCanInteract);
		bool focusContinue = false;
		bool continueCanInteract = false;
		float bestContinueDistance = float.MaxValue;

		if (_merchantContinuePodium != null && IsInstanceValid(_merchantContinuePodium))
		{
			bool previewVisible = IsMerchantPodiumInRange(_merchantContinuePodium, ArenaMerchantPodiumActor.PreviewRadius);
			continueCanInteract = IsMerchantPodiumInRange(_merchantContinuePodium, ArenaMerchantPodiumActor.InteractRadius);
			if (previewVisible)
			{
				bestContinueDistance = (_merchantContinuePodium.Position - _player.Position).LengthSquared();
			}

			if (previewVisible && (focusedOfferIndex < 0 || bestContinueDistance < GetMerchantOfferDistanceSquared(focusedOfferIndex)))
			{
				focusContinue = true;
			}
		}

		RefreshMerchantPodiumStates(focusedOfferIndex, focusContinue);
		SyncArenaMerchantInspect(focusedOfferIndex, focusContinue);

		bool interactPressed = Input.IsKeyPressed(Key.E);
		bool inspectPressed = Input.IsKeyPressed(Key.F);
		if (interactPressed && !_merchantInteractHeld)
		{
			if (focusContinue && continueCanInteract)
			{
				CompleteMerchantEncounter();
			}
			else if (focusedOfferIndex >= 0 && focusedOfferCanInteract)
			{
				PurchaseMerchantOffer(focusedOfferIndex);
			}
		}

		if (inspectPressed && !_merchantInspectHeld)
		{
			ToggleArenaMerchantInspect(focusedOfferIndex, focusContinue);
		}

		_merchantInteractHeld = interactPressed;
		_merchantInspectHeld = inspectPressed;
	}

	private int GetFocusedMerchantOfferIndex(out bool canInteract)
	{
		canInteract = false;
		int bestIndex = -1;
		float bestDistance = float.MaxValue;

		for (int offerIndex = 0; offerIndex < _merchantPodiums.Count; offerIndex++)
		{
			if (offerIndex < _merchantOffers.Count && _merchantOffers[offerIndex].Purchased)
			{
				continue;
			}

			ArenaMerchantPodiumActor podium = _merchantPodiums[offerIndex];
			if (!IsInstanceValid(podium))
			{
				continue;
			}

			if (!IsMerchantPodiumInRange(podium, ArenaMerchantPodiumActor.PreviewRadius))
			{
				continue;
			}

			float distanceSquared = (podium.Position - _player.Position).LengthSquared();
			if (distanceSquared >= bestDistance)
			{
				continue;
			}

			bestDistance = distanceSquared;
			bestIndex = offerIndex;
			canInteract = IsMerchantPodiumInRange(podium, ArenaMerchantPodiumActor.InteractRadius);
		}

		return bestIndex;
	}

	private float GetMerchantOfferDistanceSquared(int offerIndex)
	{
		if (offerIndex < 0 || offerIndex >= _merchantPodiums.Count || !IsInstanceValid(_merchantPodiums[offerIndex]))
		{
			return float.MaxValue;
		}

		return (_merchantPodiums[offerIndex].Position - _player.Position).LengthSquared();
	}

	private void RefreshMerchantPodiumStates(int focusedOfferIndex = -1, bool focusContinue = false)
	{
		for (int offerIndex = 0; offerIndex < _merchantPodiums.Count; offerIndex++)
		{
			ArenaMerchantPodiumActor podium = _merchantPodiums[offerIndex];
			if (!IsInstanceValid(podium))
			{
				continue;
			}

			bool previewVisible = IsMerchantPodiumInRange(podium, ArenaMerchantPodiumActor.PreviewRadius);
			bool canInteract = offerIndex == focusedOfferIndex && IsMerchantPodiumInRange(podium, ArenaMerchantPodiumActor.InteractRadius);
			podium.SetInteractionState(previewVisible, offerIndex == focusedOfferIndex && !focusContinue, canInteract);
		}

		if (_merchantContinuePodium != null && IsInstanceValid(_merchantContinuePodium))
		{
			bool previewVisible = IsMerchantPodiumInRange(_merchantContinuePodium, ArenaMerchantPodiumActor.PreviewRadius);
			bool canInteract = focusContinue && IsMerchantPodiumInRange(_merchantContinuePodium, ArenaMerchantPodiumActor.InteractRadius);
			_merchantContinuePodium.SetInteractionState(previewVisible, focusContinue, canInteract);
		}
	}

	private bool IsMerchantPodiumInRange(ArenaMerchantPodiumActor podium, float radius)
	{
		float reach = radius + _player.Radius;
		return (podium.Position - _player.Position).LengthSquared() <= reach * reach;
	}

	private void CompleteMerchantEncounter()
	{
		_merchantEncounterActive = false;
		_merchantInteractHeld = false;
		_merchantInspectHeld = false;
		CloseArenaMerchantInspect();
		ClearMerchantPodiums();
		CloseArenaMerchantOverlay();
		ShowStatusBanner("The market folds back into the dark.");
	}

	private static string BuildMerchantOfferBillboardSummary(MerchantOffer offer)
	{
		string category = GetMerchantOfferCategoryLabel(offer);
		string detail = offer.Kind == MerchantOfferKind.Shape
			? BuildMerchantShapeFallbackSummary(offer.ShapeItem)
			: string.IsNullOrWhiteSpace(offer.Modulator?.Description)
				? BuildMerchantFallbackSummary(offer.Modulator)
				: offer.Modulator.Description.Replace('\n', ' ').Trim();

		if (detail.Length > 78)
		{
			detail = detail[..75].TrimEnd() + "...";
		}

		return $"{category} | {detail}";
	}

	private static string BuildMerchantFallbackSummary(WeaponModulatorData? modulator)
	{
		if (modulator == null)
		{
			return "Run-only tuning for this loadout.";
		}

		if (modulator.Substats.Count == 0)
		{
			return string.IsNullOrWhiteSpace(modulator.Description)
				? $"{modulator.PowerTier} {WeaponModulatorData.GetCategoryDisplayName(modulator.Category)} modulator."
				: modulator.Description;
		}

		SubstatRoll roll = modulator.Substats[0];
		return $"{roll.Type} +{roll.Value:0.##}";
	}

	private static string BuildMerchantShapeFallbackSummary(EquipmentItem? item)
	{
		if (item == null)
		{
			return "A weapon shape pulled out of reserve.";
		}

		if (item.Modifier != null && item.Modifier.Key != ModifierKey.Normal && !string.IsNullOrWhiteSpace(item.Modifier.PrimaryEffectDescription))
		{
			return item.Modifier.PrimaryEffectDescription.Replace('\n', ' ').Trim();
		}

		if (item.Substats.Count > 0)
		{
			return item.Substats[0].ToDisplayString();
		}

		return item.Shape?.SpecialDescription ?? "A weapon shape pulled out of reserve.";
	}

	private bool TryGetMerchantWaypointTarget(out Vector2 targetPosition, out Color color, out string text)
	{
		targetPosition = Vector2.Zero;
		color = Colors.White;
		text = string.Empty;

		if (!_merchantEncounterActive)
		{
			return false;
		}

		int nextOfferIndex = -1;
		float bestOfferDistance = float.MaxValue;
		for (int offerIndex = 0; offerIndex < _merchantOffers.Count && offerIndex < _merchantPodiums.Count; offerIndex++)
		{
			if (_merchantOffers[offerIndex].Purchased || !IsInstanceValid(_merchantPodiums[offerIndex]))
			{
				continue;
			}

			float distanceSquared = (_merchantPodiums[offerIndex].Position - _player.Position).LengthSquared();
			if (distanceSquared >= bestOfferDistance)
			{
				continue;
			}

			bestOfferDistance = distanceSquared;
			nextOfferIndex = offerIndex;
		}

		if (nextOfferIndex >= 0)
		{
			targetPosition = _merchantPodiums[nextOfferIndex].Position;
			color = GetMerchantOfferAccentColor(_merchantOffers[nextOfferIndex]);
			text = "SHOP";
			return true;
		}

		if (_merchantContinuePodium != null && IsInstanceValid(_merchantContinuePodium))
		{
			targetPosition = _merchantContinuePodium.Position;
			color = new Color(0.42f, 0.92f, 1f, 1f);
			text = "GO";
			return true;
		}

		return false;
	}
}