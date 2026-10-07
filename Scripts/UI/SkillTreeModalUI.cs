using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

public partial class SkillTreeModalUI : ModalBase
{
    [Export] public SkillTreeGraphView? GraphView;
    [Export] public Label? OverviewLabel;
    [Export] public RichTextLabel? BranchSummaryText;
    [Export] public Label? SelectedNodeLabel;
    [Export] public Label? StatusLabel;
    [Export] public RichTextLabel? DescriptionText;
    [Export] public NodePath GameStateManagerPath = new NodePath();

    private GameStateManager? _gameStateManager;
    private SkillTreeGraphData? _graph;

    public override void _Ready()
    {
        base._Ready();
        ResolveServices();

        if (GraphView != null)
        {
            GraphView.NodeSelected += OnNodeSelected;
        }

        BuildStructurePreview();
        RefreshOverview();
        RefreshBranchSummary();
    }

    public override void _ExitTree()
    {
        if (GraphView != null)
        {
            GraphView.NodeSelected -= OnNodeSelected;
        }

        base._ExitTree();
    }

    public override void Open()
    {
        base.Open();
        RefreshOverview();
        GraphView?.FocusCoreNode();
    }

    private void ResolveServices()
    {
        if (!GameStateManagerPath.IsEmpty)
        {
            _gameStateManager = GetNodeOrNull(GameStateManagerPath) as GameStateManager;
        }

        _gameStateManager ??= GetNodeOrNull("/root/GameStateManager") as GameStateManager;
    }

    private void BuildStructurePreview()
    {
        _graph = SkillTreeGraphFactory.CreateDefault();
        GraphView?.SetGraph(_graph);

        if (_graph != null)
        {
            OnNodeSelected(_graph.GetCoreNode());
        }
    }

    private void RefreshOverview()
    {
        if (OverviewLabel == null)
        {
            return;
        }

        int level = _gameStateManager?.PlayerLevel ?? 1;
        OverviewLabel.Text = $"Level {level}\nOne core node expands into four major branches. Each branch opens with a hub, then splits into high-impact upgrades and one outer keystone instead of tiny filler stat bumps.";
    }

    private void RefreshBranchSummary()
    {
        if (BranchSummaryText == null)
        {
            return;
        }

        BranchSummaryText.Text = string.Join("\n\n", new[]
        {
            "VANGUARD\nSurvival, tempo, and combat control.",
            "ARSENAL\nWeapon behavior, cadence, and attack identity.",
            "RESONANCE\nEssence, modulators, and synergy payoff.",
            "BOUNTY\nBoss rewards, elite spoils, and run economy.",
        });
    }

    private void OnNodeSelected(SkillTreeNodeDefinition node)
    {
        if (SelectedNodeLabel != null)
        {
            SelectedNodeLabel.Text = node.Title;
            SelectedNodeLabel.Modulate = node.AccentColor.Lightened(0.08f);
        }

        if (StatusLabel != null)
        {
            StatusLabel.Text = $"{node.BranchName} | {ResolveKindLabel(node.Kind)} | Tier {node.Tier}";
            StatusLabel.Modulate = node.AccentColor.Lightened(0.12f);
        }

        if (DescriptionText != null)
        {
            DescriptionText.Text = node.BranchSummary + "\n\n" + node.Description;
        }
    }

    private static string ResolveKindLabel(SkillTreeNodeKind kind)
    {
        return kind switch
        {
            SkillTreeNodeKind.Core => "Origin Node",
            SkillTreeNodeKind.BranchHub => "Branch Hub",
            SkillTreeNodeKind.Keystone => "Keystone",
            _ => "Major Upgrade",
        };
    }
}