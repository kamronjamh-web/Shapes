using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private FpsWeaponInspectionUI? _weaponInspectionUi;
	private Button? _inventoryButton;
	private bool _weaponInspectionOpen;
	private bool _inspectionWasPaused;
	private bool _inspectionReturnToShop;
	private int _inspectionWeaponIndex;
	private readonly System.Collections.Generic.List<CanvasItem> _inspectionHiddenHud = new();

	public bool IsWeaponInspectionOpen => _weaponInspectionOpen;
	public int InspectedWeaponIndex => _inspectionWeaponIndex;
	public int EquippedWeaponIndex => _selectedWeaponIndex;

	private void BuildWeaponInspection(Control hudRoot)
	{
		_weaponInspectionUi = new FpsWeaponInspectionUI { Name = "WeaponInspection", Visible = false };
		hudRoot.AddChild(_weaponInspectionUi);
		_weaponInspectionUi.BindWorldCamera(_player.Camera);
		_weaponInspectionUi.ShapeSelected += PreviewInspectedWeapon;
		_weaponInspectionUi.EquipRequested += () => EquipInspectedWeapon();
		_weaponInspectionUi.CloseRequested += CloseWeaponInspection;
		_weaponInspectionUi.ShapeDropped += index => TryEquipInventoryShape(index);
		_weaponInspectionUi.ShapeDropRequested += index =>
		{
			bool dropped = TryDropInventoryShape(index, out string reason);
			_weaponInspectionUi.SetStatus(dropped ? "Shape placed on the ground." : reason, !dropped);
		};
		_weaponInspectionUi.StoredModulatorDropRequested += part =>
		{
			bool dropped = TryDropInventoryModulator(part, out string reason);
			_weaponInspectionUi.SetStatus(dropped ? "Modulator placed on the ground." : reason, !dropped);
		};
		_weaponInspectionUi.CanInstallPart = (weapon, part, slot) => CanInstallInventoryModulator(weapon, part, slot, out _);
		_weaponInspectionUi.CanRemovePart = (weapon, slot) => CanRemoveInventoryModulator(weapon, slot, out _);
		_weaponInspectionUi.ModulatorDropRequested += (weapon, part, slot) =>
		{
			bool installed = TryInstallInventoryModulator(weapon, part, slot, out string reason);
			_weaponInspectionUi.SetStatus(installed ? $"Installed {part.DisplayName}." : reason, !installed);
		};
		_weaponInspectionUi.ModulatorRemoveRequested += (weapon, slot) =>
		{
			bool removed = TryRemoveInventoryModulator(weapon, slot, out string reason);
			_weaponInspectionUi.SetStatus(removed ? "Returned the part to run storage." : reason, !removed);
		};
		_weaponInspectionUi.SetInspectionVisible(false);
	}

	public void OpenWeaponInspection()
	{
		if (_weaponInspectionOpen || (_victory && !IsDefenseMap) || _defeat || _arsenal.Count == 0 || _weaponInspectionUi == null) return;
		_inspectionWasPaused = _pauseRequested;
		_inspectionReturnToShop = _shopOpen;
		if (_shopOpen) CloseDefenseShop();
		if (_gameplayTuningVisible) SetGameplayTuningVisible(false);
		if (_debugPanelVisible) SetDebugPanelVisible(false);
		_weaponInspectionOpen = true;
		_pauseRequested = true;
		_inspectionWeaponIndex = _selectedWeaponIndex;
		RefreshInventoryInspection();
		_player.SetWeaponInspectionActive(true);
		_inspectionHiddenHud.Clear();
		foreach (Node sibling in _weaponInspectionUi.GetParent().GetChildren())
			if (sibling is CanvasItem item && item != _weaponInspectionUi && item.Visible)
			{
				_inspectionHiddenHud.Add(item);
				item.Hide();
			}
		_weaponInspectionUi.SetInspectionVisible(true);
		RefreshCombatState();
	}

	public void PreviewInspectedWeapon(int arsenalIndex)
	{
		if (!_weaponInspectionOpen || GetInventoryShape(arsenalIndex) == null) return;
		_inspectionWeaponIndex = arsenalIndex;
		_weaponInspectionUi?.SetSelection(_inspectionWeaponIndex, _selectedWeaponIndex);
		_weaponInspectionUi?.SetInventory(GetInventoryStoredModulators(), GetInventoryWeaponSlots(_inspectionWeaponIndex), GetInventoryWeaponStats(_inspectionWeaponIndex));
	}

	public bool EquipInspectedWeapon()
	{
		return TryEquipInventoryShape(_inspectionWeaponIndex);
	}

	public void CloseWeaponInspection() => CloseWeaponInspectionToPreviousMenu(true);

	private void CloseWeaponInspectionToPreviousMenu(bool returnToShop)
	{
		if (!_weaponInspectionOpen) return;
		_weaponInspectionOpen = false;
		_weaponInspectionUi?.SetInspectionVisible(false);
		_player.SetWeaponInspectionActive(false);
		foreach (CanvasItem item in _inspectionHiddenHud)
			if (IsInstanceValid(item) && (item != _noticeLabel || _noticeTimer > 0f)) item.Show();
		_inspectionHiddenHud.Clear();
		_pauseRequested = _inspectionWasPaused;
		bool reopenShop = returnToShop && _inspectionReturnToShop && !_pauseRequested && IsDefenseShopAvailable && (_victory || IsNearDefenseShop());
		_inspectionReturnToShop = false;
		RefreshCombatState();
		if (reopenShop) OpenDefenseShop();
	}

	private bool HandleWeaponInspectionInput(InputEvent input)
	{
		if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab })
		{
			if (_weaponInspectionOpen) CloseWeaponInspection();
			else OpenWeaponInspection();
			return true;
		}
		if (!_weaponInspectionOpen) return false;
		if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) CloseWeaponInspection();
		return true;
	}
}
