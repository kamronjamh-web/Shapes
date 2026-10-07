using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.UI;

public enum SkillTreeNodeKind
{
    Core = 0,
    BranchHub = 1,
    Major = 2,
    Keystone = 3,
}

public sealed class SkillTreeNodeDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public string BranchSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public SkillTreeNodeKind Kind { get; init; } = SkillTreeNodeKind.Major;
    public int Tier { get; init; }
    public Vector2 NormalizedPosition { get; init; } = new(0.5f, 0.5f);
    public Color AccentColor { get; init; } = Colors.White;
}

public readonly record struct SkillTreeEdgeDefinition(string FromId, string ToId, bool Primary = false);

public sealed class SkillTreeGraphData
{
    public SkillTreeGraphData(string coreNodeId, SkillTreeNodeDefinition[] nodes, SkillTreeEdgeDefinition[] edges)
    {
        CoreNodeId = coreNodeId;
        Nodes = nodes;
        Edges = edges;
    }

    public string CoreNodeId { get; }
    public SkillTreeNodeDefinition[] Nodes { get; }
    public SkillTreeEdgeDefinition[] Edges { get; }

    public bool TryGetNode(string? id, out SkillTreeNodeDefinition node)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (string.Equals(Nodes[i].Id, id, StringComparison.Ordinal))
                {
                    node = Nodes[i];
                    return true;
                }
            }
        }

        node = Nodes.Length > 0 ? Nodes[0] : new SkillTreeNodeDefinition();
        return false;
    }

    public SkillTreeNodeDefinition GetCoreNode()
    {
        return TryGetNode(CoreNodeId, out SkillTreeNodeDefinition node) ? node : new SkillTreeNodeDefinition();
    }
}

public static class SkillTreeGraphFactory
{
    public static SkillTreeGraphData CreateDefault()
    {
        Color coreColor = new(0.92f, 0.95f, 1f, 1f);
        Color vanguardColor = new(0.24f, 0.84f, 0.72f, 1f);
        Color arsenalColor = new(0.96f, 0.63f, 0.22f, 1f);
        Color bountyColor = new(0.98f, 0.82f, 0.29f, 1f);
        Color resonanceColor = new(0.33f, 0.72f, 1f, 1f);

        const string coreSummary = "The origin point of the tree. Every branch begins from a single progression core before specializing into its own lane.";
        const string vanguardSummary = "Survival, tempo, and combat control. These nodes change how safe and stable a run feels instead of only raising raw stats.";
        const string arsenalSummary = "Weapon behavior, cadence, and attack identity. These nodes should make shapes feel more distinct, not just numerically stronger.";
        const string bountySummary = "Boss rewards, elite spoils, and run economy. This branch improves what encounters pay out once you win them.";
        const string resonanceSummary = "Essence, modulator, and synergy power. This branch amplifies the weirdness and payoff of your modifier combinations.";

        SkillTreeNodeDefinition[] nodes =
        {
            new()
            {
                Id = "core",
                Title = "Awakening Core",
                BranchName = "Core",
                BranchSummary = coreSummary,
                Description = "The central node anchors every future progression decision. Each cardinal spoke opens a branch hub, and those hubs split outward into major upgrades and outer keystones.",
                Kind = SkillTreeNodeKind.Core,
                Tier = 0,
                NormalizedPosition = new Vector2(0.50f, 0.50f),
                AccentColor = coreColor,
            },

            new()
            {
                Id = "vanguard_hub",
                Title = "Vanguard",
                BranchName = "Vanguard",
                BranchSummary = vanguardSummary,
                Description = "The northern branch stabilizes runs through stronger protection, tempo resets, and clutch recovery windows.",
                Kind = SkillTreeNodeKind.BranchHub,
                Tier = 1,
                NormalizedPosition = new Vector2(0.50f, 0.26f),
                AccentColor = vanguardColor,
            },
            new()
            {
                Id = "vanguard_step",
                Title = "Bulwark Step",
                BranchName = "Vanguard",
                BranchSummary = vanguardSummary,
                Description = "Movement creates protection. Dodges or reposition windows gain a defensive payoff instead of being purely evasive.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.38f, 0.10f),
                AccentColor = vanguardColor,
            },
            new()
            {
                Id = "vanguard_keystone",
                Title = "Final Stand",
                BranchName = "Vanguard",
                BranchSummary = vanguardSummary,
                Description = "Keystone. The first lethal mistake converts into a comeback window so difficult runs hinge on recovery, not instant collapse.",
                Kind = SkillTreeNodeKind.Keystone,
                Tier = 3,
                NormalizedPosition = new Vector2(0.50f, 0.05f),
                AccentColor = vanguardColor,
            },
            new()
            {
                Id = "vanguard_surge",
                Title = "Kill Surge",
                BranchName = "Vanguard",
                BranchSummary = vanguardSummary,
                Description = "Kills return tempo through shield pulses, movement bursts, or quick sustain so momentum feels earned and tangible.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.62f, 0.10f),
                AccentColor = vanguardColor,
            },

            new()
            {
                Id = "arsenal_hub",
                Title = "Arsenal",
                BranchName = "Arsenal",
                BranchSummary = arsenalSummary,
                Description = "The eastern branch reshapes how your weapons behave by reinforcing patterns, cadence, coverage, and signature attack identity.",
                Kind = SkillTreeNodeKind.BranchHub,
                Tier = 1,
                NormalizedPosition = new Vector2(0.74f, 0.50f),
                AccentColor = arsenalColor,
            },
            new()
            {
                Id = "arsenal_orbit",
                Title = "Orbit Lattice",
                BranchName = "Arsenal",
                BranchSummary = arsenalSummary,
                Description = "Persistent or circling weapons occupy space better, making defensive or area-control shapes feel deliberately constructed.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.90f, 0.38f),
                AccentColor = arsenalColor,
            },
            new()
            {
                Id = "arsenal_keystone",
                Title = "Overclock Core",
                BranchName = "Arsenal",
                BranchSummary = arsenalSummary,
                Description = "Keystone. One part of your loadout becomes the dominant carry pattern, gaining a dramatic cadence and identity spike instead of a tiny stat bump.",
                Kind = SkillTreeNodeKind.Keystone,
                Tier = 3,
                NormalizedPosition = new Vector2(0.95f, 0.50f),
                AccentColor = arsenalColor,
            },
            new()
            {
                Id = "arsenal_chain",
                Title = "Chain Release",
                BranchName = "Arsenal",
                BranchSummary = arsenalSummary,
                Description = "Projectiles, chains, or seeker patterns widen their reach so aggressive shapes feel more expressive and readable.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.90f, 0.62f),
                AccentColor = arsenalColor,
            },

            new()
            {
                Id = "bounty_hub",
                Title = "Bounty",
                BranchName = "Bounty",
                BranchSummary = bountySummary,
                Description = "The southern branch improves how bosses, elites, and end-of-run moments pay out once the encounter is already won.",
                Kind = SkillTreeNodeKind.BranchHub,
                Tier = 1,
                NormalizedPosition = new Vector2(0.50f, 0.74f),
                AccentColor = bountyColor,
            },
            new()
            {
                Id = "bounty_trophy",
                Title = "Trophy Hunter",
                BranchName = "Bounty",
                BranchSummary = bountySummary,
                Description = "Bosses drop one more structured reward packet, making major encounters feel more generous without flooding the run with trash.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.38f, 0.90f),
                AccentColor = bountyColor,
            },
            new()
            {
                Id = "bounty_keystone",
                Title = "Choice Cache",
                BranchName = "Bounty",
                BranchSummary = bountySummary,
                Description = "Keystone. Major encounters offer curated reward choices so progression feels intentional instead of purely random.",
                Kind = SkillTreeNodeKind.Keystone,
                Tier = 3,
                NormalizedPosition = new Vector2(0.50f, 0.95f),
                AccentColor = bountyColor,
            },
            new()
            {
                Id = "bounty_salvage",
                Title = "Salvage Loop",
                BranchName = "Bounty",
                BranchSummary = bountySummary,
                Description = "Unused reward drops feed back into forge value or shards, turning disappointing rewards into future momentum.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.62f, 0.90f),
                AccentColor = bountyColor,
            },

            new()
            {
                Id = "resonance_hub",
                Title = "Resonance",
                BranchName = "Resonance",
                BranchSummary = resonanceSummary,
                Description = "The western branch pushes essence, modulators, and synergies toward stronger identity and more expressive payoffs.",
                Kind = SkillTreeNodeKind.BranchHub,
                Tier = 1,
                NormalizedPosition = new Vector2(0.26f, 0.50f),
                AccentColor = resonanceColor,
            },
            new()
            {
                Id = "resonance_static",
                Title = "Static Bloom",
                BranchName = "Resonance",
                BranchSummary = resonanceSummary,
                Description = "Applied status effects spread or persist longer, making modifier-heavy builds feel more contagious and dramatic.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.10f, 0.38f),
                AccentColor = resonanceColor,
            },
            new()
            {
                Id = "resonance_keystone",
                Title = "Wildcard Circuit",
                BranchName = "Resonance",
                BranchSummary = resonanceSummary,
                Description = "Keystone. Mixed modifier paths count toward hybrid payoffs faster, rewarding weird builds instead of punishing them.",
                Kind = SkillTreeNodeKind.Keystone,
                Tier = 3,
                NormalizedPosition = new Vector2(0.05f, 0.50f),
                AccentColor = resonanceColor,
            },
            new()
            {
                Id = "resonance_well",
                Title = "Essence Well",
                BranchName = "Resonance",
                BranchSummary = resonanceSummary,
                Description = "Essence and modulator installs gain a second structural benefit so the branch changes build rules instead of only padding values.",
                Kind = SkillTreeNodeKind.Major,
                Tier = 2,
                NormalizedPosition = new Vector2(0.10f, 0.62f),
                AccentColor = resonanceColor,
            },
        };

        SkillTreeEdgeDefinition[] edges =
        {
            new("core", "vanguard_hub", true),
            new("core", "arsenal_hub", true),
            new("core", "bounty_hub", true),
            new("core", "resonance_hub", true),

            new("vanguard_hub", "vanguard_step"),
            new("vanguard_hub", "vanguard_keystone"),
            new("vanguard_hub", "vanguard_surge"),

            new("arsenal_hub", "arsenal_orbit"),
            new("arsenal_hub", "arsenal_keystone"),
            new("arsenal_hub", "arsenal_chain"),

            new("bounty_hub", "bounty_trophy"),
            new("bounty_hub", "bounty_keystone"),
            new("bounty_hub", "bounty_salvage"),

            new("resonance_hub", "resonance_static"),
            new("resonance_hub", "resonance_keystone"),
            new("resonance_hub", "resonance_well"),
        };

        return new SkillTreeGraphData("core", nodes, edges);
    }
}

public partial class SkillTreeGraphView : Control
{
    [Export(PropertyHint.Range, "16,160,1")] public float GraphPadding = 72f;
    [Export(PropertyHint.Range, "1,12,0.1")] public float TraceWidth = 4f;
    private const float DragPanThreshold = 8f;

    private readonly Dictionary<string, Button> _buttonsById = new(StringComparer.Ordinal);
    private SkillTreeGraphData? _graph;
    private string _selectedNodeId = string.Empty;
    private ScrollContainer? _scrollContainer;
    private bool _trackingPan;
    private bool _isPanning;
    private Vector2 _panStartMousePosition;
    private Vector2 _panStartScroll;

    public event Action<SkillTreeNodeDefinition>? NodeSelected;

    public SkillTreeNodeDefinition? SelectedNode
    {
        get
        {
            if (_graph == null)
            {
                return null;
            }

            return _graph.TryGetNode(_selectedNodeId, out SkillTreeNodeDefinition node)
                ? node
                : _graph.GetCoreNode();
        }
    }

    public override void _Ready()
    {
        _scrollContainer = GetParent() as ScrollContainer;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Input(InputEvent @event)
    {
        ScrollContainer? scrollContainer = ResolveScrollContainer();
        if (scrollContainer == null)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mouseButton when mouseButton.ButtonIndex == MouseButton.Left:
                HandlePanButton(scrollContainer, mouseButton);
                break;
            case InputEventMouseMotion mouseMotion:
                HandlePanMotion(scrollContainer, mouseMotion);
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            LayoutButtons();
            QueueRedraw();
        }
    }

    public void SetGraph(SkillTreeGraphData graph)
    {
        _graph = graph;
        _selectedNodeId = graph.CoreNodeId;
        RebuildButtons();
        LayoutButtons();
        UpdateButtonStyles();
        QueueRedraw();
        EmitSelectedNode();
    }

    public void FocusCoreNode()
    {
        if (_graph == null)
        {
            return;
        }

        FocusNode(_graph.CoreNodeId);
    }

    public void FocusNode(string nodeId)
    {
        if (_graph == null || !_graph.TryGetNode(nodeId, out _))
        {
            return;
        }

        _selectedNodeId = nodeId;
        UpdateButtonStyles();
        QueueRedraw();
        EmitSelectedNode();
    }

    public override void _Draw()
    {
        Rect2 graphRect = ResolveGraphRect();
        DrawRect(graphRect, new Color(0.025f, 0.044f, 0.076f, 0.86f));
        DrawRect(graphRect, new Color(0.19f, 0.29f, 0.40f, 0.24f), false, 2f);

        Vector2 graphCenter = ResolveGraphPoint(new Vector2(0.5f, 0.5f));
        Color axisColor = new Color(0.33f, 0.46f, 0.60f, 0.18f);
        DrawLine(new Vector2(graphCenter.X, graphRect.Position.Y + 14f), new Vector2(graphCenter.X, graphRect.End.Y - 14f), axisColor, 2f, true);
        DrawLine(new Vector2(graphRect.Position.X + 14f, graphCenter.Y), new Vector2(graphRect.End.X - 14f, graphCenter.Y), axisColor, 2f, true);

        if (_graph == null)
        {
            return;
        }

        foreach (SkillTreeEdgeDefinition edge in _graph.Edges)
        {
            if (!_graph.TryGetNode(edge.FromId, out SkillTreeNodeDefinition fromNode)
                || !_graph.TryGetNode(edge.ToId, out SkillTreeNodeDefinition toNode))
            {
                continue;
            }

            bool selectedEdge = string.Equals(edge.FromId, _selectedNodeId, StringComparison.Ordinal)
                || string.Equals(edge.ToId, _selectedNodeId, StringComparison.Ordinal);

            Color traceColor = toNode.AccentColor.Lerp(Colors.White, edge.Primary ? 0.18f : 0.06f);
            traceColor.A = selectedEdge ? 0.90f : edge.Primary ? 0.66f : 0.46f;
            float width = selectedEdge ? TraceWidth + 1.6f : edge.Primary ? TraceWidth : Mathf.Max(1.8f, TraceWidth - 1f);

            DrawTrace(ResolveNodeCenter(edge.FromId), ResolveNodeCenter(edge.ToId), traceColor, width);
        }

        foreach (SkillTreeNodeDefinition node in _graph.Nodes)
        {
            DrawNode(node);
        }
    }

    private void RebuildButtons()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _buttonsById.Clear();

        if (_graph == null)
        {
            return;
        }

        foreach (SkillTreeNodeDefinition node in _graph.Nodes)
        {
            Button button = new()
            {
                Name = node.Id,
                Text = node.Title,
                TooltipText = node.Description,
                Flat = true,
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                VerticalIconAlignment = VerticalAlignment.Center,
            };

            button.SetAnchorsPreset(LayoutPreset.TopLeft);
            button.Pressed += () => FocusNode(node.Id);
            AddChild(button);
            _buttonsById[node.Id] = button;
        }
    }

    private void LayoutButtons()
    {
        if (_graph == null)
        {
            return;
        }

        foreach (SkillTreeNodeDefinition node in _graph.Nodes)
        {
            if (!_buttonsById.TryGetValue(node.Id, out Button? button))
            {
                continue;
            }

            Vector2 nodeSize = ResolveNodeSize(node);
            Vector2 center = ResolveGraphPoint(node.NormalizedPosition);

            button.Size = nodeSize;
            button.Position = new Vector2(
                Mathf.Round(center.X - nodeSize.X * 0.5f),
                Mathf.Round(center.Y - nodeSize.Y * 0.5f));
        }
    }

    private void UpdateButtonStyles()
    {
        if (_graph == null)
        {
            return;
        }

        foreach (SkillTreeNodeDefinition node in _graph.Nodes)
        {
            if (!_buttonsById.TryGetValue(node.Id, out Button? button))
            {
                continue;
            }

            bool selected = string.Equals(node.Id, _selectedNodeId, StringComparison.Ordinal);
            button.AddThemeStyleboxOverride("normal", CreateNodeStyle(node, selected, false));
            button.AddThemeStyleboxOverride("hover", CreateNodeStyle(node, selected, true));
            button.AddThemeStyleboxOverride("pressed", CreateNodeStyle(node, true, true));
            button.AddThemeStyleboxOverride("focus", CreateNodeStyle(node, true, true));
            button.AddThemeColorOverride("font_color", Colors.White);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
            button.AddThemeColorOverride("font_focus_color", Colors.White);
            button.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
            button.AddThemeConstantOverride("outline_size", 1);
            button.AddThemeFontSizeOverride("font_size", ResolveFontSize(node));
        }
    }

    private void EmitSelectedNode()
    {
        SkillTreeNodeDefinition? node = SelectedNode;
        if (node == null)
        {
            return;
        }

        NodeSelected?.Invoke(node);
    }

    private void DrawNode(SkillTreeNodeDefinition node)
    {
        bool selected = string.Equals(node.Id, _selectedNodeId, StringComparison.Ordinal);
        bool hovered = _buttonsById.TryGetValue(node.Id, out Button? button) && button.IsHovered();
        DrawStyleBox(CreateNodeStyle(node, selected, hovered), ResolveNodeRect(node));
    }

    private ScrollContainer? ResolveScrollContainer()
    {
        _scrollContainer ??= GetParent() as ScrollContainer;
        return _scrollContainer;
    }

    private void HandlePanButton(ScrollContainer scrollContainer, InputEventMouseButton mouseButton)
    {
        if (mouseButton.Pressed)
        {
            if (!scrollContainer.GetGlobalRect().HasPoint(GetGlobalMousePosition()))
            {
                return;
            }

            _trackingPan = true;
            _isPanning = false;
            _panStartMousePosition = GetGlobalMousePosition();
            _panStartScroll = new Vector2(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical);
            return;
        }

        if (!_trackingPan && !_isPanning)
        {
            return;
        }

        bool wasPanning = _isPanning;
        _trackingPan = false;
        _isPanning = false;

        if (wasPanning)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandlePanMotion(ScrollContainer scrollContainer, InputEventMouseMotion mouseMotion)
    {
        if (!_trackingPan)
        {
            return;
        }

        Vector2 delta = GetGlobalMousePosition() - _panStartMousePosition;
        if (!_isPanning)
        {
            if (delta.LengthSquared() < DragPanThreshold * DragPanThreshold)
            {
                return;
            }

            _isPanning = true;
        }

        scrollContainer.ScrollHorizontal = Mathf.Max(0, Mathf.RoundToInt(_panStartScroll.X - delta.X));
        scrollContainer.ScrollVertical = Mathf.Max(0, Mathf.RoundToInt(_panStartScroll.Y - delta.Y));
        GetViewport().SetInputAsHandled();
    }

    private Rect2 ResolveGraphRect()
    {
        float innerWidth = Mathf.Max(1f, Size.X - GraphPadding * 2f);
        float innerHeight = Mathf.Max(1f, Size.Y - GraphPadding * 2f);
        return new Rect2(GraphPadding, GraphPadding, innerWidth, innerHeight);
    }

    private Rect2 ResolveNodeRect(SkillTreeNodeDefinition node)
    {
        Vector2 nodeSize = ResolveNodeSize(node);
        Vector2 center = ResolveNodeCenter(node.Id);
        return new Rect2(center - nodeSize * 0.5f, nodeSize);
    }

    private Vector2 ResolveGraphPoint(Vector2 normalizedPosition)
    {
        Rect2 graphRect = ResolveGraphRect();
        return new Vector2(
            graphRect.Position.X + graphRect.Size.X * normalizedPosition.X,
            graphRect.Position.Y + graphRect.Size.Y * normalizedPosition.Y);
    }

    private Vector2 ResolveNodeCenter(string nodeId)
    {
        if (_buttonsById.TryGetValue(nodeId, out Button? button))
        {
            return button.Position + button.Size * 0.5f;
        }

        if (_graph != null && _graph.TryGetNode(nodeId, out SkillTreeNodeDefinition node))
        {
            return ResolveGraphPoint(node.NormalizedPosition);
        }

        return ResolveGraphPoint(new Vector2(0.5f, 0.5f));
    }

    private static Vector2 ResolveNodeSize(SkillTreeNodeDefinition node)
    {
        return node.Kind switch
        {
            SkillTreeNodeKind.Core => new Vector2(156f, 108f),
            SkillTreeNodeKind.BranchHub => new Vector2(152f, 76f),
            SkillTreeNodeKind.Keystone => new Vector2(136f, 72f),
            _ => new Vector2(124f, 64f),
        };
    }

    private static int ResolveFontSize(SkillTreeNodeDefinition node)
    {
        return node.Kind switch
        {
            SkillTreeNodeKind.Core => 17,
            SkillTreeNodeKind.BranchHub => 15,
            SkillTreeNodeKind.Keystone => 14,
            _ => 13,
        };
    }

    private static StyleBoxFlat CreateNodeStyle(SkillTreeNodeDefinition node, bool selected, bool hovered)
    {
        Color accent = node.AccentColor;
        Color background = node.Kind switch
        {
            SkillTreeNodeKind.Core => new Color(0.11f, 0.15f, 0.21f, 0.98f),
            SkillTreeNodeKind.BranchHub => new Color(0.05f + accent.R * 0.12f, 0.06f + accent.G * 0.11f, 0.08f + accent.B * 0.10f, 0.95f),
            SkillTreeNodeKind.Keystone => new Color(0.06f + accent.R * 0.16f, 0.06f + accent.G * 0.14f, 0.08f + accent.B * 0.12f, 0.96f),
            _ => new Color(0.045f + accent.R * 0.08f, 0.055f + accent.G * 0.07f, 0.08f + accent.B * 0.07f, 0.93f),
        };

        if (selected)
        {
            background = background.Lerp(accent, 0.18f);
        }
        else if (hovered)
        {
            background = background.Lightened(0.06f);
        }

        Color border = selected
            ? accent.Lightened(0.22f)
            : hovered ? accent.Lightened(0.10f) : accent.Darkened(0.10f);

        int borderWidth = selected ? 3 : node.Kind is SkillTreeNodeKind.BranchHub or SkillTreeNodeKind.Keystone ? 2 : 1;

        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusBottomLeft = node.Kind == SkillTreeNodeKind.Core ? 34 : 18,
            CornerRadiusBottomRight = node.Kind == SkillTreeNodeKind.Core ? 34 : 18,
            CornerRadiusTopLeft = node.Kind == SkillTreeNodeKind.Core ? 34 : 18,
            CornerRadiusTopRight = node.Kind == SkillTreeNodeKind.Core ? 34 : 18,
            ShadowColor = new Color(accent.R, accent.G, accent.B, selected ? 0.32f : 0.16f),
            ShadowSize = selected ? 14 : node.Kind == SkillTreeNodeKind.Keystone ? 12 : 8,
            ShadowOffset = Vector2.Zero,
            ContentMarginLeft = 8f,
            ContentMarginTop = 8f,
            ContentMarginRight = 8f,
            ContentMarginBottom = 8f,
        };
        return style;
    }

    private void DrawTrace(Vector2 from, Vector2 to, Color color, float width)
    {
        if (Mathf.IsEqualApprox(from.X, to.X) || Mathf.IsEqualApprox(from.Y, to.Y))
        {
            DrawLine(from, to, color, width, true);
            return;
        }

        Vector2 elbow = Mathf.Abs(to.X - from.X) >= Mathf.Abs(to.Y - from.Y)
            ? new Vector2(to.X, from.Y)
            : new Vector2(from.X, to.Y);

        DrawLine(from, elbow, color, width, true);
        DrawLine(elbow, to, color, width, true);
        DrawCircle(elbow, Mathf.Max(1.5f, width * 0.38f), color);
    }
}