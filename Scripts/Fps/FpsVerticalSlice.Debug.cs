using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice : IArenaDebugRuntime
{
	private const int FpsDebugLoadoutSlotCount = 5;

	private Control? _arenaDebugPanel;
	private ArenaDebugPanelUI? _arenaDebugPanelUi;
	private Button? _debugPanelButton;
	private bool _debugPanelVisible;
	private ArenaDebugOverrides _arenaDebugOverrides;
	private bool _arenaDebugOverridesActive;
	private float _debugBaseMoveSpeed;
	private float _debugBaseMaxHealth;
	private float _debugBaseFireRate;
	private float _debugBaseShotDamage;
	private float _debugBaseShotRange;
	private float _debugBaseHipSpread;
	private float _debugBaseAimSpread;
	private float _debugBaseHipFov;
	private float _debugBaseAimFov;
	private float _debugBaseRecoilKick;

	private void EnsureFpsDebugBaseStats()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player))
		{
			return;
		}

		_debugBaseMoveSpeed = _player.MoveSpeed;
		_debugBaseMaxHealth = _player.MaxHealth;
		_debugBaseFireRate = _player.FireRate;
		_debugBaseShotDamage = _player.ShotDamage;
		_debugBaseShotRange = _player.ShotRange;
		_debugBaseHipSpread = _player.HipFireSpreadDegrees;
		_debugBaseAimSpread = _player.AimSpreadDegrees;
		_debugBaseHipFov = _player.HipFov;
		_debugBaseAimFov = _player.AimFov;
		_debugBaseRecoilKick = _player.RecoilKick;
	}

	private void BuildDebugPanel(Control hudRoot)
	{
		if (_arenaDebugPanel != null && IsInstanceValid(_arenaDebugPanel))
		{
			return;
		}

		PackedScene? debugScene = GD.Load<PackedScene>("res://Scenes/UI/ArenaDebugPanel.tscn");
		if (debugScene == null)
		{
			GD.PushWarning("FPS slice: could not load ArenaDebugPanel scene.");
			return;
		}

		if (debugScene.Instantiate() is not Control panel)
		{
			GD.PushWarning("FPS slice: ArenaDebugPanel scene did not instantiate as Control.");
			return;
		}

		panel.Name = "ArenaDebugPanel";
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		panel.OffsetLeft = -760f;
		panel.OffsetTop = 48f;
		panel.OffsetRight = -12f;
		panel.OffsetBottom = 848f;
		panel.MouseFilter = Control.MouseFilterEnum.Pass;
		panel.Visible = false;
		hudRoot.AddChild(panel);

		_arenaDebugPanel = panel;
		_arenaDebugPanelUi = panel as ArenaDebugPanelUI;
		if (_arenaDebugPanelUi != null)
		{
			_arenaDebugPanelUi.EnableExternalMenuGate(true);
			_arenaDebugPanelUi.ExternalMenuVisibilityRequested += SetDebugPanelVisible;
		}

		_debugPanelVisible = false;
		RefreshDebugPanelVisibility();
	}

	private void TeardownDebugPanel()
	{
		if (_arenaDebugPanelUi != null)
		{
			_arenaDebugPanelUi.ExternalMenuVisibilityRequested -= SetDebugPanelVisible;
		}

		_arenaDebugPanelUi = null;
		_arenaDebugPanel = null;
		_debugPanelButton = null;
		_debugPanelVisible = false;
	}

	private void OnDebugPanelPressed()
	{
		SetDebugPanelVisible(!_debugPanelVisible);
		RefreshCombatState();
	}

	private void SetDebugPanelVisible(bool visible)
	{
		_debugPanelVisible = visible && _pauseRequested && !_victory && !_defeat && IsInstanceValid(_arenaDebugPanel);
		RefreshDebugPanelVisibility();
	}

	private void RefreshDebugPanelVisibility()
	{
		bool active = _debugPanelVisible && _pauseRequested && !_victory && !_defeat && IsInstanceValid(_arenaDebugPanel);
		_debugPanelVisible = active;

		if (_arenaDebugPanelUi != null)
		{
			_arenaDebugPanelUi.SetExternalMenuVisible(active);
		}
		else if (_arenaDebugPanel != null && IsInstanceValid(_arenaDebugPanel))
		{
			_arenaDebugPanel.Visible = active;
		}

		if (_debugPanelButton != null && IsInstanceValid(_debugPanelButton))
		{
			_debugPanelButton.Text = active ? "Close Debug" : "Debug Panel";
		}
	}

	private bool IsValidDebugSlot(int slotIndex)
	{
		return slotIndex >= 0 && slotIndex < FpsDebugLoadoutSlotCount;
	}

	private void EnsureArsenalCapacity(int requiredSlots)
	{
		if (requiredSlots <= 0)
		{
			return;
		}

		while (_arsenal.Count < requiredSlots)
		{
			EquipmentItem? fallback = CreateFallbackSliceWeapon();
			if (fallback == null)
			{
				break;
			}

			_arsenal.Add(fallback);
		}
	}

	private EquipmentItem? GetDebugSlotWeapon(int slotIndex)
	{
		if (!IsValidDebugSlot(slotIndex) || slotIndex >= _arsenal.Count)
		{
			return null;
		}

		return _arsenal[slotIndex];
	}

	private static string GetDebugSlotLabel(int slotIndex)
	{
		return slotIndex == 0 ? "Primary" : $"Support {slotIndex}";
	}

	private void RefreshWeaponAfterDebugLoadoutChange(int changedSlot)
	{
		if (_arsenal.Count == 0)
		{
			UpdateWeaponHud();
			return;
		}

		if (_selectedWeaponIndex < 0 || _selectedWeaponIndex >= _arsenal.Count)
		{
			_selectedWeaponIndex = Mathf.Clamp(_selectedWeaponIndex, 0, _arsenal.Count - 1);
		}

		if (_selectedWeaponIndex == changedSlot)
		{
			EquipWeapon(_selectedWeaponIndex, showNotice: false);
		}
		else
		{
			UpdateWeaponHud();
		}

		ReapplyLiveOverridesToFpsPlayer();
	}

	private bool TryCreateDebugModulator(string modulatorKey, string tierKey, out WeaponModulatorData modulator)
	{
		modulator = null!;
		if (string.IsNullOrWhiteSpace(modulatorKey))
		{
			return false;
		}

		WeaponModulatorTier tier = WeaponModulatorTier.Baseline;
		if (!string.IsNullOrWhiteSpace(tierKey))
		{
			_ = WeaponModulatorData.TryParseTierKey(tierKey, out tier);
		}

		return EquipmentCatalogDefaults.TryCreateModulatorByKey(modulatorKey, tier, out modulator);
	}

	private bool CanInstallModulatorForFpsWeapon(EquipmentItem weapon, WeaponModulatorData modulator, int replacingIndex = -1)
	{
		if (!modulator.CanApplyToWeapon(WeaponModulatorCompatibility.BuildWeaponTags(weapon)))
		{
			return false;
		}

		if (!modulator.IsWingModulator)
		{
			return IsDefensePartCompatible(modulator, weapon);
		}
		if (_arsenal.IndexOf(weapon) <= 0) return false;

		int wingCountOnWeapon = 0;
		for (int i = 0; i < weapon.Modulators.Count; i++)
		{
			if (i == replacingIndex)
			{
				continue;
			}

			if (weapon.Modulators[i].IsWingModulator)
			{
				wingCountOnWeapon++;
			}
		}

		if (wingCountOnWeapon > 0)
		{
			return false;
		}

		string pendingWingKey = modulator.NormalizedKey;
		for (int slot = 0; slot < _arsenal.Count; slot++)
		{
			EquipmentItem equipped = _arsenal[slot];
			if (ReferenceEquals(equipped, weapon))
			{
				continue;
			}

			for (int modIndex = 0; modIndex < equipped.Modulators.Count; modIndex++)
			{
				WeaponModulatorData equippedModulator = equipped.Modulators[modIndex];
				if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == pendingWingKey)
				{
					return false;
				}
			}
		}

		return true;
	}

	public ArenaDebugLoadoutSlotSnapshot GetArenaLoadoutSlotSnapshot(int slotIndex)
	{
		if (!IsValidDebugSlot(slotIndex))
		{
			return ArenaDebugLoadoutSlotSnapshot.Empty;
		}

		EquipmentItem? weapon = GetDebugSlotWeapon(slotIndex);
		Godot.Collections.Array<string> installedModulators = new();
		if (weapon != null)
		{
			for (int i = 0; i < weapon.Modulators.Count; i++)
			{
				WeaponModulatorData modulator = weapon.Modulators[i];
				string tierPrefix = modulator.UsesTierPresentation ? $"{modulator.Tier} " : string.Empty;
				installedModulators.Add($"{i + 1}. {tierPrefix}{modulator.DisplayName}");
			}
		}

		return new ArenaDebugLoadoutSlotSnapshot(
			hasRuntime: true,
			slotIndex: slotIndex,
			hasItem: weapon != null,
			slotLabel: GetDebugSlotLabel(slotIndex),
			itemLabel: weapon == null
				? "Empty"
				: $"{weapon.Shape?.DisplayName ?? "Weapon"} | {weapon.Rarity?.DisplayName ?? "Common"} | Lv {weapon.Level}",
			shapeKey: weapon?.Shape?.Key ?? string.Empty,
			rarityKey: weapon?.Rarity?.Key ?? string.Empty,
			level: weapon?.Level ?? 0,
			unlockedModulatorSlots: weapon?.UnlockedModulatorSlots ?? 0,
			availableModulatorSlots: weapon?.AvailableModulatorSlots ?? 0,
			installedModulatorLabels: installedModulators);
	}

	public bool TrySetArenaDebugLoadoutItem(int slotIndex, string shapeKey, string rarityKey, int level)
	{
		if (!IsValidDebugSlot(slotIndex) || string.IsNullOrWhiteSpace(shapeKey))
		{
			return false;
		}

		EnsureArsenalCapacity(slotIndex + 1);
		if (slotIndex >= _arsenal.Count)
		{
			return false;
		}

		ShapeData shape = EquipmentCatalogDefaults.CreateShapeByKey(shapeKey);
		RarityData rarity = EquipmentCatalogDefaults.CreateRarityByKey(rarityKey);
		ModifierData normalEssence = EquipmentCatalogDefaults.CreateModifierByKey(ModifierKey.Normal, includeBaselineNormal: true);
		StatGenerator generator = new(new RandomNumberGenerator());
		EquipmentItem item = generator.BuildItem(shape, rarity, normalEssence, StatGenerator.ClampWeaponLevel(level));
		item.Essence = null;
		item.WeaponXpProgress = 0f;
		StatGenerator.RecalculateDerivedStats(item);

		_arsenal[slotIndex] = item;
		if (_arsenal.Count == 1)
		{
			_selectedWeaponIndex = 0;
		}

		RefreshWeaponAfterDebugLoadoutChange(slotIndex);
		return true;
	}

	public bool TryClearArenaDebugLoadoutSlot(int slotIndex)
	{
		if (!IsValidDebugSlot(slotIndex))
		{
			return false;
		}

		if (slotIndex >= _arsenal.Count)
		{
			return false;
		}

		_arsenal.RemoveAt(slotIndex);
		if (_arsenal.Count == 0)
		{
			EquipmentItem? fallback = CreateFallbackSliceWeapon();
			if (fallback != null)
			{
				_arsenal.Add(fallback);
			}
		}

		if (_arsenal.Count > 0)
		{
			_selectedWeaponIndex = Mathf.Clamp(_selectedWeaponIndex, 0, _arsenal.Count - 1);
		}

		RefreshWeaponAfterDebugLoadoutChange(Mathf.Clamp(slotIndex, 0, Math.Max(0, _arsenal.Count - 1)));
		return true;
	}

	public bool TryAddArenaDebugModulator(int slotIndex, string modulatorKey, string tierKey)
	{
		if (!IsValidDebugSlot(slotIndex) || !TryCreateDebugModulator(modulatorKey, tierKey, out WeaponModulatorData modulator))
		{
			return false;
		}

		EquipmentItem? weapon = GetDebugSlotWeapon(slotIndex);
		if (weapon == null || weapon.AvailableModulatorSlots <= 0 || !CanInstallModulatorForFpsWeapon(weapon, modulator))
		{
			return false;
		}

		weapon.Modulators.Add(modulator);
		StatGenerator.RecalculateDerivedStats(weapon);
		RefreshWeaponAfterDebugLoadoutChange(slotIndex);
		return true;
	}

	public bool TryReplaceArenaDebugModulator(int slotIndex, int modulatorIndex, string modulatorKey, string tierKey)
	{
		if (!IsValidDebugSlot(slotIndex) || !TryCreateDebugModulator(modulatorKey, tierKey, out WeaponModulatorData modulator))
		{
			return false;
		}

		EquipmentItem? weapon = GetDebugSlotWeapon(slotIndex);
		if (weapon == null || modulatorIndex < 0 || modulatorIndex >= weapon.Modulators.Count)
		{
			return false;
		}

		if (!CanInstallModulatorForFpsWeapon(weapon, modulator, modulatorIndex))
		{
			return false;
		}

		weapon.Modulators[modulatorIndex] = modulator;
		StatGenerator.RecalculateDerivedStats(weapon);
		RefreshWeaponAfterDebugLoadoutChange(slotIndex);
		return true;
	}

	public bool TryRemoveArenaDebugModulator(int slotIndex, int modulatorIndex)
	{
		if (!IsValidDebugSlot(slotIndex))
		{
			return false;
		}

		EquipmentItem? weapon = GetDebugSlotWeapon(slotIndex);
		if (weapon == null || modulatorIndex < 0 || modulatorIndex >= weapon.Modulators.Count)
		{
			return false;
		}

		weapon.Modulators.RemoveAt(modulatorIndex);
		StatGenerator.RecalculateDerivedStats(weapon);
		RefreshWeaponAfterDebugLoadoutChange(slotIndex);
		return true;
	}

	public ArenaDebugStateSnapshot GetArenaDebugState()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player))
		{
			return ArenaDebugStateSnapshot.Empty;
		}

		EquipmentItem? weapon = GetCurrentWeapon();
		int projectileCount = Mathf.Max(1, 1 + Mathf.RoundToInt(_arenaDebugOverridesActive ? _arenaDebugOverrides.WeaponProjectileCountFlat : 0f));
		int ricochet = Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverridesActive ? _arenaDebugOverrides.WeaponRicochetFlat : 0f));
		int bounce = Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverridesActive ? _arenaDebugOverrides.WeaponBounceTimesFlat : 0f));

		return new ArenaDebugStateSnapshot(
			hasRuntime: true,
			overridesActive: _arenaDebugOverridesActive,
			overrides: _arenaDebugOverrides,
			moveSpeedPercent: _debugBaseMoveSpeed > 0.001f ? (_player.MoveSpeed / _debugBaseMoveSpeed) * 100f : 100f,
			maxHp: _player.MaxHealth,
			dodgeChancePercent: 0f,
			lifeStealPercent: 0f,
			healthRegenPerSecond: 0f,
			critDamagePercent: 100f,
			critChancePercent: 0f,
			weaponRange: _player.ShotRange,
			weaponSizePercent: 100f,
			weaponAttackSpeed: _player.FireRate,
			weaponDamage: weapon == null ? _player.ShotDamage : GetWeaponBaseDamage(weapon),
			weaponRicochet: ricochet,
			weaponBounceTimes: bounce,
			weaponProjectileCount: projectileCount);
	}

	public void ApplyLiveOverrides(ArenaDebugOverrides overrides)
	{
		_arenaDebugOverrides = overrides;
		_arenaDebugOverridesActive = true;
		ReapplyLiveOverridesToFpsPlayer();
	}

	public void ResetLiveOverrides()
	{
		_arenaDebugOverrides = default;
		_arenaDebugOverridesActive = false;
		ReapplyLiveOverridesToFpsPlayer();
	}

	private void ReapplyLiveOverridesToFpsPlayer()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player))
		{
			return;
		}

		EquipmentItem? weapon = GetCurrentWeapon();
		if (weapon != null)
		{
			ApplyWeaponHandling(weapon);
		}

		if (_arenaDebugOverridesActive)
		{
			float attackScale = Mathf.Max(0.05f, 1f + _arenaDebugOverrides.WeaponAttackSpeedPercent / 100f);
			float damageScale = Mathf.Max(0f, 1f + _arenaDebugOverrides.WeaponDamagePercentBonus / 100f);
			_player.FireRate = Mathf.Max(0.05f, _player.FireRate * attackScale);
			_player.ShotDamage = Mathf.Max(0f, _player.ShotDamage * damageScale);
			_player.ShotRange = Mathf.Max(1f, _player.ShotRange + _arenaDebugOverrides.RangeFlat);
		}

		OnPlayerHealthChanged(_player.CurrentHealth, _player.MaxHealth);
		UpdateWeaponHud();
	}

	public void SpawnMerchantShop()
	{
		ShowNotice("Merchant spawn is not wired in FPS slice.", 2.2f);
	}
}
