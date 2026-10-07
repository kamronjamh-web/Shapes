using System;
using Godot;
using SuperGatchaRebuild;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.DebugTools;

// Central debug command endpoint used by hub and arena debug panels.
public partial class DebugCommandService : Node
{
    [Export] public NodePath CurrencyManagerPath = new NodePath();
    [Export] public NodePath DebugManagerPath = new NodePath();

    [Export] public NodePath ArenaManagerPath = new NodePath();
    [Export] public NodePath EnemySpawnerPath = new NodePath();
    [Export] public NodePath BossManagerPath = new NodePath();
    [Export] public NodePath PlayerStatsPath = new NodePath();

    [Export] public NodePath AugmentServicePath = new NodePath();
    [Export] public NodePath InventoryDebugServicePath = new NodePath();
    [Export] public NodePath StatsDebugServicePath = new NodePath();
    [Export] public NodePath ForgeServicePath = new NodePath();

    [Export] public Godot.Collections.Array<ShapeData> AllShapes = new();
    [Export] public RarityData? AddAllShapesRarity;
    [Export] public ModifierData? AddAllShapesModifier;
    [Export] public int AddAllShapesLevel = 1;

    private CurrencyManager? _currencyManager;
    private DebugManager? _debugManager;

    private IArenaDebugRuntime? _arenaRuntime;
    private IEnemySpawnerDebugRuntime? _enemySpawner;
    private IBossDebugRuntime? _bossRuntime;
    private IItemDropDebugRuntime? _itemDropRuntime;
    private IPlayerDebugProgression? _playerProgression;
    private IAugmentDebugService? _augmentService;
    private IInventoryDebugCommands? _inventoryDebugService;
    private IStatsDebugService? _statsDebugService;
    private DebugForgeService? _forgeService;

    public override void _Ready()
    {
        ResolveDependencies();
    }

    public void AddRollCurrency(int amount = 1)
    {
        EnsureRuntimeDependencies();
        _currencyManager?.AddRollCurrency(amount);
    }

    public void AddFatedRollCurrency(int amount = 1)
    {
        EnsureRuntimeDependencies();
        _currencyManager?.AddFatedRollCurrency(amount);
    }

    public void AddCoins(int amount)
    {
        EnsureRuntimeDependencies();
        _currencyManager?.AddCoins(amount);
    }

    public void LevelUp(int amount = 1)
    {
        EnsureRuntimeDependencies();
        _playerProgression?.AddLevels(Mathf.Max(1, amount));
        _playerProgression?.SaveProgress();
    }

    public int AddAllShapes()
    {
        EnsureRuntimeDependencies();

        if (_forgeService != null && AddAllShapesRarity != null && AddAllShapesModifier != null && AllShapes.Count > 0)
        {
            int added = _forgeService.AddAllShapes(AllShapes, AddAllShapesRarity, AddAllShapesModifier, AddAllShapesLevel, out _);
            return added;
        }

        return _inventoryDebugService?.AddAllShapesToInventory() ?? 0;
    }

    public void AddAugment(string augmentType, int level)
    {
        EnsureRuntimeDependencies();
        _augmentService?.AddAugment(augmentType, level);
    }

    public void SimulateRolls(int amount = 1000, bool addToInventory = false)
    {
        EnsureRuntimeDependencies();
        _statsDebugService?.SimulateRolls(Mathf.Max(1, amount), addToInventory);
    }

    public void ResetObservedCounts()
    {
        EnsureRuntimeDependencies();
        _statsDebugService?.ResetObservedCounts();
    }

    public void DeactivateDebug()
    {
        EnsureRuntimeDependencies();
        _debugManager?.DeactivateDebug();
    }

    public void ApplyArenaOverrides(ArenaDebugOverrides overrides)
    {
        EnsureRuntimeDependencies();
        _arenaRuntime?.ApplyLiveOverrides(overrides);
    }

    public void ResetArenaOverrides()
    {
        EnsureRuntimeDependencies();
        _arenaRuntime?.ResetLiveOverrides();
        _playerProgression?.RebuildFromEquipment();
    }

    public ArenaDebugStateSnapshot GetArenaDebugState()
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.GetArenaDebugState() ?? ArenaDebugStateSnapshot.Empty;
    }

    public void SpawnMerchantShop()
    {
        EnsureRuntimeDependencies();
        _arenaRuntime?.SpawnMerchantShop();
    }

    public ArenaDebugLoadoutSlotSnapshot GetArenaLoadoutSlotSnapshot(int slotIndex)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.GetArenaLoadoutSlotSnapshot(slotIndex) ?? ArenaDebugLoadoutSlotSnapshot.Empty;
    }

    public bool TrySetArenaDebugLoadoutItem(int slotIndex, string shapeKey, string rarityKey, int level)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.TrySetArenaDebugLoadoutItem(slotIndex, shapeKey, rarityKey, level) == true;
    }

    public bool TryClearArenaDebugLoadoutSlot(int slotIndex)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.TryClearArenaDebugLoadoutSlot(slotIndex) == true;
    }

    public bool TryAddArenaDebugModulator(int slotIndex, string modulatorKey, string tierKey)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.TryAddArenaDebugModulator(slotIndex, modulatorKey, tierKey) == true;
    }

    public bool TryReplaceArenaDebugModulator(int slotIndex, int modulatorIndex, string modulatorKey, string tierKey)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.TryReplaceArenaDebugModulator(slotIndex, modulatorIndex, modulatorKey, tierKey) == true;
    }

    public bool TryRemoveArenaDebugModulator(int slotIndex, int modulatorIndex)
    {
        EnsureRuntimeDependencies();
        return _arenaRuntime?.TryRemoveArenaDebugModulator(slotIndex, modulatorIndex) == true;
    }

    public void SpawnStressWave(int count, float hpMultiplier)
    {
        EnsureRuntimeDependencies();
        _enemySpawner?.SpawnEnemies(Mathf.Max(1, count), Mathf.Max(0.1f, hpMultiplier));
    }

    public void SpawnGrunt(float hpMultiplier)
    {
        SpawnEnemy(EnemyKind.Grunt, 1, hpMultiplier);
    }

    public void SpawnEnemy(EnemyKind kind, int count, float hpMultiplier)
    {
        EnsureRuntimeDependencies();
        _enemySpawner?.SpawnEnemy(kind, Mathf.Max(1, count), Mathf.Max(0.1f, hpMultiplier));
    }

    public void SpawnAllEnemyTypes(float hpMultiplier)
    {
        EnsureRuntimeDependencies();

        float safeHpMultiplier = Mathf.Max(0.1f, hpMultiplier);
        foreach (EnemyKind kind in (EnemyKind[])Enum.GetValues(typeof(EnemyKind)))
        {
            _enemySpawner?.SpawnEnemy(kind, 1, safeHpMultiplier);
        }
    }

    public void SpawnMonolith()
    {
        EnsureRuntimeDependencies();
        _bossRuntime?.SpawnMonolith();
    }

    public void SpawnInterceptor(float hpMultiplier)
    {
        EnsureRuntimeDependencies();
        _enemySpawner?.SpawnInterceptor(1, Mathf.Max(0.1f, hpMultiplier));
    }

    public void SpawnOverseer()
    {
        EnsureRuntimeDependencies();
        _bossRuntime?.SpawnOverseer();
    }

    public void SpawnCube()
    {
        EnsureRuntimeDependencies();
        _bossRuntime?.SpawnCube();
    }

    public void KillAllEnemies()
    {
        EnsureRuntimeDependencies();
        _enemySpawner?.KillAllEnemies();
    }

    public void SimulateItemDropPlaceholder()
    {
        SpawnRandomShapeItemDrop();
    }

    public void SpawnRandomShapeItemDrop()
    {
        EnsureRuntimeDependencies();
        _itemDropRuntime?.SpawnRandomShapeItemDrop();
    }

    public void SpawnRandomShapeItemDrop(string rarityKey)
    {
        EnsureRuntimeDependencies();
        _itemDropRuntime?.SpawnRandomShapeItemDrop(rarityKey);
    }

    public void SpawnEssenceDrop(ModifierKey key)
    {
        EnsureRuntimeDependencies();
        _itemDropRuntime?.SpawnEssenceDrop(key);
    }

    public void SpawnModulatorDrop(string key)
    {
        EnsureRuntimeDependencies();
        _itemDropRuntime?.SpawnModulatorDrop(key);
    }

    public void GrantAllItemsX1()
    {
        EnsureRuntimeDependencies();
        _inventoryDebugService?.GrantAllItemsX1();
    }

    public void ClearAllItems()
    {
        EnsureRuntimeDependencies();
        _inventoryDebugService?.ClearAllItems();
    }

    public void ReturnToHub(string hubScenePath)
    {
        if (string.IsNullOrWhiteSpace(hubScenePath))
        {
            return;
        }

        if (!FileAccess.FileExists(hubScenePath))
        {
            GD.PushWarning($"Hub scene not found: {hubScenePath}");
            return;
        }

        EnsureRuntimeDependencies();
        ArenaRunOutcome outcome = CaptureArenaRunOutcome();

        Error err = GetTree().ChangeSceneToFile(hubScenePath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Failed to return to hub scene: {err}");
            return;
        }

        ApplyArenaRunOutcome(outcome);
    }

    private void ResolveDependencies()
    {
        _currencyManager = ResolveNode<CurrencyManager>(CurrencyManagerPath, "CurrencyManager");
        _debugManager = ResolveNode<DebugManager>(DebugManagerPath, "DebugManager");

        _arenaRuntime = ResolveNode<IArenaDebugRuntime>(ArenaManagerPath, "ArenaManager");
        _enemySpawner = ResolveNode<IEnemySpawnerDebugRuntime>(EnemySpawnerPath, "EnemySpawner");
        _bossRuntime = ResolveNode<IBossDebugRuntime>(BossManagerPath, "BossManager");
        _itemDropRuntime = ResolveNode<IItemDropDebugRuntime>(ArenaManagerPath, "ArenaManager");
        _playerProgression = ResolveNode<IPlayerDebugProgression>(PlayerStatsPath, "PlayerStats");
        _playerProgression ??= GetNodeOrNull("/root/GameStateManager") as IPlayerDebugProgression;

        _augmentService = ResolveNode<IAugmentDebugService>(AugmentServicePath, "AugmentService");
        _inventoryDebugService = ResolveNode<IInventoryDebugCommands>(InventoryDebugServicePath, "InventoryManager");
        _statsDebugService = ResolveNode<IStatsDebugService>(StatsDebugServicePath, "GameStateManager");

        _forgeService = ResolveNode<DebugForgeService>(ForgeServicePath, "DebugForgeService");
    }

    private void EnsureRuntimeDependencies()
    {
        ResolveDependencies();

        Node? sceneRoot = GetTree().CurrentScene;
        if (sceneRoot == null)
        {
            return;
        }

        _arenaRuntime = EnsureValidNodeRef(_arenaRuntime);
        _enemySpawner = EnsureValidNodeRef(_enemySpawner);
        _bossRuntime = EnsureValidNodeRef(_bossRuntime);
        _itemDropRuntime = EnsureValidNodeRef(_itemDropRuntime);

        _arenaRuntime ??= FindInterfaceInScene<IArenaDebugRuntime>(sceneRoot);
        _enemySpawner ??= FindInterfaceInScene<IEnemySpawnerDebugRuntime>(sceneRoot);
        _bossRuntime ??= FindInterfaceInScene<IBossDebugRuntime>(sceneRoot);
        _itemDropRuntime ??= FindInterfaceInScene<IItemDropDebugRuntime>(sceneRoot);

        _playerProgression ??= FindInterfaceInScene<IPlayerDebugProgression>(sceneRoot);
        _playerProgression ??= GetNodeOrNull("/root/GameStateManager") as IPlayerDebugProgression;
        _augmentService ??= FindInterfaceInScene<IAugmentDebugService>(sceneRoot);
        _inventoryDebugService ??= FindInterfaceInScene<IInventoryDebugCommands>(sceneRoot);
        _statsDebugService ??= FindInterfaceInScene<IStatsDebugService>(sceneRoot);
    }

    private ArenaRunOutcome CaptureArenaRunOutcome()
    {
        Node? sceneRoot = GetTree().CurrentScene;
        if (sceneRoot is IArenaRunOutcomeProvider provider)
        {
            return provider.GetRunOutcomeForHub();
        }

        return ArenaRunOutcome.Empty;
    }

    private void ApplyArenaRunOutcome(ArenaRunOutcome outcome)
    {
        if (!outcome.HasAnyData)
        {
            return;
        }

        _playerProgression?.ApplyArenaOutcome(outcome);

        if (outcome.CoinsEarned > 0)
        {
            _currencyManager?.AddCoins(outcome.CoinsEarned);
        }

        if (outcome.LevelsEarned > 0)
        {
            _playerProgression?.AddLevels(outcome.LevelsEarned);
            _playerProgression?.SaveProgress();
        }
    }

    private static T? EnsureValidNodeRef<T>(T? value) where T : class
    {
        if (value is Node node && !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        return value;
    }

    private static T? FindInterfaceInScene<T>(Node root) where T : class
    {
        if (root is T typedRoot)
        {
            return typedRoot;
        }

        foreach (Node child in root.GetChildren())
        {
            T? found = FindInterfaceInScene<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private T? ResolveNode<T>(NodePath path, string autoloadName) where T : class
    {
        if (!path.IsEmpty)
        {
            Node? pathNode = GetNodeOrNull(path);
            if (pathNode is T typedByPath)
            {
                return typedByPath;
            }
        }

        Node? autoloadNode = GetNodeOrNull($"/root/{autoloadName}");
        return autoloadNode as T;
    }
}

// Snapshot of transferable arena progression at return-to-hub time.
public readonly struct ArenaRunOutcome
{
    public static ArenaRunOutcome Empty => new(0, 0, 0, 0);

    public ArenaRunOutcome(int coinsEarned, int levelsEarned, int kills, int highestWave)
    {
        CoinsEarned = Mathf.Max(0, coinsEarned);
        LevelsEarned = Mathf.Max(0, levelsEarned);
        Kills = Mathf.Max(0, kills);
        HighestWave = Mathf.Max(0, highestWave);
    }

    public int CoinsEarned { get; }
    public int LevelsEarned { get; }
    public int Kills { get; }
    public int HighestWave { get; }

    public bool HasProgressionDelta => CoinsEarned > 0 || LevelsEarned > 0;
    public bool HasAnyData => HasProgressionDelta || Kills > 0 || HighestWave > 0;
}

public readonly struct ArenaDebugStateSnapshot
{
    public static ArenaDebugStateSnapshot Empty => new();

    public ArenaDebugStateSnapshot(
        bool hasRuntime,
        bool overridesActive,
        ArenaDebugOverrides overrides,
        float moveSpeedPercent,
        float maxHp,
        float dodgeChancePercent,
        float lifeStealPercent,
        float healthRegenPerSecond,
        float critDamagePercent,
        float critChancePercent,
        float weaponRange,
        float weaponSizePercent,
        float weaponAttackSpeed,
        float weaponDamage,
        int weaponRicochet,
        int weaponBounceTimes,
        int weaponProjectileCount)
    {
        HasRuntime = hasRuntime;
        OverridesActive = overridesActive;
        Overrides = overrides;
        MoveSpeedPercent = moveSpeedPercent;
        MaxHp = maxHp;
        DodgeChancePercent = dodgeChancePercent;
        LifeStealPercent = lifeStealPercent;
        HealthRegenPerSecond = healthRegenPerSecond;
        CritDamagePercent = critDamagePercent;
        CritChancePercent = critChancePercent;
        WeaponRange = weaponRange;
        WeaponSizePercent = weaponSizePercent;
        WeaponAttackSpeed = weaponAttackSpeed;
        WeaponDamage = weaponDamage;
        WeaponRicochet = weaponRicochet;
        WeaponBounceTimes = weaponBounceTimes;
        WeaponProjectileCount = weaponProjectileCount;
    }

    public bool HasRuntime { get; }
    public bool OverridesActive { get; }
    public ArenaDebugOverrides Overrides { get; }
    public float MoveSpeedPercent { get; }
    public float MaxHp { get; }
    public float DodgeChancePercent { get; }
    public float LifeStealPercent { get; }
    public float HealthRegenPerSecond { get; }
    public float CritDamagePercent { get; }
    public float CritChancePercent { get; }
    public float WeaponRange { get; }
    public float WeaponSizePercent { get; }
    public float WeaponAttackSpeed { get; }
    public float WeaponDamage { get; }
    public int WeaponRicochet { get; }
    public int WeaponBounceTimes { get; }
    public int WeaponProjectileCount { get; }
}

public readonly struct ArenaDebugLoadoutSlotSnapshot
{
    public static ArenaDebugLoadoutSlotSnapshot Empty => new();

    public ArenaDebugLoadoutSlotSnapshot(
        bool hasRuntime,
        int slotIndex,
        bool hasItem,
        string slotLabel,
        string itemLabel,
        string shapeKey,
        string rarityKey,
        int level,
        int unlockedModulatorSlots,
        int availableModulatorSlots,
        Godot.Collections.Array<string> installedModulatorLabels)
    {
        HasRuntime = hasRuntime;
        SlotIndex = slotIndex;
        HasItem = hasItem;
        SlotLabel = slotLabel ?? string.Empty;
        ItemLabel = itemLabel ?? string.Empty;
        ShapeKey = shapeKey ?? string.Empty;
        RarityKey = rarityKey ?? string.Empty;
        Level = level;
        UnlockedModulatorSlots = unlockedModulatorSlots;
        AvailableModulatorSlots = availableModulatorSlots;
        InstalledModulatorLabels = installedModulatorLabels ?? new Godot.Collections.Array<string>();
    }

    public bool HasRuntime { get; }
    public int SlotIndex { get; }
    public bool HasItem { get; }
    public string SlotLabel { get; }
    public string ItemLabel { get; }
    public string ShapeKey { get; }
    public string RarityKey { get; }
    public int Level { get; }
    public int UnlockedModulatorSlots { get; }
    public int AvailableModulatorSlots { get; }
    public Godot.Collections.Array<string> InstalledModulatorLabels { get; }
}

// Implemented by arena scenes that can report progression gains back to hub services.
public interface IArenaRunOutcomeProvider
{
    ArenaRunOutcome GetRunOutcomeForHub();
}

// Global arena stat override payload from ArenaDebugPanelUI.
public struct ArenaDebugOverrides
{
    public float MoveSpeedPercent;
    public float MaxHpFlat;
    public float DodgeChancePercent;
    public float LifeStealPercent;
    public float HealthRegenPerSecond;
    public float CritDamagePercent;
    public float CritChancePercent;
    public float RangeFlat;

    public float WeaponSizePercent;
    public float WeaponAttackSpeedPercent;
    public float WeaponDamagePercentBonus;
    public float WeaponRicochetFlat;
    public float WeaponBounceTimesFlat;
    public float WeaponProjectileCountFlat;
}

// Placeholder contracts for runtime integration.
public interface IArenaDebugRuntime
{
    void ApplyLiveOverrides(ArenaDebugOverrides overrides);
    void ResetLiveOverrides();
    ArenaDebugStateSnapshot GetArenaDebugState();
    void SpawnMerchantShop();
    ArenaDebugLoadoutSlotSnapshot GetArenaLoadoutSlotSnapshot(int slotIndex);
    bool TrySetArenaDebugLoadoutItem(int slotIndex, string shapeKey, string rarityKey, int level);
    bool TryClearArenaDebugLoadoutSlot(int slotIndex);
    bool TryAddArenaDebugModulator(int slotIndex, string modulatorKey, string tierKey);
    bool TryReplaceArenaDebugModulator(int slotIndex, int modulatorIndex, string modulatorKey, string tierKey);
    bool TryRemoveArenaDebugModulator(int slotIndex, int modulatorIndex);
}

public interface IEnemySpawnerDebugRuntime
{
    void SpawnEnemy(EnemyKind kind, int count, float hpMultiplier);
    void SpawnEnemies(int count, float hpMultiplier);
    void SpawnInterceptor(int count, float hpMultiplier);
    void KillAllEnemies();
}

public interface IBossDebugRuntime
{
    void SpawnMonolith();
    void SpawnOverseer();
    void SpawnCube();
}

public interface IItemDropDebugRuntime
{
    void SpawnRandomShapeItemDrop();
    void SpawnRandomShapeItemDrop(string rarityKey);
    void SpawnEssenceDrop(ModifierKey key);
    void SpawnModulatorDrop(string key);
}

public interface IPlayerDebugProgression
{
    void AddLevels(int amount);
    void SaveProgress();
    void RebuildFromEquipment();
    void ApplyArenaOutcome(ArenaRunOutcome outcome);
}

public interface IAugmentDebugService
{
    void AddAugment(string augmentType, int level);
}

public interface IInventoryDebugCommands
{
    int AddAllShapesToInventory();
    void GrantAllItemsX1();
    void ClearAllItems();
}

public interface IStatsDebugService
{
    void SimulateRolls(int amount, bool addToInventory);
    void ResetObservedCounts();
}
