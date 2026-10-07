using Godot;
using System.Text;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
	private const float MerchantPreviewCriticalDamagePercent = 50f;

	private Control _arenaMerchantInspectCard = null!;
	private Label _arenaMerchantInspectTitle = null!;
	private RichTextLabel _arenaMerchantInspectBody = null!;
	private Label _arenaMerchantInspectHint = null!;
	private int _merchantInspectOfferIndex = -1;
	private bool _merchantInspectHeld;

	private readonly struct MerchantWeaponStatPreview
	{
		public bool HasShape { get; init; }
		public int HitCount { get; init; }
		public int ProjectileCount { get; init; }
		public float DamagePerProjectile { get; init; }
		public float AttacksPerSecond { get; init; }
		public float EffectiveCooldown { get; init; }
		public float Dps { get; init; }
	}

	private void BuildArenaMerchantInspectOverlay(Control root)
	{
		_arenaMerchantInspectCard = CreateHudCard(
			Vector2.Zero,
			Vector2.Zero,
			new Color(1f, 0.82f, 0.46f, 0.26f),
			new Color(0.06f, 0.05f, 0.02f, 0.92f));
		_arenaMerchantInspectCard.AnchorLeft = 1f;
		_arenaMerchantInspectCard.AnchorRight = 1f;
		_arenaMerchantInspectCard.AnchorTop = 1f;
		_arenaMerchantInspectCard.AnchorBottom = 1f;
		_arenaMerchantInspectCard.OffsetLeft = -442f;
		_arenaMerchantInspectCard.OffsetTop = -356f;
		_arenaMerchantInspectCard.OffsetRight = -20f;
		_arenaMerchantInspectCard.OffsetBottom = -24f;
		_arenaMerchantInspectCard.MouseFilter = Control.MouseFilterEnum.Ignore;
		_arenaMerchantInspectCard.Visible = false;
		_arenaMerchantInspectCard.ZIndex = 42;
		root.AddChild(_arenaMerchantInspectCard);

		_arenaMerchantInspectTitle = new Label();
		_arenaMerchantInspectTitle.Position = new Vector2(18f, 14f);
		_arenaMerchantInspectTitle.Size = new Vector2(388f, 28f);
		StyleHudLabel(_arenaMerchantInspectTitle, 21, new Color(1f, 0.96f, 0.84f, 1f));
		_arenaMerchantInspectCard.AddChild(_arenaMerchantInspectTitle);

		_arenaMerchantInspectHint = new Label();
		_arenaMerchantInspectHint.Position = new Vector2(18f, 42f);
		_arenaMerchantInspectHint.Size = new Vector2(388f, 18f);
		_arenaMerchantInspectHint.AddThemeFontSizeOverride("font_size", 11);
		_arenaMerchantInspectHint.AddThemeColorOverride("font_color", new Color(0.94f, 0.88f, 0.70f, 0.84f));
		_arenaMerchantInspectCard.AddChild(_arenaMerchantInspectHint);

		_arenaMerchantInspectBody = new RichTextLabel();
		_arenaMerchantInspectBody.Position = new Vector2(18f, 66f);
		_arenaMerchantInspectBody.Size = new Vector2(388f, 250f);
		_arenaMerchantInspectBody.BbcodeEnabled = true;
		_arenaMerchantInspectBody.ScrollActive = false;
		_arenaMerchantInspectBody.FitContent = false;
		_arenaMerchantInspectBody.MouseFilter = Control.MouseFilterEnum.Ignore;
		_arenaMerchantInspectBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_arenaMerchantInspectBody.AddThemeFontSizeOverride("normal_font_size", 13);
		_arenaMerchantInspectBody.AddThemeColorOverride("default_color", new Color(0.95f, 0.93f, 0.86f, 0.95f));
		_arenaMerchantInspectCard.AddChild(_arenaMerchantInspectBody);
	}

	private bool IsArenaMerchantInspectVisible()
	{
		return IsInstanceValid(_arenaMerchantInspectCard) && _arenaMerchantInspectCard.Visible;
	}

	private void ToggleArenaMerchantInspect(int focusedOfferIndex, bool focusContinue)
	{
		if (focusContinue || focusedOfferIndex < 0 || focusedOfferIndex >= _merchantOffers.Count || _merchantOffers[focusedOfferIndex].Purchased)
		{
			CloseArenaMerchantInspect();
			return;
		}

		if (IsArenaMerchantInspectVisible() && _merchantInspectOfferIndex == focusedOfferIndex)
		{
			CloseArenaMerchantInspect();
			return;
		}

		_merchantInspectOfferIndex = focusedOfferIndex;
		_arenaMerchantInspectCard.Visible = true;
		RefreshArenaMerchantInspect();
	}

	private void SyncArenaMerchantInspect(int focusedOfferIndex, bool focusContinue)
	{
		if (!IsArenaMerchantInspectVisible())
		{
			return;
		}

		if (focusContinue || focusedOfferIndex < 0 || focusedOfferIndex >= _merchantOffers.Count || _merchantOffers[focusedOfferIndex].Purchased)
		{
			CloseArenaMerchantInspect();
			return;
		}

		_merchantInspectOfferIndex = focusedOfferIndex;
		RefreshArenaMerchantInspect();
	}

	private void CloseArenaMerchantInspect()
	{
		_merchantInspectOfferIndex = -1;
		if (IsInstanceValid(_arenaMerchantInspectCard))
		{
			_arenaMerchantInspectCard.Visible = false;
		}
	}

	private void RefreshArenaMerchantInspect()
	{
		if (!IsArenaMerchantInspectVisible())
		{
			return;
		}

		if (_merchantInspectOfferIndex < 0 || _merchantInspectOfferIndex >= _merchantOffers.Count)
		{
			CloseArenaMerchantInspect();
			return;
		}

		MerchantOffer offer = _merchantOffers[_merchantInspectOfferIndex];
		if (offer.Purchased)
		{
			CloseArenaMerchantInspect();
			return;
		}

		_arenaMerchantInspectTitle.Text = GetMerchantOfferDisplayName(offer);
		_arenaMerchantInspectTitle.AddThemeColorOverride("font_color", GetMerchantOfferAccentColor(offer).Lightened(0.20f));
		_arenaMerchantInspectHint.Text = offer.Kind == MerchantOfferKind.Shape
			? $"F close  |  E buy ({offer.Cost})  |  Purchase goes to inventory"
			: $"F close  |  E buy ({offer.Cost})  |  Preview tracks workshop changes";
		_arenaMerchantInspectBody.Text = BuildArenaMerchantInspectText(offer);
	}

	private void RefreshMerchantOfferPreviews()
	{
		for (int offerIndex = 0; offerIndex < _merchantOffers.Count; offerIndex++)
		{
			MerchantOffer offer = _merchantOffers[offerIndex];
			PopulateMerchantOfferPreview(offer);

			if (offerIndex >= _merchantPodiums.Count || !IsInstanceValid(_merchantPodiums[offerIndex]))
			{
				continue;
			}

			ArenaMerchantPodiumActor podium = _merchantPodiums[offerIndex];
			podium.ConfigureOffer(
				podium.Position,
				GetMerchantOfferDisplayName(offer),
				BuildMerchantOfferBillboardSummary(offer),
				offer.Cost,
				GetMerchantOfferAccentColor(offer),
				offer.Kind == MerchantOfferKind.Shape ? offer.ShapeItem : offer.PreviewItem,
				useShapePreview: offer.Kind == MerchantOfferKind.Shape);

			if (offer.Purchased)
			{
				podium.MarkPurchased();
			}
		}

		RefreshMerchantPodiumStates();
		RefreshArenaMerchantInspect();
	}

	private void PopulateMerchantOfferPreview(MerchantOffer offer)
	{
		offer.PreviewSlotIndex = -1;
		offer.PreviewReplaceIndex = -1;
		offer.PreviewItem = null;

		if (offer.Kind == MerchantOfferKind.Shape)
		{
			PopulateMerchantShapeOfferPreview(offer);
			return;
		}

		if (offer.Modulator == null)
		{
			return;
		}

		float bestScore = float.NegativeInfinity;
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? currentItem = GetRunLoadoutItem(slotIndex);
			if (currentItem?.Shape == null)
			{
				continue;
			}

			if (currentItem.AvailableModulatorSlots > 0 && CanInstallRunModulator(currentItem, offer.Modulator))
			{
				EquipmentItem previewItem = CreateMerchantPreviewItem(currentItem, offer.Modulator);
				float score = ScoreMerchantPreviewCandidate(currentItem, previewItem, slotIndex, -1);
				if (score > bestScore)
				{
					bestScore = score;
					offer.PreviewSlotIndex = slotIndex;
					offer.PreviewReplaceIndex = -1;
					offer.PreviewItem = previewItem;
				}
			}

			for (int replaceIndex = 0; replaceIndex < currentItem.Modulators.Count; replaceIndex++)
			{
				if (!CanInstallRunModulator(currentItem, offer.Modulator, replaceIndex))
				{
					continue;
				}

				EquipmentItem previewItem = CreateMerchantPreviewItem(currentItem, offer.Modulator, replaceIndex);
				float score = ScoreMerchantPreviewCandidate(currentItem, previewItem, slotIndex, replaceIndex);
				if (score > bestScore)
				{
					bestScore = score;
					offer.PreviewSlotIndex = slotIndex;
					offer.PreviewReplaceIndex = replaceIndex;
					offer.PreviewItem = previewItem;
				}
			}
		}
	}

	private void PopulateMerchantShapeOfferPreview(MerchantOffer offer)
	{
		if (offer.ShapeItem?.Shape == null)
		{
			return;
		}

		offer.PreviewItem = offer.ShapeItem;
		float bestScore = float.NegativeInfinity;
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? currentItem = GetRunLoadoutItem(slotIndex);
			if (currentItem?.Shape == null)
			{
				continue;
			}

			float score = ScoreMerchantShapeOfferCandidate(currentItem, offer.ShapeItem, slotIndex);
			if (score > bestScore)
			{
				bestScore = score;
				offer.PreviewSlotIndex = slotIndex;
			}
		}
	}

	private EquipmentItem CreateMerchantPreviewItem(EquipmentItem item, WeaponModulatorData modulator, int replaceIndex = -1)
	{
		EquipmentItem previewItem = CloneRunEquipmentItem(item);
		WeaponModulatorData previewModulator = CloneRunModulator(modulator);
		if (replaceIndex >= 0 && replaceIndex < previewItem.Modulators.Count)
		{
			previewItem.Modulators[replaceIndex] = previewModulator;
		}
		else
		{
			previewItem.Modulators.Add(previewModulator);
		}

		EnsureRunLoadoutItemCoreData(previewItem);
		return previewItem;
	}

	private static float ScoreMerchantPreviewCandidate(EquipmentItem currentItem, EquipmentItem previewItem, int slotIndex, int replaceIndex)
	{
		MerchantWeaponStatPreview currentStats = BuildMerchantWeaponStatPreview(currentItem);
		MerchantWeaponStatPreview previewStats = BuildMerchantWeaponStatPreview(previewItem);

		float score = previewStats.Dps - currentStats.Dps;
		score += (previewStats.AttacksPerSecond - currentStats.AttacksPerSecond) * 7.5f;
		score += (previewStats.DamagePerProjectile - currentStats.DamagePerProjectile) * 0.35f;
		score += (previewStats.ProjectileCount - currentStats.ProjectileCount) * 14f;
		score += replaceIndex < 0 ? 1.5f : -0.35f;
		score += slotIndex == 0 ? 0.75f : 0f;
		return score;
	}

	private static float ScoreMerchantShapeOfferCandidate(EquipmentItem currentItem, EquipmentItem offeredItem, int slotIndex)
	{
		MerchantWeaponStatPreview currentStats = BuildMerchantWeaponStatPreview(currentItem);
		MerchantWeaponStatPreview offeredStats = BuildMerchantWeaponStatPreview(offeredItem);

		float score = offeredStats.Dps - currentStats.Dps;
		score += (offeredStats.AttacksPerSecond - currentStats.AttacksPerSecond) * 6.5f;
		score += (offeredStats.DamagePerProjectile - currentStats.DamagePerProjectile) * 0.30f;
		score += (offeredStats.ProjectileCount - currentStats.ProjectileCount) * 12f;
		score += slotIndex == 0 ? 0.75f : 0f;
		return score;
	}

	private string BuildArenaMerchantInspectText(MerchantOffer offer)
	{
		StringBuilder builder = new();
		string accent = MerchantToHtmlColor(GetMerchantOfferAccentColor(offer));
		builder.AppendLine($"[color={accent}]{MerchantEscapeBbcode(GetMerchantOfferCategoryLabel(offer))}[/color]  |  Cost [b]{offer.Cost}[/b] coins");

		if (!string.IsNullOrWhiteSpace(BuildMerchantOfferDescriptionText(offer)))
		{
			builder.AppendLine(MerchantEscapeBbcode(BuildMerchantOfferDescriptionText(offer).Trim()));
		}

		AppendArenaMerchantFitSection(builder, offer);

		if (offer.Kind == MerchantOfferKind.Modulator && offer.Modulator != null && offer.Modulator.Substats.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("[b]Substats[/b]");
			for (int substatIndex = 0; substatIndex < offer.Modulator.Substats.Count; substatIndex++)
			{
				builder.AppendLine($"- {MerchantEscapeBbcode(offer.Modulator.Substats[substatIndex].ToDisplayString())}");
			}
		}
		else if (offer.Kind == MerchantOfferKind.Shape && offer.ShapeItem != null && offer.ShapeItem.Substats.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("[b]Substats[/b]");
			for (int substatIndex = 0; substatIndex < offer.ShapeItem.Substats.Count; substatIndex++)
			{
				builder.AppendLine($"- {MerchantEscapeBbcode(offer.ShapeItem.Substats[substatIndex].ToDisplayString())}");
			}
		}

		return builder.ToString().TrimEnd();
	}

	private void AppendArenaMerchantFitSection(StringBuilder builder, MerchantOffer offer)
	{
		builder.AppendLine();
		builder.AppendLine("[b]Projected fit[/b]");

		if (offer.PreviewSlotIndex < 0 || offer.PreviewItem == null)
		{
			if (offer.Kind == MerchantOfferKind.Shape)
			{
				builder.AppendLine("This shape goes straight to inventory when you buy it.");
			}
			else
			{
				builder.AppendLine("No equipped weapon can socket this modulator right now.");
				builder.AppendLine("Buy it for the stash, then make room in the workshop.");
			}
			return;
		}

		EquipmentItem? currentItem = GetRunLoadoutItem(offer.PreviewSlotIndex);
		if (currentItem == null)
		{
			builder.AppendLine("The suggested weapon preview is temporarily unavailable.");
			return;
		}

		builder.AppendLine($"{MerchantEscapeBbcode(GetArenaLoadoutSlotLabel(offer.PreviewSlotIndex))}  |  {BuildMerchantPreviewInstallText(currentItem, offer)}");
		if (currentItem.Shape != null)
		{
			builder.AppendLine(offer.Kind == MerchantOfferKind.Shape
				? $"Compared against: {MerchantEscapeBbcode(currentItem.Shape.DisplayName)} Lv {currentItem.Level}"
				: $"Base weapon: {MerchantEscapeBbcode(currentItem.Shape.DisplayName)} Lv {currentItem.Level}");
		}

		MerchantWeaponStatPreview currentStats = BuildMerchantWeaponStatPreview(currentItem);
		MerchantWeaponStatPreview previewStats = BuildMerchantWeaponStatPreview(offer.PreviewItem);
		if (!currentStats.HasShape || !previewStats.HasShape)
		{
			builder.AppendLine("This offer changes utility only; no direct weapon damage preview is available.");
			return;
		}

		builder.AppendLine($"Now: DMG [b]{MerchantFormatWeaponDamage(currentStats)}[/b]  |  ATK/s [b]{MerchantFormatAttackSpeed(currentStats)}[/b]  |  DPS [b]{MerchantFormatStatNumber(currentStats.Dps)}[/b]");
		builder.AppendLine($"{(offer.Kind == MerchantOfferKind.Shape ? "Offer" : "With offer")}: DMG [b]{MerchantFormatWeaponDamage(previewStats)}[/b]  |  ATK/s [b]{MerchantFormatAttackSpeed(previewStats)}[/b]  |  DPS [b]{MerchantFormatStatNumber(previewStats.Dps)}[/b]");
		builder.AppendLine($"Delta: DMG {MerchantFormatStatDelta(previewStats.DamagePerProjectile - currentStats.DamagePerProjectile, "0.#")}  |  ATK/s {MerchantFormatStatDelta(previewStats.AttacksPerSecond - currentStats.AttacksPerSecond, "0.##", "/s")}  |  DPS {MerchantFormatStatDelta(previewStats.Dps - currentStats.Dps, "0.#")}");
		builder.AppendLine($"Profile: {previewStats.HitCount} hit{(previewStats.HitCount == 1 ? string.Empty : "s")}  |  {MerchantFormatStatNumber(previewStats.EffectiveCooldown)}s cooldown  |  {previewStats.ProjectileCount} projectile{(previewStats.ProjectileCount == 1 ? string.Empty : "s")}");
	}

	private static string BuildMerchantPreviewInstallText(EquipmentItem currentItem, MerchantOffer offer)
	{
		if (offer.Kind == MerchantOfferKind.Shape)
		{
			return "Best compared against this slot";
		}

		if (offer.PreviewReplaceIndex >= 0 && offer.PreviewReplaceIndex < currentItem.Modulators.Count)
		{
			WeaponModulatorData replaced = currentItem.Modulators[offer.PreviewReplaceIndex];
			return $"Replace socket {offer.PreviewReplaceIndex + 1}: {MerchantEscapeBbcode(replaced.DisplayName)}";
		}

		int socketNumber = Mathf.Clamp(currentItem.Modulators.Count + 1, 1, Mathf.Max(1, currentItem.UnlockedModulatorSlots));
		return $"Open socket {socketNumber}/{Mathf.Max(1, currentItem.UnlockedModulatorSlots)}";
	}

	private static MerchantWeaponStatPreview BuildMerchantWeaponStatPreview(EquipmentItem item)
	{
		if (item.Shape == null)
		{
			return default;
		}

		ShapeData shape = item.Shape;
		WeaponKind weaponKind = MerchantMapShapeToWeapon(shape.Key);
		RarityTier weaponRarity = MerchantMapRarity(item.Rarity?.Key);
		float attackSpeedPercent = Mathf.Max(0f, MerchantSumSubstatValue(item, SubstatType.AttackSpeed));
		float critChancePercent = Mathf.Max(0f, MerchantSumSubstatValue(item, SubstatType.CritChance));
		float critDamagePercent = Mathf.Max(0f, MerchantSumSubstatValue(item, SubstatType.CritDamage));
		float projectileBonus = Mathf.Max(0f, MerchantSumSubstatValue(item, SubstatType.ProjectileCount));
		int projectileCount = Mathf.Max(1, 1 + Mathf.RoundToInt(projectileBonus));
		int hitCount = Mathf.Max(1, shape.Hits);
		float scaledMainDamage = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(item)) * MerchantGetWeaponDamageMultiplier(weaponKind, weaponRarity);
		float critExpectedMultiplier = MerchantGetExpectedCritMultiplier(critChancePercent, critDamagePercent);
		float baseCooldown = Mathf.Max(0.01f, shape.Cooldown);
		float attackSpeedMultiplier = (1f + attackSpeedPercent / 100f) * MerchantGetWeaponFireMultiplier(weaponKind, weaponRarity);
		float attacksPerSecond = scaledMainDamage > 0f && shape.Cooldown < 900f
			? attackSpeedMultiplier / baseCooldown
			: 0f;
		float damagePerAttack = scaledMainDamage * hitCount * projectileCount * critExpectedMultiplier;

		return new MerchantWeaponStatPreview
		{
			HasShape = true,
			HitCount = hitCount,
			ProjectileCount = projectileCount,
			DamagePerProjectile = scaledMainDamage,
			AttacksPerSecond = attacksPerSecond,
			EffectiveCooldown = attacksPerSecond > 0f ? 1f / attacksPerSecond : 0f,
			Dps = damagePerAttack * attacksPerSecond,
		};
	}

	private static float MerchantGetWeaponFireMultiplier(WeaponKind kind, RarityTier rarity)
	{
		return MerchantGetWeaponBaseFireProfileMultiplier(kind) * MerchantGetWeaponTierMultiplier(MerchantGetRarityMultiplier(rarity));
	}

	private static float MerchantGetWeaponDamageMultiplier(WeaponKind kind, RarityTier rarity)
	{
		return MerchantGetWeaponBaseDamageProfileMultiplier(kind) * MerchantGetWeaponTierMultiplier(MerchantGetRarityMultiplier(rarity));
	}

	private static float MerchantGetExpectedCritMultiplier(float critChancePercent, float critDamagePercent)
	{
		float critChance = Mathf.Clamp(critChancePercent / 100f, 0f, 1f);
		float critBonus = MerchantPreviewCriticalDamagePercent / 100f + Mathf.Max(0f, critDamagePercent) / 100f;
		return 1f + critChance * critBonus;
	}

	private static float MerchantGetWeaponBaseFireProfileMultiplier(WeaponKind kind)
	{
		return kind switch
		{
			WeaponKind.Circle => 0.72f,
			WeaponKind.Triangle => 0.65f,
			WeaponKind.Hexagon => 0.55f,
			WeaponKind.Sphere => 0.50f,
			WeaponKind.Cube => 0.65f,
			WeaponKind.Pyramid => 0.90f,
			WeaponKind.Cone => 0.78f,
			WeaponKind.Tesseract => 0.48f,
			WeaponKind.Hyperphere => 0.55f,
			WeaponKind.Pentachoron => 0.68f,
			WeaponKind.Hypercone => 0.60f,
			WeaponKind.ImpossibleCube => 0.42f,
			WeaponKind.Mobius => 0.78f,
			WeaponKind.Klein => 0.50f,
			WeaponKind.BoysSurface => 0.58f,
			_ => 1f,
		};
	}

	private static float MerchantGetWeaponBaseDamageProfileMultiplier(WeaponKind kind)
	{
		return kind switch
		{
			WeaponKind.Circle => 1.12f,
			WeaponKind.Triangle => 1.55f,
			WeaponKind.Hexagon => 1.60f,
			WeaponKind.Sphere => 1.85f,
			WeaponKind.Cube => 1.25f,
			WeaponKind.Pyramid => 1.10f,
			WeaponKind.Cone => 1.35f,
			WeaponKind.Tesseract => 2.00f,
			WeaponKind.Hyperphere => 1.75f,
			WeaponKind.Pentachoron => 1.45f,
			WeaponKind.Hypercone => 1.65f,
			WeaponKind.ImpossibleCube => 2.30f,
			WeaponKind.Mobius => 1.05f,
			WeaponKind.Klein => 1.90f,
			WeaponKind.BoysSurface => 1.80f,
			_ => 1f,
		};
	}

	private static float MerchantGetWeaponTierMultiplier(float weaponTier)
	{
		return 1f + (weaponTier - 1f) * 0.52f;
	}

	private static float MerchantGetRarityMultiplier(RarityTier rarity)
	{
		return rarity switch
		{
			RarityTier.Uncommon => 1.16f,
			RarityTier.Rare => 1.35f,
			RarityTier.Epic => 1.60f,
			RarityTier.Legendary => 1.95f,
			RarityTier.Mythic => 2.35f,
			_ => 1f,
		};
	}

	private static WeaponKind MerchantMapShapeToWeapon(string? key)
	{
		return (key ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"circle" => WeaponKind.Circle,
			"triangle" => WeaponKind.Triangle,
			"hexagon" => WeaponKind.Hexagon,
			"sphere" => WeaponKind.Sphere,
			"cube" => WeaponKind.Cube,
			"pyramid" => WeaponKind.Pyramid,
			"cone" => WeaponKind.Cone,
			"tesseract" => WeaponKind.Tesseract,
			"hyperphere" => WeaponKind.Hyperphere,
			"hypersphere" => WeaponKind.Hyperphere,
			"pentachoron" => WeaponKind.Pentachoron,
			"hypercone" => WeaponKind.Hypercone,
			"impossiblecube" => WeaponKind.ImpossibleCube,
			"impossible_cube" => WeaponKind.ImpossibleCube,
			"mobius" => WeaponKind.Mobius,
			"klein" => WeaponKind.Klein,
			"boys" => WeaponKind.BoysSurface,
			"boyssurface" => WeaponKind.BoysSurface,
			"boys_surface" => WeaponKind.BoysSurface,
			_ => WeaponKind.Square,
		};
	}

	private static RarityTier MerchantMapRarity(string? key)
	{
		return EquipmentCatalogDefaults.NormalizeRarityKey(key) switch
		{
			"uncommon" => RarityTier.Uncommon,
			"rare" => RarityTier.Rare,
			"legendary" => RarityTier.Epic,
			"mythic" => RarityTier.Legendary,
			"divine" => RarityTier.Mythic,
			_ => RarityTier.Common,
		};
	}

	private static float MerchantSumSubstatValue(EquipmentItem item, SubstatType type)
	{
		return StatGenerator.GetTotalSubstatValue(item, type);
	}

	private static string MerchantFormatWeaponDamage(MerchantWeaponStatPreview stats)
	{
		string damage = MerchantFormatStatNumber(stats.DamagePerProjectile);
		return stats.ProjectileCount > 1 ? $"{damage} x {stats.ProjectileCount}" : damage;
	}

	private static string MerchantFormatAttackSpeed(MerchantWeaponStatPreview stats)
	{
		return stats.AttacksPerSecond > 0f ? $"{MerchantFormatStatNumber(stats.AttacksPerSecond)}/s" : "--/s";
	}

	private static string MerchantFormatStatNumber(float value)
	{
		float abs = Mathf.Abs(value);
		return abs >= 100f ? value.ToString("0.#") : value.ToString("0.##");
	}

	private static string MerchantFormatStatDelta(float value, string format, string suffix = "")
	{
		if (Mathf.Abs(value) < 0.001f)
		{
			return $"[color=#9aa8bc]+0{suffix}[/color]";
		}

		string color = value > 0f ? "#62e6a8" : "#ff6b78";
		string sign = value > 0f ? "+" : string.Empty;
		return $"[color={color}]{sign}{value.ToString(format)}{suffix}[/color]";
	}

	private static string MerchantEscapeBbcode(string text)
	{
		return text.Replace("[", "(").Replace("]", ")");
	}

	private static string MerchantToHtmlColor(Color color)
	{
		int red = Mathf.Clamp(Mathf.RoundToInt(color.R * 255f), 0, 255);
		int green = Mathf.Clamp(Mathf.RoundToInt(color.G * 255f), 0, 255);
		int blue = Mathf.Clamp(Mathf.RoundToInt(color.B * 255f), 0, 255);
		return $"#{red:X2}{green:X2}{blue:X2}";
	}
}