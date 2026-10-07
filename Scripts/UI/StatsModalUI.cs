using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Pulls rarity/shape/essence counters + recent history and renders distribution percentages.
public partial class StatsModalUI : ModalBase
{
    private static readonly string[] RarityOrder =
    {
        "common",
        "uncommon",
        "rare",
        "legendary",
        "mythic",
        "divine",
    };

    private static readonly string[] ShapeOrder =
    {
        "square",
        "circle",
        "triangle",
        "hexagon",
        "sphere",
        "cube",
        "pyramid",
        "cone",
        "tesseract",
        "hyperphere",
        "pentachoron",
        "hypercone",
        "impossiblecube",
        "mobius",
        "klein",
        "boys",
    };

    private static readonly string[] ModifierOrder =
    {
        "normal",
        "shiny",
        "electric",
        "dark",
        "unstable",
        "shielded",
        "prismatic",
        "cursed",
        "thorned",
        "parasite",
    };

    [Export] public RichTextLabel? RarityDistributionText;
    [Export] public RichTextLabel? ShapeDistributionText;
    [Export] public RichTextLabel? ModifierDistributionText;
    [Export] public RichTextLabel? RecentHistoryText;

    [Export] public Button? ClearHistoryButton;

    [Export] public NodePath StatsProviderPath = new NodePath();
    [Export] public NodePath DebugManagerPath = new NodePath();

    private IStatsProvider? _statsProvider;
    private DebugManager? _debugManager;

    public override void _Ready()
    {
        base._Ready();

        ResolveServices();

        if (ClearHistoryButton != null)
        {
            ClearHistoryButton.Pressed += OnClearHistoryPressed;
        }

        if (_debugManager != null)
        {
            _debugManager.DebugActivated += OnDebugStateChanged;
            _debugManager.DebugDeactivated += OnDebugStateChanged;
        }

        RefreshView();
    }

    public override void Open()
    {
        base.Open();
        RefreshView();
    }

    private void ResolveServices()
    {
        if (!StatsProviderPath.IsEmpty)
        {
            Node? node = GetNodeOrNull(StatsProviderPath);
            if (node is IStatsProvider providerByPath)
            {
                _statsProvider = providerByPath;
            }
        }

        _statsProvider ??= GetNodeOrNull("/root/GameStateManager") as IStatsProvider;

        if (!DebugManagerPath.IsEmpty)
        {
            _debugManager = GetNodeOrNull(DebugManagerPath) as DebugManager;
        }

        _debugManager ??= GetNodeOrNull("/root/DebugManager") as DebugManager;
    }

    private void OnDebugStateChanged()
    {
        RefreshDebugVisibility();
    }

    private void OnClearHistoryPressed()
    {
        if (_debugManager?.DebugActive != true)
        {
            return;
        }

        _statsProvider?.ClearHistory();
        RefreshView();
    }

    private void RefreshView()
    {
        StatsSnapshot snapshot = _statsProvider?.GetSnapshot() ?? new StatsSnapshot();

        if (RarityDistributionText != null)
        {
            RarityDistributionText.Text = BuildDistribution("RARITY", RarityOrder, snapshot.RarityCounts);
        }

        if (ShapeDistributionText != null)
        {
            ShapeDistributionText.Text = BuildDistribution("SHAPES", ShapeOrder, snapshot.ShapeCounts);
        }

        if (ModifierDistributionText != null)
        {
            ModifierDistributionText.Text = BuildDistribution("ESSENCES", ModifierOrder, snapshot.ModifierCounts);
        }

        if (RecentHistoryText != null)
        {
            RecentHistoryText.Text = BuildHistory(snapshot);
        }

        RefreshDebugVisibility();
    }

    private void RefreshDebugVisibility()
    {
        if (ClearHistoryButton != null)
        {
            ClearHistoryButton.Visible = _debugManager?.DebugActive == true;
        }
    }

    private static string BuildDistribution(string title, string[] keyOrder, Dictionary<string, int> counts)
    {
        int total = 0;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            total += Mathf.Max(0, pair.Value);
        }

        string output = title + "\n";
        output += "----------------------\n";

        for (int i = 0; i < keyOrder.Length; i++)
        {
            string key = keyOrder[i];
            counts.TryGetValue(key, out int value);

            float percent = total > 0 ? value / (float)total * 100f : 0f;
            output += $"{key}: {value} ({percent:0.00}%)\n";
        }

        output += $"Total: {total}";
        return output;
    }

    private static string BuildHistory(StatsSnapshot snapshot)
    {
        string output = "RUN SUMMARY\n----------------------\n";
        output += $"Last Run: +{snapshot.LastArenaCoinsEarned} coins, +{snapshot.LastArenaLevelsEarned} levels, {snapshot.LastArenaKills} kills, wave {snapshot.LastArenaHighestWave}\n";
        output += $"Lifetime: {snapshot.LifetimeArenaRuns} runs, {snapshot.LifetimeArenaKills} kills, {snapshot.LifetimeArenaCoinsEarned} coins, best wave {snapshot.LifetimeHighestWave}\n\n";

        output += "RECENT\n----------------------\n";

        if (snapshot.RecentItems.Count == 0)
        {
            output += "No history yet.";
            return output;
        }

        int start = Mathf.Max(0, snapshot.RecentItems.Count - 20);

        for (int i = snapshot.RecentItems.Count - 1; i >= start; i--)
        {
            output += snapshot.RecentItems[i] + "\n";
        }

        return output.TrimEnd();
    }
}

public sealed class StatsSnapshot
{
    public Dictionary<string, int> RarityCounts { get; } = new();
    public Dictionary<string, int> ShapeCounts { get; } = new();
    public Dictionary<string, int> ModifierCounts { get; } = new();
    public List<string> RecentItems { get; } = new();

    public int LastArenaCoinsEarned { get; set; }
    public int LastArenaLevelsEarned { get; set; }
    public int LastArenaKills { get; set; }
    public int LastArenaHighestWave { get; set; }
    public int LifetimeArenaCoinsEarned { get; set; }
    public int LifetimeArenaKills { get; set; }
    public int LifetimeArenaRuns { get; set; }
    public int LifetimeHighestWave { get; set; }
}

// Optional contract for stats/history data providers.
public interface IStatsProvider
{
    StatsSnapshot GetSnapshot();
    void ClearHistory();
}
