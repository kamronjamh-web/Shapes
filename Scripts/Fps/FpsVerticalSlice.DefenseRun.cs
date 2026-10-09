using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private sealed class DefensePickup
	{
		public MeshInstance3D Visual = null!;
		public int Coins;
		public EquipmentItem? Weapon;
		public WeaponModulatorData? Modulator;
	}
	private readonly List<DefensePickup> _defensePickups = new();
	private readonly List<WeaponModulatorData> _recoveredModulators = new();
	private readonly List<WeaponModulatorData> _shopStock = new();
	private readonly List<EquipmentItem> _shopShapes = new();
	private readonly RandomNumberGenerator _defenseRng = new();
	private List<WeaponModulatorData> _defenseModulatorPool = new();
	private int _defenseCoins;
	private float _defenseCoinRemainder;
	private int _killsSinceShop;
	private float _combatShopRemaining;
	private bool _shopOpen;
	private ColorRect? _shopBackdrop;
	private VBoxContainer? _shopContent;
	private Label? _shopBalance;
	private readonly List<(Button Button, Func<bool> Enabled)> _shopButtons = new();
	private bool IsDefenseShopAvailable => IsDefenseMap && !_defeat && (_victory || _waitingForNextWave || _combatShopRemaining > 0f);

	private void InitializeDefenseRun()
	{
		if (!IsDefenseMap) return;
		_defenseRng.Randomize();
		_rewardedDefenseWaves.Clear();
		_lastWaveModulatorReward = string.Empty;
		_defenseModulatorPool = ModulatorCatalog.CreateWeightedRollPool();
		RestockDefenseShop();
	}

	private bool HandleDefenseInput(InputEvent input)
	{
		if (!IsDefenseMap || input is not InputEventKey key || !key.Pressed || key.Echo) return false;
		if (_shopOpen)
		{
			if (key.Keycode is Key.Escape or Key.F) CloseDefenseShop();
			return true;
		}
		if (_pauseRequested || _victory || _defeat) return false;
		if (key.Keycode == Key.F && IsDefenseShopAvailable && IsNearDefenseShop())
		{
			OpenDefenseShop();
			return true;
		}
		if (key.Keycode == Key.N && _waitingForNextWave)
		{
			_nextWaveDelayTimer = 0f;
			return true;
		}
		return false;
	}

	private bool IsNearDefenseShop() => _player.GlobalPosition.DistanceSquaredTo(ResolveDefenseGroundPosition(DefenseSettings!.ShopPosition)) < 25f;

	private void TickDefenseRun(float dt)
	{
		if (!IsDefenseMap) return;
		if (!_pauseRequested && !_victory && !_defeat)
		{
			_combatShopRemaining = Mathf.Max(0f, _combatShopRemaining - dt);
			for (int index = _defensePickups.Count - 1; index >= 0; index--)
			{
				DefensePickup pickup = _defensePickups[index];
				pickup.Visual.RotateY(dt * 1.4f);
				Vector3 offset = pickup.Visual.GlobalPosition - _player.GlobalPosition;
				offset.Y = 0f;
				if (offset.LengthSquared() <= DefenseSettings!.PickupRadius * DefenseSettings.PickupRadius) CollectDefensePickup(pickup);
			}
		}
		if (_shopMarker != null && IsInstanceValid(_shopMarker)) _shopMarker.Visible = IsDefenseShopAvailable;
		if (_shopOpen && !IsDefenseShopAvailable) CloseDefenseShop();
		if (_shopOpen)
		{
			if (_shopBalance != null) _shopBalance.Text = $"{_defenseCoins} coins | {(_victory ? "Defense complete / final workshop" : _waitingForNextWave ? "Preparation / wave clock held" : $"Combat continues / shop {_combatShopRemaining:0}s")}";
			foreach (var entry in _shopButtons) entry.Button.Disabled = !entry.Enabled();
		}
		if (_defenseHud != null && _defenseCore != null)
		{
			string shop = _victory ? "Final reward / workshop available on the results screen" : IsDefenseShopAvailable ? (IsNearDefenseShop() ? "F: open field shop" : UsesFortifications ? "Shop available near the central core" : IsHillMap ? "Shop available inside the summit room" : "Field shop available in the west bay") : "Shop returns after more kills";
			string gates = UsesFortifications ? $"\n{DefenseGateStatus()}" : string.Empty;
			_defenseHud.Text = $"CORE {Mathf.CeilToInt(_defenseCore.CurrentHealth)}/{Mathf.CeilToInt(_defenseCore.MaxHealth)}   |   COINS {_defenseCoins}\n{shop}{(_waitingForNextWave && !_victory ? "   |   N: start next wave" : string.Empty)}{gates}";
		}
	}

	private void SpawnDefenseLoot(FpsSliceEnemy enemy)
		=> SpawnDefenseLoot(enemy.GlobalPosition);

	private void SpawnDefenseLoot(Vector3 position)
	{
		if (!IsDefenseMap || _defeat || _victory) return;
		position.Y = (IsHillMap ? SampleHillSurfaceHeight(position) : 0f) + 0.45f;
		float luck = GetFpsModulatorLuckMultiplier();
		float coinValue = Mathf.Max(0, DefenseSettings!.CoinsPerEnemy) * luck + _defenseCoinRemainder;
		int coins = Mathf.FloorToInt(coinValue);
		_defenseCoinRemainder = coinValue - coins;
		DefensePickup? merged = _defensePickups.Find(p => p.Coins > 0 && p.Visual.Position.DistanceSquaredTo(position) < 9f);
		if (merged != null) merged.Coins += coins;
		else if (coins > 0) AddDefensePickup(position, coins, null, null);
		if (_defenseRng.Randf() < Mathf.Clamp(DefenseSettings.WeaponDropChance * luck, 0f, 1f))
		{
			List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
			ShapeData shape = WeightedRandom.Pick(shapes, item => item.Weight, _defenseRng);
			EquipmentItem weapon = CreateDefenseShapeWeapon(shape);
			AddDefensePickup(position + Vector3.Right * 0.65f, 0, weapon, null);
		}
		if (_defenseModulatorPool.Count > 0 && _defenseRng.Randf() < Mathf.Clamp(DefenseSettings.ModulatorDropChance * luck, 0f, 1f))
		{
			WeaponModulatorData? part = ModulatorLoot.Roll(_defenseModulatorPool, _defenseRng);
			if (part != null) AddDefensePickup(position + Vector3.Left * 0.65f, 0, null, part);
		}
		_killsSinceShop++;
		if (_killsSinceShop >= Mathf.Max(1, DefenseSettings.CombatShopKillInterval))
		{
			_killsSinceShop = 0;
			if (_combatShopRemaining <= 0f) RestockDefenseShop();
			_combatShopRemaining = Mathf.Max(5f, DefenseSettings.CombatShopDuration);
			ShowNotice(IsHillMap ? "The summit shop opened. Combat continues while shopping." : "A field shop appeared in the west bay. Combat continues while shopping.", 3f);
		}
	}

	private void AddDefensePickup(Vector3 position, int coins, EquipmentItem? weapon, WeaponModulatorData? modulator)
	{
		Color color = weapon != null ? new Color(0.23f, 0.75f, 1f) : modulator != null ? ModulatorLoot.GetTierColor(modulator.PowerTier) : new Color(1f, 0.76f, 0.25f);
		Mesh mesh = weapon != null ? new BoxMesh { Size = new Vector3(0.55f, 0.55f, 0.55f) }
			: modulator != null ? new CylinderMesh { TopRadius = 0f, BottomRadius = 0.30f, Height = 0.65f, RadialSegments = 4 }
			: new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 8, Rings = 4 };
		MeshInstance3D visual = AddDefenseVisual(mesh, position, color);
		DefensePickup pickup = new() { Visual = visual, Coins = coins, Weapon = weapon, Modulator = modulator };
		_defensePickups.Add(pickup);
		if (_defensePickups.Count > Mathf.Max(8, DefenseSettings!.MaxLoosePickups)) CollectDefensePickup(_defensePickups[0]);
	}

	private void CollectDefensePickup(DefensePickup pickup)
	{
		_defenseCoins += pickup.Coins;
		if (pickup.Weapon != null)
		{
			_arsenal.Add(pickup.Weapon);
			ShowNotice($"Recovered {pickup.Weapon.Shape!.DisplayName}. Q/E or mouse wheel switches shapes.", 2.5f);
		}
		if (pickup.Modulator != null)
		{
			_recoveredModulators.Add(pickup.Modulator);
			ShowNotice($"Recovered {pickup.Modulator.DisplayName}. Install it at a field shop.", 2.5f);
		}
		pickup.Visual.QueueFree();
		_defensePickups.Remove(pickup);
	}

	private void SweepDefenseLoot()
	{
		while (_defensePickups.Count > 0) CollectDefensePickup(_defensePickups[0]);
	}

	private void RestockDefenseShop()
	{
		_shopStock.Clear();
		_shopShapes.Clear();
		List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
		for (int offer = 0; offer < 3 && shapes.Count > 0; offer++)
		{
			ShapeData shape = WeightedRandom.Pick(shapes, candidate => candidate.Weight, _defenseRng);
			_shopShapes.Add(CreateDefenseShapeWeapon(shape)); shapes.Remove(shape);
		}
		EquipmentItem? weapon = GetCurrentWeapon();
		List<WeaponModulatorData> pool = _defenseModulatorPool.FindAll(mod => weapon != null && CanInstallModulatorForFpsWeapon(weapon, mod));
		while (_shopStock.Count < 3 && pool.Count > 0)
		{
			WeaponModulatorData? part = ModulatorLoot.Roll(pool, _defenseRng);
			if (part == null) break;
			_shopStock.Add(part);
			pool.RemoveAll(candidate => candidate.NormalizedKey == part.NormalizedKey);
		}
	}

	private void BuildDefenseHud(Control hudRoot)
	{
		if (!IsDefenseMap) return;
		_defenseHud = CreateHudLabel(string.Empty, 19, new Vector2(28f, 65f), 850f);
		_defenseHud.Size = new Vector2(850f, 64f);
		hudRoot.AddChild(_defenseHud);
		_shopBackdrop = new ColorRect { Name = "FieldShopOverlay", Visible = false, Color = new Color(0.02f, 0.05f, 0.07f, 0.88f), MouseFilter = Control.MouseFilterEnum.Stop };
		StretchToFullRect(_shopBackdrop);
		hudRoot.AddChild(_shopBackdrop);
		PanelContainer panel = new() { Position = new Vector2(450f, 130f), Size = new Vector2(700f, 640f), MouseFilter = Control.MouseFilterEnum.Stop };
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		panel.OffsetLeft = -350f;
		panel.OffsetRight = 350f;
		panel.OffsetTop = -320f;
		panel.OffsetBottom = 320f;
		StyleBoxFlat style = new() { BgColor = new Color(0.06f, 0.12f, 0.15f), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 20, ContentMarginBottom = 20 };
		panel.AddThemeStyleboxOverride("panel", style);
		_shopBackdrop.AddChild(panel);
		ScrollContainer scroll = new() { Name = "DefenseShopScroll", CustomMinimumSize = new Vector2(650f, 580f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		panel.AddChild(scroll);
		_shopContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_shopContent.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_shopContent);
	}

	private void OpenDefenseShop()
	{
		if (!IsDefenseShopAvailable || (!_victory && !IsNearDefenseShop())) return;
		_shopOpen = true;
		_shopBackdrop!.Visible = true;
		RebuildDefenseShopMenu();
		RefreshCombatState();
	}

	private void CloseDefenseShop()
	{
		if (!_shopOpen) return;
		_shopOpen = false;
		if (_shopBackdrop != null) _shopBackdrop.Visible = false;
		RefreshCombatState();
	}

	private void RebuildDefenseShopMenu()
	{
		if (_shopContent == null) return;
		foreach (Node child in _shopContent.GetChildren()) { _shopContent.RemoveChild(child); child.QueueFree(); }
		_shopButtons.Clear();
		AddShopLabel("FIELD SHOP", 30);
		_shopBalance = AddShopLabel(string.Empty, 18);
		EquipmentItem? weapon = GetCurrentWeapon();
		AddShopLabel($"{weapon?.Shape?.DisplayName} / Level {weapon?.Level} / Modulators {weapon?.Modulators.Count}/{weapon?.UnlockedModulatorSlots}", 20);
		Button inspect = CreateOverlayButton("Inventory / inspect equipped shape");
		inspect.Name = "InspectWeaponButton";
		inspect.Pressed += OpenWeaponInspection;
		_shopContent.AddChild(inspect);
		HBoxContainer switchRow = new();
		_shopContent.AddChild(switchRow);
		foreach (int step in new[] { -1, 1 })
		{
			Button button = CreateOverlayButton(step < 0 ? "Previous shape" : "Next shape");
			button.Pressed += () => { StepWeapon(step); RebuildDefenseShopMenu(); };
			switchRow.AddChild(button);
		}
		FpsDefenseSettings settings = DefenseSettings!;
		AddShopButton($"Repair core +{settings.RepairAmount:0} / {settings.RepairPrice} coins",
			() => _defenseCore != null && _defenseCore.CurrentHealth < _defenseCore.MaxHealth && _defenseCoins >= settings.RepairPrice,
			() => { if (!TrySpendDefenseCoins(settings.RepairPrice)) return; _defenseCore!.Repair(settings.RepairAmount); RebuildDefenseShopMenu(); });
		AddShopButton($"Upgrade current shape / {settings.WeaponUpgradePrice} coins",
			() => GetCurrentWeapon() is { } current && current.Level < EquipmentCatalogDefaults.MaxWeaponLevel && _defenseCoins >= settings.WeaponUpgradePrice,
			() => { EquipmentItem? current = GetCurrentWeapon(); if (current == null || current.Level >= EquipmentCatalogDefaults.MaxWeaponLevel || !TrySpendDefenseCoins(settings.WeaponUpgradePrice)) return; current.Level++; RefreshDefenseWeapon(current); });
		AddShopLabel("BASE WEAPON SHAPES", 18);
		foreach (EquipmentItem shapeWeapon in _shopShapes)
			AddShopButton($"Buy {shapeWeapon.Shape!.DisplayName} / {settings.ShapePrice} coins", () => _shopShapes.Contains(shapeWeapon) && _defenseCoins >= settings.ShapePrice,
				() => { if (!_shopShapes.Contains(shapeWeapon) || !TrySpendDefenseCoins(settings.ShapePrice)) return; _shopShapes.Remove(shapeWeapon); _arsenal.Add(shapeWeapon); EquipWeapon(_arsenal.Count - 1, false); RebuildDefenseShopMenu(); });
		AddShopLabel("SHOP MODULATORS", 18);
		foreach (WeaponModulatorData part in _shopStock)
			AddDefenseModulatorOffer(part, true);
		AddShopLabel($"RECOVERED MODULATORS ({_recoveredModulators.Count}) / free installation", 18);
		if (!string.IsNullOrEmpty(_lastWaveModulatorReward)) AddShopLabel($"Last wave reward: {_lastWaveModulatorReward}", 15);
		foreach (WeaponModulatorData part in _recoveredModulators)
			AddDefenseModulatorOffer(part, false);
		BuildDefenseModulatorWorkshop(weapon);
		AddShopButton(_victory ? "Back to results / F or Esc" : "Return to defense / F or Esc", () => true, CloseDefenseShop);
	}

	private bool TrySpendDefenseCoins(int amount)
	{
		if (!IsDefenseShopAvailable || !_shopOpen || amount < 0 || _defenseCoins < amount) return false;
		_defenseCoins -= amount;
		return true;
	}
	private static EquipmentItem CreateDefenseShapeWeapon(ShapeData shape)
	{
		EquipmentItem weapon = new() { Shape = shape, Rarity = EquipmentCatalogDefaults.CreateRarityByKey("common"), Level = 1, Stars = shape.Stars };
		StatGenerator.RecalculateDerivedStats(weapon);
		return weapon;
	}
	private bool CanInstallDefenseModulator(WeaponModulatorData part)
	{
		EquipmentItem? weapon = GetCurrentWeapon();
		return IsDefenseShopAvailable && weapon != null && weapon.AvailableModulatorSlots > 0 && CanInstallModulatorForFpsWeapon(weapon, part);
	}
	private static bool IsDefensePartCompatible(WeaponModulatorData part, EquipmentItem? weapon)
		=> WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out _);
	private void InstallDefenseModulator(WeaponModulatorData part)
	{
		EquipmentItem weapon = GetCurrentWeapon()!;
		weapon.Modulators.Add(CloneSliceModulator(part));
		RefreshDefenseWeapon(weapon);
	}
	private void RefreshDefenseWeapon(EquipmentItem weapon)
	{
		StatGenerator.RecalculateDerivedStats(weapon);
		ApplyWeaponHandling(weapon);
		_player.ApplyWeaponItemVisual(weapon);
		UpdateWeaponHud();
		RebuildDefenseShopMenu();
	}
	private Label AddShopLabel(string text, int fontSize)
	{
		Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		_shopContent!.AddChild(label);
		return label;
	}
	private void AddShopButton(string text, Func<bool> enabled, Action action)
	{
		Button button = CreateOverlayButton(text);
		button.Disabled = !enabled();
		button.Pressed += () => { if (enabled()) action(); };
		_shopContent!.AddChild(button);
		_shopButtons.Add((button, enabled));
	}
}
