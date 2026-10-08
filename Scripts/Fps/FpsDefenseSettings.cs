using Godot;
using Godot.Collections;

namespace SuperGatchaRebuild.Fps;

[Tool, GlobalClass]
public partial class FpsDefenseSettings : Resource
{
	private string _mapName = "North Gate";
	private float _courtyardHalfSize = 24f;
	private Array<FpsDefenseLaneSettings> _lanes = new();
	private Vector3 _corePosition = new(0f, 0f, 6f);
	private Vector3 _shopPosition = new(-13f, 0f, 12f);
	private float _coreMaxHealth = 600f;
	private float _playerAggroRadius = 6f;
	private float _preparationSeconds = 20f;
	private int _coinsPerEnemy = 5;
	private float _weaponDropChance = 0.005f;
	private float _modulatorDropChance = 0.01f;
	private float _pickupRadius = 2.5f;
	private int _maxLoosePickups = 128;
	private int _combatShopKillInterval = 8;
	private float _combatShopDuration = 25f;
	private int _repairPrice = 25;
	private float _repairAmount = 120f;
	private int _weaponUpgradePrice = 40;
	private int _modulatorPrice = 20;
	private int _shapePrice = 30;

	[ExportGroup("Map")]
	[Export] public string MapName { get => _mapName; set => Change(ref _mapName, value); }
	[Export(PropertyHint.Range, "14,60,1")] public float CourtyardHalfSize { get => _courtyardHalfSize; set => Change(ref _courtyardHalfSize, value); }
	[Export] public Array<FpsDefenseLaneSettings> Lanes { get => _lanes; set => Change(ref _lanes, value); }
	[Export] public Vector3 CorePosition { get => _corePosition; set => Change(ref _corePosition, value); }
	[Export] public Vector3 ShopPosition { get => _shopPosition; set => Change(ref _shopPosition, value); }
	[Export(PropertyHint.Range, "1,10000,1")] public float CoreMaxHealth { get => _coreMaxHealth; set => Change(ref _coreMaxHealth, value); }
	[Export(PropertyHint.Range, "0,20,0.5")] public float PlayerAggroRadius { get => _playerAggroRadius; set => Change(ref _playerAggroRadius, value); }
	[Export(PropertyHint.Range, "0,60,1")] public float PreparationSeconds { get => _preparationSeconds; set => Change(ref _preparationSeconds, value); }

	[ExportGroup("Loot")]
	[Export(PropertyHint.Range, "0,100,1")] public int CoinsPerEnemy { get => _coinsPerEnemy; set => Change(ref _coinsPerEnemy, value); }
	[Export(PropertyHint.Range, "0,1,0.001")] public float WeaponDropChance { get => _weaponDropChance; set => Change(ref _weaponDropChance, value); }
	[Export(PropertyHint.Range, "0,1,0.001")] public float ModulatorDropChance { get => _modulatorDropChance; set => Change(ref _modulatorDropChance, value); }
	[Export(PropertyHint.Range, "1,8,0.1")] public float PickupRadius { get => _pickupRadius; set => Change(ref _pickupRadius, value); }
	[Export(PropertyHint.Range, "8,256,1")] public int MaxLoosePickups { get => _maxLoosePickups; set => Change(ref _maxLoosePickups, value); }

	[ExportGroup("Shop")]
	[Export(PropertyHint.Range, "1,100,1")] public int CombatShopKillInterval { get => _combatShopKillInterval; set => Change(ref _combatShopKillInterval, value); }
	[Export(PropertyHint.Range, "5,120,1")] public float CombatShopDuration { get => _combatShopDuration; set => Change(ref _combatShopDuration, value); }
	[Export(PropertyHint.Range, "1,500,1")] public int RepairPrice { get => _repairPrice; set => Change(ref _repairPrice, value); }
	[Export(PropertyHint.Range, "1,1000,1")] public float RepairAmount { get => _repairAmount; set => Change(ref _repairAmount, value); }
	[Export(PropertyHint.Range, "1,500,1")] public int WeaponUpgradePrice { get => _weaponUpgradePrice; set => Change(ref _weaponUpgradePrice, value); }
	[Export(PropertyHint.Range, "1,500,1")] public int ModulatorPrice { get => _modulatorPrice; set => Change(ref _modulatorPrice, value); }
	[Export(PropertyHint.Range, "1,500,1")] public int ShapePrice { get => _shapePrice; set => Change(ref _shapePrice, value); }

	private void Change<T>(ref T field, T value)
	{
		if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		EmitChanged();
	}
}
