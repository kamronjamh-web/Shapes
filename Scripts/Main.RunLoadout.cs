using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild;

public partial class Main
{
	private readonly List<WeaponModulatorData> _runStoredModulators = new();
	private readonly Dictionary<string, float> _runLoadoutWeaponXp = new(StringComparer.Ordinal);
	private readonly string?[] _runLoadoutSourceInstanceIds = new string?[5];

	private void InitializeRunEquipmentLoadout()
	{
		_inventoryManager ??= GetNodeOrNull<InventoryManager>("/root/InventoryManager");
		_runLoadoutWeaponXp.Clear();
		Array.Clear(_runLoadoutSourceInstanceIds, 0, _runLoadoutSourceInstanceIds.Length);

		Array.Clear(_equippedLoadout, 0, _equippedLoadout.Length);
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? equippedItem = _inventoryManager?.GetEquippedItemAt(slotIndex);
			_runLoadoutSourceInstanceIds[slotIndex] = equippedItem?.InstanceId;
			_equippedLoadout[slotIndex] = equippedItem == null
				? null
				: CloneRunEquipmentItem(equippedItem);

			if (_equippedLoadout[slotIndex] != null && equippedItem != null)
			{
				SetRunLoadoutWeaponXp(_equippedLoadout[slotIndex]!, equippedItem.WeaponXpProgress);
			}
		}

		_runStoredModulators.Clear();
		Array.Clear(_wingSupportShotCooldowns, 0, _wingSupportShotCooldowns.Length);
		RefreshEquipmentLoadout();
	}

	private EquipmentItem? GetRunLoadoutItem(int slotIndex)
	{
		return slotIndex < 0 || slotIndex >= _equippedLoadout.Length ? null : _equippedLoadout[slotIndex];
	}

	private bool TryStoreRunModulator(WeaponModulatorData modulator)
	{
		if (modulator == null)
		{
			return false;
		}

		_runStoredModulators.Add(CloneRunModulator(modulator));
		return true;
	}

	private bool TryLevelUpRunLoadoutItem(int slotIndex)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
		{
			return false;
		}

		item.Level = Math.Min(EquipmentCatalogDefaults.MaxWeaponLevel, item.Level + 1);
		EnsureRunLoadoutItemCoreData(item);
		return true;
	}

	private void GrantXpToRunLoadoutWeapons(float amount)
	{
		if (amount <= 0f)
		{
			return;
		}

		List<int> recipientSlots = new();
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? item = GetRunLoadoutItem(slotIndex);
			if (item != null && item.Level < EquipmentCatalogDefaults.MaxWeaponLevel)
			{
				recipientSlots.Add(slotIndex);
			}
		}

		if (recipientSlots.Count == 0)
		{
			return;
		}

		float splitXp = amount / recipientSlots.Count;
		bool statsDirty = false;
		List<string> levelUpNotices = new();

		for (int i = 0; i < recipientSlots.Count; i++)
		{
			int slotIndex = recipientSlots[i];
			EquipmentItem? item = GetRunLoadoutItem(slotIndex);
			if (item == null || item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
			{
				continue;
			}

			float storedXp = GetRunLoadoutWeaponXp(item) + splitXp;
			while (item.Level < EquipmentCatalogDefaults.MaxWeaponLevel)
			{
				float requiredXp = GetWeaponXpRequiredForNextLevel(item.Level);
				if (storedXp + 0.001f < requiredXp)
				{
					break;
				}

				storedXp -= requiredXp;
				int previousSlots = item.UnlockedModulatorSlots;
				bool hadEssenceSlot = item.HasEssenceSlotUnlocked;

				if (!TryLevelUpRunLoadoutItem(slotIndex))
				{
					break;
				}

				statsDirty = true;
				item = GetRunLoadoutItem(slotIndex);
				if (item == null)
				{
					break;
				}

				levelUpNotices.Add(BuildRunLoadoutWeaponLevelUpStatus(item, previousSlots, hadEssenceSlot));
				if (item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
				{
					storedXp = 0f;
					break;
				}
			}

			if (item != null)
			{
				SetRunLoadoutWeaponXp(item, storedXp);
			}
		}

		if (statsDirty)
		{
			RefreshEquipmentLoadout();
			ApplyEquippedPrimaryToRun();
			ApplyDerivedPlayerStats(false);
		}

		if (levelUpNotices.Count > 0)
		{
			ShowStatusBanner(BuildRunLoadoutLevelUpBanner(levelUpNotices));
		}
	}

	private float GetRunLoadoutWeaponXp(EquipmentItem? item)
	{
		if (item == null || string.IsNullOrWhiteSpace(item.InstanceId) || item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
		{
			return 0f;
		}

		return _runLoadoutWeaponXp.TryGetValue(item.InstanceId, out float xp)
			? Mathf.Max(0f, xp)
			: 0f;
	}

	private void SetRunLoadoutWeaponXp(EquipmentItem item, float amount)
	{
		if (string.IsNullOrWhiteSpace(item.InstanceId))
		{
			return;
		}

		if (item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
		{
			_runLoadoutWeaponXp.Remove(item.InstanceId);
			return;
		}

		_runLoadoutWeaponXp[item.InstanceId] = Mathf.Max(0f, amount);
	}

	private static float GetWeaponXpRequiredForNextLevel(int currentLevel)
	{
		return currentLevel switch
		{
			<= 1 => 450f,
			2 => 1350f,
			3 => 3200f,
			4 => 7200f,
			_ => 0f,
		};
	}

	private void PersistRunLoadoutWeaponProgress()
	{
		_inventoryManager ??= GetNodeOrNull<InventoryManager>("/root/InventoryManager");
		if (_inventoryManager == null)
		{
			return;
		}

		bool changed = false;
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? runItem = _equippedLoadout[slotIndex];
			string? sourceInstanceId = _runLoadoutSourceInstanceIds[slotIndex];
			if (runItem == null || string.IsNullOrWhiteSpace(sourceInstanceId))
			{
				continue;
			}

			changed |= _inventoryManager.TrySetItemWeaponProgress(
				sourceInstanceId,
				runItem.Level,
				GetRunLoadoutWeaponXp(runItem),
				emitSignals: false);
		}

		if (changed)
		{
			_inventoryManager.NotifyInventoryChanged();
		}
	}

	private static string BuildRunLoadoutLevelUpBanner(IReadOnlyList<string> notices)
	{
		if (notices.Count <= 2)
		{
			return string.Join(" | ", notices);
		}

		return $"{notices[0]} | {notices[1]} | +{notices.Count - 2} more";
	}

	private static string BuildRunLoadoutWeaponLevelUpStatus(EquipmentItem item, int previousSlots, bool hadEssenceSlot)
	{
		int gainedSlots = Math.Max(0, item.UnlockedModulatorSlots - previousSlots);
		bool unlockedEssence = !hadEssenceSlot && item.HasEssenceSlotUnlocked;
		string suffix = string.Empty;
		if (gainedSlots > 0)
		{
			suffix = $" (+{gainedSlots} mod slot{(gainedSlots == 1 ? string.Empty : "s")})";
		}
		else if (unlockedEssence)
		{
			suffix = " (essence slot unlocked)";
		}

		string shapeName = item.Shape?.DisplayName ?? "Weapon";
		return $"{shapeName} Lv {item.Level}{suffix}";
	}

	private bool CanInstallRunStoredModulator(int slotIndex, int storedModulatorIndex, int replacingModulatorIndex = -1)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || storedModulatorIndex < 0 || storedModulatorIndex >= _runStoredModulators.Count)
		{
			return false;
		}

		EnsureRunLoadoutItemCoreData(item);
		if (replacingModulatorIndex >= 0)
		{
			if (item.Modulators == null || replacingModulatorIndex >= item.Modulators.Count)
			{
				return false;
			}
		}
		else if (item.AvailableModulatorSlots <= 0)
		{
			return false;
		}

		return CanInstallRunModulator(item, _runStoredModulators[storedModulatorIndex], replacingModulatorIndex);
	}

	private bool TryInstallRunStoredModulator(int slotIndex, int storedModulatorIndex)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || storedModulatorIndex < 0 || storedModulatorIndex >= _runStoredModulators.Count)
		{
			return false;
		}

		EnsureRunLoadoutItemCoreData(item);
		if (item.AvailableModulatorSlots <= 0)
		{
			return false;
		}

		WeaponModulatorData incoming = CloneRunModulator(_runStoredModulators[storedModulatorIndex]);
		if (!CanInstallRunModulator(item, incoming))
		{
			return false;
		}

		_runStoredModulators.RemoveAt(storedModulatorIndex);
		item.Modulators.Add(incoming);
		EnsureRunLoadoutItemCoreData(item);
		return true;
	}

	private bool TryReplaceRunStoredModulator(int slotIndex, int modulatorIndex, int storedModulatorIndex)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || item.Modulators == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count
			|| storedModulatorIndex < 0 || storedModulatorIndex >= _runStoredModulators.Count)
		{
			return false;
		}

		EnsureRunLoadoutItemCoreData(item);
		WeaponModulatorData incoming = CloneRunModulator(_runStoredModulators[storedModulatorIndex]);
		if (!CanInstallRunModulator(item, incoming, modulatorIndex))
		{
			return false;
		}

		WeaponModulatorData replaced = CloneRunModulator(item.Modulators[modulatorIndex]);
		_runStoredModulators.RemoveAt(storedModulatorIndex);
		item.Modulators[modulatorIndex] = incoming;
		_runStoredModulators.Add(replaced);
		EnsureRunLoadoutItemCoreData(item);
		return true;
	}

	private bool TryReturnRunModulatorToStash(int slotIndex, int modulatorIndex)
	{
		EquipmentItem? item = GetRunLoadoutItem(slotIndex);
		if (item == null || item.Modulators == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count)
		{
			return false;
		}

		WeaponModulatorData modulator = CloneRunModulator(item.Modulators[modulatorIndex]);
		item.Modulators.RemoveAt(modulatorIndex);
		EnsureRunLoadoutItemCoreData(item);
		_runStoredModulators.Add(modulator);
		return true;
	}

	private bool CanInstallRunModulator(EquipmentItem item, WeaponModulatorData modulator, int replacingModulatorIndex = -1)
	{
		if (!modulator.CanApplyToWeapon(WeaponModulatorCompatibility.BuildWeaponTags(item)))
		{
			return false;
		}

		if (!modulator.IsWingModulator)
		{
			return true;
		}

		int equippedSlot = GetRunLoadoutSlotIndex(item);
		if (equippedSlot <= 0)
		{
			return false;
		}

		if (HasRunWingModulator(item, replacingModulatorIndex))
		{
			return false;
		}

		return !HasConflictingRunWingModulator(item, modulator);
	}

	private int GetRunLoadoutSlotIndex(EquipmentItem item)
	{
		for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			if (ReferenceEquals(_equippedLoadout[slotIndex], item))
			{
				return slotIndex;
			}
		}

		return -1;
	}

	private bool HasConflictingRunWingModulator(EquipmentItem item, WeaponModulatorData? pendingModulator = null)
	{
		if (pendingModulator != null)
		{
			string pendingWingKey = WeaponModulatorData.NormalizeKey(pendingModulator.Key);
			if (string.IsNullOrWhiteSpace(pendingWingKey))
			{
				return false;
			}

			for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
			{
				EquipmentItem? equippedItem = _equippedLoadout[slotIndex];
				if (equippedItem == null || ReferenceEquals(equippedItem, item))
				{
					continue;
				}

				for (int modulatorIndex = 0; modulatorIndex < equippedItem.Modulators.Count; modulatorIndex++)
				{
					WeaponModulatorData equippedModulator = equippedItem.Modulators[modulatorIndex];
					if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == pendingWingKey)
					{
						return true;
					}
				}
			}

			return false;
		}

		for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
		{
			EquipmentItem? equippedItem = _equippedLoadout[slotIndex];
			if (equippedItem == null || ReferenceEquals(equippedItem, item))
			{
				continue;
			}

			for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
			{
				WeaponModulatorData itemModulator = item.Modulators[modulatorIndex];
				if (!itemModulator.IsWingModulator)
				{
					continue;
				}

				string wingKey = itemModulator.NormalizedKey;
				for (int equippedModulatorIndex = 0; equippedModulatorIndex < equippedItem.Modulators.Count; equippedModulatorIndex++)
				{
					WeaponModulatorData equippedModulator = equippedItem.Modulators[equippedModulatorIndex];
					if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == wingKey)
					{
						return true;
					}
				}
			}
		}

		return false;
	}

	private static bool HasRunWingModulator(EquipmentItem item, int excludedModulatorIndex = -1)
	{
		for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
		{
			if (modulatorIndex == excludedModulatorIndex)
			{
				continue;
			}

			if (item.Modulators[modulatorIndex].IsWingModulator)
			{
				return true;
			}
		}

		return false;
	}

	private static EquipmentItem CloneRunEquipmentItem(EquipmentItem source)
	{
		EquipmentItem clone = new()
		{
			InstanceId = Guid.NewGuid().ToString("N"),
			Shape = source.Shape == null ? null : CloneRunShape(source.Shape),
			Rarity = source.Rarity == null ? null : CloneRunRarity(source.Rarity),
			Essence = source.Essence == null ? null : CloneRunModifier(source.Essence),
			Level = source.Level,
			Stars = source.Stars,
			Health = source.Health,
			Damage = source.Damage,
			Substats = CloneSubstats(source.Substats),
			Modulators = CloneRunModulators(source.Modulators),
		};

		EnsureRunLoadoutItemCoreData(clone);
		return clone;
	}

	private static ShapeData CloneRunShape(ShapeData source)
	{
		return new ShapeData
		{
			Key = source.Key,
			DisplayName = source.DisplayName,
			ShapeGroup = source.ShapeGroup,
			Weight = source.Weight,
			Stars = source.Stars,
			AttackType = source.AttackType,
			AttackName = source.AttackName,
			Hits = source.Hits,
			DamagePerHit = source.DamagePerHit,
			TotalDamage = source.TotalDamage,
			Cooldown = source.Cooldown,
			SpecialDescription = source.SpecialDescription,
			Description = source.Description,
			VisualScene = source.VisualScene,
			VisualScale = source.VisualScale,
			VisualRotationDegrees = source.VisualRotationDegrees,
			WeaponScene = source.WeaponScene,
		};
	}

	private static RarityData CloneRunRarity(RarityData source)
	{
		return new RarityData
		{
			Key = source.Key,
			DisplayName = source.DisplayName,
			Weight = source.Weight,
			StatMultiplier = source.StatMultiplier,
			SubstatCount = source.SubstatCount,
			XpValue = source.XpValue,
			UiColor = source.UiColor,
		};
	}

	private static ModifierData CloneRunModifier(ModifierData source)
	{
		return new ModifierData
		{
			Key = source.Key,
			DisplayName = source.DisplayName,
			Weight = source.Weight,
			UiColor = source.UiColor,
			HpMultiplier = source.HpMultiplier,
			DamageMultiplier = source.DamageMultiplier,
			XpMultiplierBonus = source.XpMultiplierBonus,
			PrimaryEffectKey = source.PrimaryEffectKey,
			PrimaryEffectDescription = source.PrimaryEffectDescription,
			PassiveName = source.PassiveName,
			PassiveDescription = source.PassiveDescription,
			SynergyName = source.SynergyName,
			SynergyDescription = source.SynergyDescription,
			SuperSynergyName = source.SuperSynergyName,
			SuperSynergyDescription = source.SuperSynergyDescription,
			GuaranteedSubstat = source.GuaranteedSubstat,
			GrantsAllSubstats = source.GrantsAllSubstats,
			IsWildcard = source.IsWildcard,
		};
	}

	private static List<SubstatRoll> CloneSubstats(List<SubstatRoll> source)
	{
		List<SubstatRoll> clone = new(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			SubstatRoll roll = source[i];
			clone.Add(new SubstatRoll(roll.Type, roll.Value));
		}

		return clone;
	}

	private static List<WeaponModulatorData> CloneRunModulators(List<WeaponModulatorData> source)
	{
		List<WeaponModulatorData> clone = new(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			clone.Add(CloneRunModulator(source[i]));
		}

		return clone;
	}

	private static WeaponModulatorData CloneRunModulator(WeaponModulatorData source)
	{
		List<WeaponModulatorEffectData> effects = new(source.Effects.Count);
		for (int effectIndex = 0; effectIndex < source.Effects.Count; effectIndex++)
		{
			WeaponModulatorEffectData effect = source.Effects[effectIndex];
			effects.Add(new WeaponModulatorEffectData
			{
				Type = effect.Type,
				Trigger = effect.Trigger,
				Magnitude = effect.Magnitude,
				MagnitudeB = effect.MagnitudeB,
				MagnitudeC = effect.MagnitudeC,
				Count = effect.Count,
				CountB = effect.CountB,
				Key = effect.Key,
			});
		}

		WeaponModulatorData clone = new()
		{
			Key = source.Key,
			BaseDisplayName = string.IsNullOrWhiteSpace(source.BaseDisplayName) ? source.DisplayName : source.BaseDisplayName,
			DisplayName = source.DisplayName,
			BaseDescription = string.IsNullOrWhiteSpace(source.BaseDescription) ? source.Description : source.BaseDescription,
			Description = source.Description,
			PowerTier = source.PowerTier,
			Category = source.Category,
			Tags = new List<string>(source.Tags),
			AllowedWeaponTags = new List<string>(source.AllowedWeaponTags),
			BlockedWeaponTags = new List<string>(source.BlockedWeaponTags),
			SupportsTier = source.SupportsTier,
			Weight = source.Weight,
			UiColor = source.UiColor,
			Substats = CloneSubstats(source.Substats),
			Effects = effects,
			Tier = source.Tier,
		};

		clone.RefreshPresentation();
		return clone;
	}

	private static void EnsureRunLoadoutItemCoreData(EquipmentItem item)
	{
		item.Level = StatGenerator.ClampWeaponLevel(item.Level);
		item.Stars = Math.Max(1, item.Stars);
		item.Substats ??= new List<SubstatRoll>();
		item.Modulators ??= new List<WeaponModulatorData>();

		if (string.IsNullOrWhiteSpace(item.InstanceId))
		{
			item.InstanceId = Guid.NewGuid().ToString("N");
		}

		for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
		{
			item.Modulators[modulatorIndex].RefreshPresentation();
		}

		StatGenerator.RecalculateDerivedStats(item);
	}
}