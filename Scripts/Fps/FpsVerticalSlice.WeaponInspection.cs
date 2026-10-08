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

	public bool IsWeaponInspectionOpen => _weaponInspectionOpen;
	public int InspectedWeaponIndex => _inspectionWeaponIndex;
	public int EquippedWeaponIndex => _selectedWeaponIndex;

	private void BuildWeaponInspection(Control hudRoot)
	{
		_weaponInspectionUi = new FpsWeaponInspectionUI { Name = "WeaponInspection", Visible = false };
		hudRoot.AddChild(_weaponInspectionUi);
		_weaponInspectionUi.ShapeSelected += PreviewInspectedWeapon;
		_weaponInspectionUi.EquipRequested += () => EquipInspectedWeapon();
		_weaponInspectionUi.CloseRequested += CloseWeaponInspection;
		_weaponInspectionUi.SetInspectionVisible(false);
	}

	public void OpenWeaponInspection()
	{
		if (_weaponInspectionOpen || _victory || _defeat || _arsenal.Count == 0 || _weaponInspectionUi == null) return;
		_inspectionWasPaused = _pauseRequested;
		_inspectionReturnToShop = _shopOpen;
		if (_shopOpen) CloseDefenseShop();
		if (_gameplayTuningVisible) SetGameplayTuningVisible(false);
		if (_debugPanelVisible) SetDebugPanelVisible(false);
		_weaponInspectionOpen = true;
		_pauseRequested = true;
		_inspectionWeaponIndex = _selectedWeaponIndex;
		_weaponInspectionUi.SetWeapons(_arsenal, _inspectionWeaponIndex, _selectedWeaponIndex);
		_weaponInspectionUi.SetInspectionVisible(true);
		RefreshCombatState();
	}

	public void PreviewInspectedWeapon(int arsenalIndex)
	{
		if (!_weaponInspectionOpen || arsenalIndex < 0 || arsenalIndex >= _arsenal.Count) return;
		_inspectionWeaponIndex = arsenalIndex;
		_weaponInspectionUi?.SetSelection(_inspectionWeaponIndex, _selectedWeaponIndex);
	}

	public bool EquipInspectedWeapon()
	{
		if (!_weaponInspectionOpen || _victory || _defeat || _inspectionWeaponIndex < 0 || _inspectionWeaponIndex >= _arsenal.Count) return false;
		if (_inspectionWeaponIndex != _selectedWeaponIndex) EquipWeapon(_inspectionWeaponIndex, false);
		_weaponInspectionUi?.SetSelection(_inspectionWeaponIndex, _selectedWeaponIndex);
		return true;
	}

	public void CloseWeaponInspection() => CloseWeaponInspectionToPreviousMenu(true);

	private void CloseWeaponInspectionToPreviousMenu(bool returnToShop)
	{
		if (!_weaponInspectionOpen) return;
		_weaponInspectionOpen = false;
		_weaponInspectionUi?.SetInspectionVisible(false);
		_pauseRequested = _inspectionWasPaused;
		bool reopenShop = returnToShop && _inspectionReturnToShop && !_pauseRequested && IsDefenseShopAvailable && IsNearDefenseShop();
		_inspectionReturnToShop = false;
		RefreshCombatState();
		if (reopenShop) OpenDefenseShop();
	}

	private bool HandleWeaponInspectionInput(InputEvent input)
	{
		if (!_weaponInspectionOpen) return false;
		if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) CloseWeaponInspection();
		return true;
	}
}
