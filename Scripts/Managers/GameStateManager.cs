using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild.Managers;

// Minimal game state service for hub identity, progression, and roll history stats.
public partial class GameStateManager : Node, IPlayerIdentityService, IStatsProvider, IStatsDebugService, IPlayerDebugProgression
{
    [Signal]
    public delegate void PlayerIdentityChangedEventHandler(string playerName, int avatarId);

    [Signal]
    public delegate void PlayerLevelChangedEventHandler(int level);

    [Export] public string SaveFilePath = "user://game_state.cfg";

    private const string MetaSection = "_meta";
    private const string SchemaVersionKey = "schema_version";
    private const int CurrentSchemaVersion = 3;
    private const string IdentitySection = "identity";
    private const string ProgressSection = "progress";
    private const string RarityStatsSection = "stats_rarity";
    private const string ShapeStatsSection = "stats_shape";
    private const string ModifierStatsSection = "stats_modifier";
    private const string RecentStatsSection = "stats_recent";
    private const string ArenaSection = "arena";
    private const int MaxHistory = 120;

    public string PlayerName { get; set; } = "Player";
    public int AvatarId { get; set; }
    public int PlayerLevel { get; private set; } = 1;
    public int LastArenaCoinsEarned { get; private set; }
    public int LastArenaLevelsEarned { get; private set; }
    public int LastArenaKills { get; private set; }
    public int LastArenaHighestWave { get; private set; }
    public int LifetimeArenaCoinsEarned { get; private set; }
    public int LifetimeArenaKills { get; private set; }
    public int LifetimeArenaRuns { get; private set; }
    public int LifetimeHighestWave { get; private set; }

    private readonly StatsSnapshot _stats = new();
    private readonly RandomNumberGenerator _rng = new();

    private static readonly string[] RarityKeys =
    {
        "common", "uncommon", "rare", "legendary", "mythic", "divine",
    };

    private static readonly string[] ShapeKeys =
    {
        "square", "circle", "triangle", "hexagon", "sphere", "cube", "pyramid", "cone",
        "tesseract", "hyperphere", "pentachoron", "hypercone", "impossiblecube", "mobius", "klein", "boys",
    };

    private static readonly string[] ModifierKeys =
    {
        "normal", "shiny", "electric", "dark", "unstable", "shielded", "prismatic", "cursed", "thorned", "parasite",
    };

    public override void _Ready()
    {
        _rng.Randomize();
        LoadState();
    }

    public void SaveIdentity()
    {
        SaveState();
        EmitSignal(SignalName.PlayerIdentityChanged, PlayerName, AvatarId);
    }

    public StatsSnapshot GetSnapshot()
    {
        StatsSnapshot copy = new();

        foreach (KeyValuePair<string, int> pair in _stats.RarityCounts)
        {
            copy.RarityCounts[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, int> pair in _stats.ShapeCounts)
        {
            copy.ShapeCounts[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, int> pair in _stats.ModifierCounts)
        {
            copy.ModifierCounts[pair.Key] = pair.Value;
        }

        copy.RecentItems.AddRange(_stats.RecentItems);
        copy.LastArenaCoinsEarned = LastArenaCoinsEarned;
        copy.LastArenaLevelsEarned = LastArenaLevelsEarned;
        copy.LastArenaKills = LastArenaKills;
        copy.LastArenaHighestWave = LastArenaHighestWave;
        copy.LifetimeArenaCoinsEarned = LifetimeArenaCoinsEarned;
        copy.LifetimeArenaKills = LifetimeArenaKills;
        copy.LifetimeArenaRuns = LifetimeArenaRuns;
        copy.LifetimeHighestWave = LifetimeHighestWave;
        return copy;
    }

    public void ClearHistory()
    {
        _stats.RecentItems.Clear();
        SaveState();
    }

    public void SimulateRolls(int amount, bool addToInventory)
    {
        _ = addToInventory;

        int total = Mathf.Max(1, amount);
        for (int i = 0; i < total; i++)
        {
            string rarity = RarityKeys[_rng.RandiRange(0, RarityKeys.Length - 1)];
            string shape = ShapeKeys[_rng.RandiRange(0, ShapeKeys.Length - 1)];
            string modifier = ModifierKeys[_rng.RandiRange(0, ModifierKeys.Length - 1)];

            RegisterObservedRoll(rarity, shape, modifier);
        }

        SaveState();
    }

    public void ResetObservedCounts()
    {
        _stats.RarityCounts.Clear();
        _stats.ShapeCounts.Clear();
        _stats.ModifierCounts.Clear();
        _stats.RecentItems.Clear();
        SaveState();
    }

    public void AddLevels(int amount)
    {
        PlayerLevel = Math.Max(1, PlayerLevel + Math.Max(1, amount));
        SaveState();
        EmitSignal(SignalName.PlayerLevelChanged, PlayerLevel);
    }

    public void SaveProgress()
    {
        SaveState();
    }

    public void RebuildFromEquipment()
    {
        // Reserved for future stat recomputation based on equipped items.
    }

    public void ApplyArenaOutcome(ArenaRunOutcome outcome)
    {
        if (!outcome.HasAnyData)
        {
            return;
        }

        LastArenaCoinsEarned = Mathf.Max(0, outcome.CoinsEarned);
        LastArenaLevelsEarned = Mathf.Max(0, outcome.LevelsEarned);
        LastArenaKills = Mathf.Max(0, outcome.Kills);
        LastArenaHighestWave = Mathf.Max(0, outcome.HighestWave);

        LifetimeArenaCoinsEarned += LastArenaCoinsEarned;
        LifetimeArenaKills += LastArenaKills;
        LifetimeArenaRuns += 1;
        LifetimeHighestWave = Mathf.Max(LifetimeHighestWave, LastArenaHighestWave);

        SaveState();
    }

    public void RegisterObservedRoll(string rarityKey, string shapeKey, string modifierKey)
    {
        Increment(_stats.RarityCounts, NormalizeKey(rarityKey));
        Increment(_stats.ShapeCounts, NormalizeKey(shapeKey));
        Increment(_stats.ModifierCounts, NormalizeKey(modifierKey));

        string historyLine = $"{NormalizeKey(rarityKey)} | {NormalizeKey(shapeKey)} | {NormalizeKey(modifierKey)}";
        _stats.RecentItems.Add(historyLine);

        if (_stats.RecentItems.Count > MaxHistory)
        {
            _stats.RecentItems.RemoveAt(0);
        }
    }

    private static string NormalizeKey(string key)
    {
        string normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "glacial" => "cursed",
            "unreal" => "mythic",
            "transcendent" => "divine",
            _ => normalized,
        };
    }

    private static void NormalizeRarityCounts(Dictionary<string, int> counts)
    {
        MergeCountAlias(counts, "transcendent", "divine");
        MergeCountAlias(counts, "unreal", "mythic");
    }

    private static void NormalizeModifierCounts(Dictionary<string, int> counts)
    {
        MergeCountAlias(counts, "glacial", "cursed");
    }

    private static void MergeCountAlias(Dictionary<string, int> counts, string aliasKey, string canonicalKey)
    {
        if (!counts.TryGetValue(aliasKey, out int aliasValue) || aliasValue <= 0)
        {
            return;
        }

        counts.Remove(aliasKey);
        if (!counts.TryGetValue(canonicalKey, out int existing))
        {
            existing = 0;
        }

        counts[canonicalKey] = existing + aliasValue;
    }

    private static void Increment(Dictionary<string, int> map, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!map.TryGetValue(key, out int value))
        {
            value = 0;
        }

        map[key] = value + 1;
    }

    private void LoadState()
    {
        ConfigFile cfg = new();
        if (cfg.Load(SaveFilePath) != Error.Ok)
        {
            return;
        }

        bool migrated = EnsureSchema(cfg);

        PlayerName = cfg.GetValue(IdentitySection, "player_name", PlayerName).AsString();
        AvatarId = cfg.GetValue(IdentitySection, "avatar_id", AvatarId).AsInt32();
        PlayerLevel = Mathf.Max(1, cfg.GetValue(ProgressSection, "player_level", PlayerLevel).AsInt32());

        LoadCounts(cfg, RarityStatsSection, _stats.RarityCounts);
        LoadCounts(cfg, ShapeStatsSection, _stats.ShapeCounts);
        LoadCounts(cfg, ModifierStatsSection, _stats.ModifierCounts);
        NormalizeRarityCounts(_stats.RarityCounts);
        NormalizeModifierCounts(_stats.ModifierCounts);
        LoadRecentHistory(cfg);
        LoadArenaSummary(cfg);

        if (migrated)
        {
            SaveState();
        }
    }

    private void SaveState()
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        cfg.SetValue(IdentitySection, "player_name", PlayerName);
        cfg.SetValue(IdentitySection, "avatar_id", AvatarId);
        cfg.SetValue(ProgressSection, "player_level", PlayerLevel);

        SaveCounts(cfg, RarityStatsSection, _stats.RarityCounts);
        SaveCounts(cfg, ShapeStatsSection, _stats.ShapeCounts);
        SaveCounts(cfg, ModifierStatsSection, _stats.ModifierCounts);
        SaveRecentHistory(cfg);
        SaveArenaSummary(cfg);
        cfg.EraseSection("arena_starter_mods");

        Error err = cfg.Save(SaveFilePath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Failed saving game state: {err}");
        }
    }

    private static void SaveCounts(ConfigFile cfg, string section, Dictionary<string, int> counts)
    {
        cfg.EraseSection(section);

        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0)
            {
                continue;
            }

            cfg.SetValue(section, pair.Key, pair.Value);
        }
    }

    private static void LoadCounts(ConfigFile cfg, string section, Dictionary<string, int> target)
    {
        target.Clear();

        if (!cfg.HasSection(section))
        {
            return;
        }

        foreach (string key in cfg.GetSectionKeys(section))
        {
            int value = Mathf.Max(0, cfg.GetValue(section, key, 0).AsInt32());
            if (value <= 0)
            {
                continue;
            }

            target[key] = value;
        }
    }

    private void SaveRecentHistory(ConfigFile cfg)
    {
        cfg.EraseSection(RecentStatsSection);

        int start = Mathf.Max(0, _stats.RecentItems.Count - MaxHistory);
        int index = 0;

        for (int i = start; i < _stats.RecentItems.Count; i++)
        {
            cfg.SetValue(RecentStatsSection, index.ToString(), _stats.RecentItems[i]);
            index++;
        }
    }

    private void LoadRecentHistory(ConfigFile cfg)
    {
        _stats.RecentItems.Clear();

        if (!cfg.HasSection(RecentStatsSection))
        {
            return;
        }

        List<int> orderedIndices = new();
        foreach (string key in cfg.GetSectionKeys(RecentStatsSection))
        {
            if (int.TryParse(key, out int parsed))
            {
                orderedIndices.Add(parsed);
            }
        }

        orderedIndices.Sort();

        for (int i = 0; i < orderedIndices.Count; i++)
        {
            string value = cfg.GetValue(RecentStatsSection, orderedIndices[i].ToString(), string.Empty).AsString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            _stats.RecentItems.Add(value);
        }

        if (_stats.RecentItems.Count > MaxHistory)
        {
            _stats.RecentItems.RemoveRange(0, _stats.RecentItems.Count - MaxHistory);
        }
    }

    private void SaveArenaSummary(ConfigFile cfg)
    {
        cfg.EraseSection(ArenaSection);
        cfg.SetValue(ArenaSection, "last_coins_earned", LastArenaCoinsEarned);
        cfg.SetValue(ArenaSection, "last_levels_earned", LastArenaLevelsEarned);
        cfg.SetValue(ArenaSection, "last_kills", LastArenaKills);
        cfg.SetValue(ArenaSection, "last_highest_wave", LastArenaHighestWave);
        cfg.SetValue(ArenaSection, "lifetime_coins_earned", LifetimeArenaCoinsEarned);
        cfg.SetValue(ArenaSection, "lifetime_kills", LifetimeArenaKills);
        cfg.SetValue(ArenaSection, "lifetime_runs", LifetimeArenaRuns);
        cfg.SetValue(ArenaSection, "lifetime_highest_wave", LifetimeHighestWave);
    }

    private void LoadArenaSummary(ConfigFile cfg)
    {
        LastArenaCoinsEarned = Mathf.Max(0, cfg.GetValue(ArenaSection, "last_coins_earned", 0).AsInt32());
        LastArenaLevelsEarned = Mathf.Max(0, cfg.GetValue(ArenaSection, "last_levels_earned", 0).AsInt32());
        LastArenaKills = Mathf.Max(0, cfg.GetValue(ArenaSection, "last_kills", 0).AsInt32());
        LastArenaHighestWave = Mathf.Max(0, cfg.GetValue(ArenaSection, "last_highest_wave", 0).AsInt32());
        LifetimeArenaCoinsEarned = Mathf.Max(0, cfg.GetValue(ArenaSection, "lifetime_coins_earned", 0).AsInt32());
        LifetimeArenaKills = Mathf.Max(0, cfg.GetValue(ArenaSection, "lifetime_kills", 0).AsInt32());
        LifetimeArenaRuns = Mathf.Max(0, cfg.GetValue(ArenaSection, "lifetime_runs", 0).AsInt32());
        LifetimeHighestWave = Mathf.Max(0, cfg.GetValue(ArenaSection, "lifetime_highest_wave", 0).AsInt32());
    }

    private static bool EnsureSchema(ConfigFile cfg)
    {
        int version = Mathf.Max(0, cfg.GetValue(MetaSection, SchemaVersionKey, 0).AsInt32());
        if (version == CurrentSchemaVersion)
        {
            return false;
        }

        if (version < 2)
        {
            MigrateLegacyRarityLadder(cfg);
        }

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        return true;
    }

    private static void MigrateLegacyRarityLadder(ConfigFile cfg)
    {
        MigrateLegacyRarityCounts(cfg, RarityStatsSection);
        MigrateLegacyRecentHistory(cfg);
    }

    private static void MigrateLegacyRarityCounts(ConfigFile cfg, string section)
    {
        if (!cfg.HasSection(section))
        {
            return;
        }

        Dictionary<string, int> migrated = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in cfg.GetSectionKeys(section))
        {
            int value = Mathf.Max(0, cfg.GetValue(section, key, 0).AsInt32());
            if (value <= 0)
            {
                continue;
            }

            string remappedKey = EquipmentCatalogDefaults.RemapLegacyRarityKey(key);
            if (!migrated.TryGetValue(remappedKey, out int existing))
            {
                existing = 0;
            }

            migrated[remappedKey] = existing + value;
        }

        cfg.EraseSection(section);
        foreach (KeyValuePair<string, int> pair in migrated)
        {
            cfg.SetValue(section, pair.Key, pair.Value);
        }
    }

    private static void MigrateLegacyRecentHistory(ConfigFile cfg)
    {
        if (!cfg.HasSection(RecentStatsSection))
        {
            return;
        }

        foreach (string key in cfg.GetSectionKeys(RecentStatsSection))
        {
            string value = cfg.GetValue(RecentStatsSection, key, string.Empty).AsString();
            cfg.SetValue(RecentStatsSection, key, RemapLegacyHistoryEntry(value));
        }
    }

    private static string RemapLegacyHistoryEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string[] parts = value.Split('|');
        if (parts.Length < 3)
        {
            return value;
        }

        string rarity = EquipmentCatalogDefaults.RemapLegacyRarityKey(parts[0].Trim());
        string shape = parts[1].Trim();
        string modifier = parts[2].Trim();
        return $"{rarity} | {shape} | {modifier}";
    }
}
