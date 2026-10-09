using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>Run-owned inventory surrounding a live, directly editable weapon.</summary>
public partial class FpsWeaponInspectionUI : Control
{
    public event Action<int>? ShapeSelected;
    public event Action? EquipRequested;
    public event Action? CloseRequested;
    public event Action<int>? ShapeDropped;
    public event Action<EquipmentItem, WeaponModulatorData, int>? ModulatorDropRequested;
    public event Action<EquipmentItem, int>? ModulatorRemoveRequested;
    public Func<EquipmentItem, WeaponModulatorData, int, bool>? CanInstallPart { get; set; }
    public Func<EquipmentItem, int, bool>? CanRemovePart { get; set; }

    private static readonly Color Accent = new(.36f, .89f, .91f), Muted = new(.57f, .67f, .74f), Ink = new(.035f, .058f, .078f), Border = new(.13f, .22f, .28f);
    private IReadOnlyList<EquipmentItem> _weapons = Array.Empty<EquipmentItem>();
    private IReadOnlyList<WeaponModulatorData> _stored = Array.Empty<WeaponModulatorData>();
    private IReadOnlyList<WeaponModulatorData?> _slots = Array.Empty<WeaponModulatorData?>();
    private readonly List<FpsInventoryThumbnail> _thumbnails = new();
    private readonly List<FpsInventoryTile> _choices = new();
    private readonly List<FpsInventoryTile> _socketDock = new(), _socketTargets = new();
    private GridContainer _grid = null!;
    private VBoxContainer _left = null!, _right = null!;
    private Label _name = null!, _rarity = null!, _status = null!, _inventoryCount = null!, _socketCount = null!;
    private Label _detailName = null!, _detailMeta = null!, _mods = null!, _description = null!, _attack = null!;
    private Label _damage = null!, _damageLabel = null!, _cadence = null!, _range = null!, _crit = null!, _outputs = null!, _health = null!;
    private Button _equip = null!, _shapeTab = null!, _modTab = null!;
    private LineEdit _search = null!;
    private FpsInventoryDropTarget _storageDrop = null!;
    private WeaponModulatorData? _detailPart;
    private FpsInventoryWeaponStats? _stats;
    private int _selected = -1, _equipped = -1, _session;
    private bool _built, _inspectionVisible, _showModulators, _animateNextSelection;
    private int _pendingSeat = -1, _pendingRemove = -1;
    public bool IsTransforming => _motionPhase is "disassemble" or "assemble";
    public int DragSession => _session;
    public EquipmentItem? InspectedItem => _selected >= 0 && _selected < _weapons.Count ? _weapons[_selected] : null;

    public override void _Ready() { EnsureBuilt(); SetInspectionVisible(_inspectionVisible); }

    public void SetWeapons(IReadOnlyList<EquipmentItem> weapons, int selectedIndex, int equippedIndex)
    {
        EnsureBuilt();
        _weapons = weapons ?? Array.Empty<EquipmentItem>();
        SetSelection(selectedIndex, equippedIndex);
        RebuildInventory();
    }

    public void SetSelection(int selectedIndex, int equippedIndex)
    {
        EnsureBuilt();
        int previous = _selected;
        _selected = selectedIndex >= 0 && selectedIndex < _weapons.Count ? selectedIndex : -1;
        _equipped = equippedIndex;
        _detailPart = null;
        EquipmentItem? weapon = InspectedItem;
        _name.Text = weapon?.Shape?.DisplayName ?? "No shapes collected";
        _rarity.Text = weapon == null ? "Collect a weapon shape to begin." : $"{weapon.Rarity?.DisplayName ?? "Common"}  /  LEVEL {weapon.Level}  /  {(_selected == _equipped ? "EQUIPPED" : "PREVIEW")}";
        _rarity.AddThemeColorOverride("font_color", (weapon?.Rarity?.UiColor ?? Muted).Lerp(Colors.White, .25f));
        _equip.Disabled = weapon == null || _selected == _equipped;
        _equip.Text = _selected == _equipped ? "EQUIPPED" : "Equip this shape";
        if (weapon != null) SyncWeaponPreview(weapon, _animateNextSelection && previous != _selected);
        else ClearWeaponPreview();
        _animateNextSelection = false;
        RefreshShapeSelection();
        ShowWeaponDetails();
    }

    public void SetInventory(IReadOnlyList<WeaponModulatorData> stored, IReadOnlyList<WeaponModulatorData?> slots, FpsInventoryWeaponStats? stats)
    {
        EnsureBuilt();
        _stored = stored; _slots = slots; _stats = stats;
        UpdateStats(); UpdateSockets(); RebuildInventory();
        if (InspectedItem is { } weapon) SyncWeaponPreview(weapon, false);
        if (_detailPart != null && (_stored.Contains(_detailPart) || _slots.Contains(_detailPart))) SelectModulatorDetails(_detailPart);
        else ShowWeaponDetails();
    }

    public void SetStatus(string message, bool error = false)
    {
        EnsureBuilt();
        _status.Text = message;
        _status.AddThemeColorOverride("font_color", error ? new Color(1, .61f, .49f) : Accent);
    }

    public void SetInspectionVisible(bool visible)
    {
        EnsureBuilt();
        if (_inspectionVisible != visible) _session++;
        _inspectionVisible = visible; Visible = visible;
        ProcessMode = visible ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
        _gunViewport.RenderTargetUpdateMode = visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _gunViewport.ProcessMode = visible ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
        _draggingView = false;
        if (!visible)
        {
            if (GetViewport().GuiIsDragging()) GetViewport().GuiCancelDrag();
            FinishPreviewImmediately();
            foreach (var preview in _thumbnails) preview.Suspend();
        }
        else
        {
            SetStatus("Drag a shape onto the weapon. Drag a modulator into an empty socket.");
            Callable.From(() => { FrameWeapon(); foreach (var thumbnail in _thumbnails) thumbnail.RefreshThumbnail(); }).CallDeferred();
        }
    }

    public override void _Process(double delta)
    {
        if (!_inspectionVisible) return;
        AdvancePreview(delta);
        PositionSocketTargets();
        UpdateDragHighlights();
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true; Name = "WeaponInspection";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Stop;
        ColorRect background = new() { Color = new Color(.016f, .028f, .043f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MarginContainer margin = new(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        VBoxContainer outer = new(); margin.AddChild(outer); outer.AddThemeConstantOverride("separation", 14);
        HBoxContainer header = new(); outer.AddChild(header); header.AddThemeConstantOverride("separation", 14);
        VBoxContainer title = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; header.AddChild(title);
        title.AddChild(Text("A R S E N A L", 11, Accent)); title.AddChild(Text("Weapon workbench", 27, Colors.White));
        Label paused = Text("●  COMBAT PAUSED", 12, new Color(.60f, .89f, .70f)); paused.VerticalAlignment = VerticalAlignment.Center; header.AddChild(paused);
        Button close = MakeButton("Tab  /  Close"); close.Name = "CloseWeaponInspection"; close.Pressed += () => CloseRequested?.Invoke(); header.AddChild(close);
        HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill }; columns.AddThemeConstantOverride("separation", 14); outer.AddChild(columns);

        _left = new VBoxContainer { CustomMinimumSize = new Vector2(252, 0) }; columns.AddChild(_left); _left.AddThemeConstantOverride("separation", 10);
        HBoxContainer inventoryHeading = new(); _left.AddChild(inventoryHeading);
        inventoryHeading.AddChild(Text("INVENTORY", 13, Accent)); _inventoryCount = Text("", 12, Muted); _inventoryCount.HorizontalAlignment = HorizontalAlignment.Right; _inventoryCount.SizeFlagsHorizontal = SizeFlags.ExpandFill; inventoryHeading.AddChild(_inventoryCount);
        HBoxContainer tabs = new(); _left.AddChild(tabs);
        _shapeTab = MakeButton("Shapes"); _shapeTab.Name = "ShapeInventoryTab"; _shapeTab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tabs.AddChild(_shapeTab); _shapeTab.Pressed += () => ShowModulatorInventory(false);
        _modTab = MakeButton("Modulators"); _modTab.Name = "ModulatorInventoryTab"; _modTab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tabs.AddChild(_modTab); _modTab.Pressed += () => ShowModulatorInventory(true);
        _search = new LineEdit { Name = "InventorySearch", PlaceholderText = "Find an item…", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(0, 34) }; _left.AddChild(_search); _search.TextChanged += _ => RebuildInventory();
        PanelContainer inventoryPanel = new() { SizeFlagsVertical = SizeFlags.ExpandFill }; inventoryPanel.AddThemeStyleboxOverride("panel", CardStyle(Ink, Border, 10)); _left.AddChild(inventoryPanel);
        _storageDrop = new FpsInventoryDropTarget { Name = "InventoryDropTarget", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, AcceptPayload = CanStorePayload, CommitPayload = StorePayload }; inventoryPanel.AddChild(_storageDrop);
        ScrollContainer inventoryScroll = new() { Name = "InventoryGridScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }; _storageDrop.AddChild(inventoryScroll); inventoryScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass }; _grid.AddThemeConstantOverride("h_separation", 8); _grid.AddThemeConstantOverride("v_separation", 8); inventoryScroll.AddChild(_grid);
        _left.AddChild(Wrapped("Drag a socketed part back here to store it.", 12, Muted));

        VBoxContainer center = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; center.AddThemeConstantOverride("separation", 10); columns.AddChild(center);
        HBoxContainer weaponHeading = new(); center.AddChild(weaponHeading);
        VBoxContainer weaponTitle = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; weaponHeading.AddChild(weaponTitle);
        _name = Text("Multiweapon", 28, Colors.White); weaponTitle.AddChild(_name);
        _rarity = Text("", 12, Muted); weaponTitle.AddChild(_rarity);
        _equip = MakeButton("EQUIPPED"); _equip.Name = "EquipInspectedShape"; _equip.CustomMinimumSize = new Vector2(112, 40); _equip.Pressed += () => { _animateNextSelection = true; EquipRequested?.Invoke(); }; weaponHeading.AddChild(_equip);
        BuildWeaponPreview(center);
        HBoxContainer socketHeading = new(); center.AddChild(socketHeading);
        socketHeading.AddChild(Text("MODULATOR SOCKETS", 12, Accent)); _socketCount = Text("", 12, Muted); _socketCount.SizeFlagsHorizontal = SizeFlags.ExpandFill; _socketCount.HorizontalAlignment = HorizontalAlignment.Right; socketHeading.AddChild(_socketCount);
        HBoxContainer dock = new() { Name = "WeaponSocketDock" }; dock.AddThemeConstantOverride("separation", 5); center.AddChild(dock);
        for (int slot = 0; slot < 10; slot++)
        {
            FpsInventoryTile button = MakeSocket(slot, false); button.Name = $"SocketDock{slot}"; button.CustomMinimumSize = new Vector2(34, 50); button.SizeFlagsHorizontal = SizeFlags.ExpandFill; dock.AddChild(button); _socketDock.Add(button);
        }
        center.AddChild(Text("Drag to rotate  ·  Scroll to zoom  ·  Drop shapes on the weapon", 12, Muted));

        _right = new VBoxContainer { CustomMinimumSize = new Vector2(264, 0) }; _right.AddThemeConstantOverride("separation", 10); columns.AddChild(_right);
        _right.AddChild(Text("WEAPON STATS", 13, Accent));
        PanelContainer statPanel = new(); statPanel.AddThemeStyleboxOverride("panel", CardStyle(Ink, Border, 12)); _right.AddChild(statPanel);
        VBoxContainer statRows = new(); statRows.AddThemeConstantOverride("separation", 9); statPanel.AddChild(statRows);
        (_damageLabel, _damage) = StatRow(statRows, "HIT DAMAGE"); (_, _cadence) = StatRow(statRows, "INTERVAL"); (_, _range) = StatRow(statRows, "REACH"); (_, _crit) = StatRow(statRows, "CRIT CHANCE"); (_, _outputs) = StatRow(statRows, "EXTRA OUTPUT"); (_, _health) = StatRow(statRows, "PLAYER MAX HP");
        ScrollContainer detailScroll = new() { Name = "WeaponDetailsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _right.AddChild(detailScroll);
        VBoxContainer detail = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; detail.AddThemeConstantOverride("separation", 10); detailScroll.AddChild(detail);
        _detailName = Wrapped("SHAPE DETAILS", 17, Colors.White); detail.AddChild(_detailName);
        _detailMeta = Wrapped("", 12, Accent); _detailMeta.Name = "ModulatorDetailsMeta"; detail.AddChild(_detailMeta);
        _attack = Wrapped("", 15, Colors.White); detail.AddChild(_attack);
        _description = Wrapped("", 13, Muted); detail.AddChild(_description);
        _mods = Wrapped("", 13, new Color(.76f, .83f, .88f)); _mods.Name = "InstalledModulatorDetails"; detail.AddChild(_mods);
        Button details = MakeButton("Weapon details"); details.Pressed += ShowWeaponDetails; _right.AddChild(details);
        _status = Wrapped("", 13, Accent); _status.CustomMinimumSize = new Vector2(0, 22); outer.AddChild(_status);
        Resized += ResizeWorkbench;
        ResizeWorkbench();
    }

    private void ResizeWorkbench()
    {
        if (!_built) return;
        bool compact = Size.X < 1400;
        _left.CustomMinimumSize = new Vector2(compact ? 232 : 270, 0);
        _right.CustomMinimumSize = new Vector2(compact ? 236 : 282, 0);
        Callable.From(FrameWeapon).CallDeferred();
    }

    public void ShowModulatorInventory(bool show)
    {
        _showModulators = show; _search.Text = ""; RebuildInventory();
    }

    private void RebuildInventory()
    {
        if (!_built) return;
        foreach (Node child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        _thumbnails.Clear(); _choices.Clear();
        _shapeTab.Text = $"Shapes  {_weapons.Count}"; _modTab.Text = $"Mods  {_stored.Count}";
        _shapeTab.AddThemeStyleboxOverride("normal", CardStyle(!_showModulators ? new Color(.07f, .23f, .25f) : Ink, !_showModulators ? Accent : Border, 7));
        _modTab.AddThemeStyleboxOverride("normal", CardStyle(_showModulators ? new Color(.07f, .23f, .25f) : Ink, _showModulators ? Accent : Border, 7));
        _inventoryCount.Text = "CURRENT RUN";
        string query = _search.Text.Trim();
        if (_showModulators)
        {
            for (int i = 0; i < _stored.Count; i++)
            {
                WeaponModulatorData part = _stored[i];
                if (!Matches(part.DisplayName + " " + ModulatorQualityText.Header(part), query)) continue;
                FpsInventoryThumbnail preview = FpsInventoryThumbnail.Modulator(part);
                FpsInventoryTile tile = ItemTile(part.BaseDisplayName, $"{ModulatorQualityText.QualityName(part.Tier)} · {(int)(part.QualityStrength * 100)}%", ModulatorLoot.GetTierColor(part.PowerTier), preview);
                tile.Name = $"StoredModulator{i}"; tile.DragCaption = part.DisplayName;
                tile.TooltipText = $"{part.DisplayName}\n{ModulatorQualityText.Header(part)}\n{ModulatorQualityText.Describe(part)}";
                tile.CreatePayload = () => !_inspectionVisible || _motionPhase != "" || InspectedItem == null || !_stored.Contains(part) ? null : Payload(InspectedItem, part);
                tile.Pressed += () => SelectModulatorDetails(part);
                tile.AcceptPayload = CanStorePayload; tile.CommitPayload = StorePayload;
                _grid.AddChild(tile);
            }
        }
        else
        {
            for (int i = 0; i < _weapons.Count; i++)
            {
                int index = i; EquipmentItem item = _weapons[i];
                if (!Matches(item.Shape?.DisplayName ?? "", query)) continue;
                FpsInventoryThumbnail preview = FpsInventoryThumbnail.Shape(item);
                FpsInventoryTile tile = ItemTile(item.Shape?.DisplayName ?? "Shape", $"{item.Rarity?.DisplayName ?? "Common"} · Lv {item.Level}", item.Rarity?.UiColor ?? Colors.White, preview);
                tile.Name = $"ShapeChoice{i}"; tile.DragCaption = item.Shape?.DisplayName ?? "Shape";
                tile.TooltipText = $"{item.Shape?.DisplayName}\n{item.Rarity?.DisplayName} · Level {item.Level}\nClick to inspect. Drag onto the multiweapon to equip.";
                tile.CreatePayload = () => !_inspectionVisible || _motionPhase != "" ? null : Payload(item, null, index);
                tile.Pressed += () => ShapeSelected?.Invoke(index);
                tile.AcceptPayload = CanStorePayload; tile.CommitPayload = StorePayload;
                tile.SetMeta("arsenal_index", index); _choices.Add(tile); _grid.AddChild(tile);
            }
        }
        if (_grid.GetChildCount() == 0)
        {
            Label empty = Wrapped(string.IsNullOrEmpty(query) ? _showModulators ? "No loose modulators yet.\n\nRecover parts from enemies and completed waves, or drag an installed cartridge here." : "Your collected shapes appear here." : "No matching items.", 14, Muted);
            empty.CustomMinimumSize = new Vector2(_left.CustomMinimumSize.X - 30, 130); _grid.AddChild(empty);
        }
        RefreshShapeSelection();
    }

    private FpsInventoryTile ItemTile(string name, string subtitle, Color color, FpsInventoryThumbnail preview)
    {
        FpsInventoryTile tile = new() { CustomMinimumSize = new Vector2(96, 137), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseDefaultCursorShape = CursorShape.PointingHand, ClipContents = true };
        tile.AddThemeStyleboxOverride("normal", CardStyle(new Color(.055f, .085f, .11f), color.Darkened(.50f), 8));
        tile.AddThemeStyleboxOverride("hover", CardStyle(new Color(.08f, .15f, .18f), color, 8));
        tile.AddThemeStyleboxOverride("pressed", CardStyle(new Color(.08f, .20f, .23f), Accent, 8));
        tile.AddThemeStyleboxOverride("focus", CardStyle(Colors.Transparent, Accent, 8));
        VBoxContainer content = new() { MouseFilter = MouseFilterEnum.Ignore }; tile.AddChild(content); content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); content.OffsetLeft = content.OffsetTop = 6; content.OffsetRight = content.OffsetBottom = -6; content.AddThemeConstantOverride("separation", 3);
        content.AddChild(preview); _thumbnails.Add(preview);
        Label caption = Text(name, 13, Colors.White); caption.AutowrapMode = TextServer.AutowrapMode.WordSmart; caption.CustomMinimumSize = new Vector2(0, 34); caption.MaxLinesVisible = 2; content.AddChild(caption);
        Label meta = Text(subtitle, 10, color.Lerp(Colors.White, .35f)); meta.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; content.AddChild(meta);
        tile.Ready += () => tile.PreviewTexture = preview.ThumbnailTexture;
        return tile;
    }

    private void RefreshShapeSelection()
    {
        foreach (FpsInventoryTile tile in _choices)
        {
            int index = tile.GetMeta("arsenal_index").AsInt32();
            Color color = _weapons[index].Rarity?.UiColor ?? Muted;
            tile.AddThemeStyleboxOverride("normal", CardStyle(index == _selected ? new Color(.075f, .19f, .22f) : new Color(.055f, .085f, .11f), index == _selected ? Accent : color.Darkened(.5f), 8));
        }
    }

    private void UpdateStats()
    {
        if (_stats == null) return;
        _damageLabel.Text = _stats.DamageLabel.ToUpperInvariant(); _damage.Text = _stats.Damage.ToString("0.#");
        _cadence.Text = $"{_stats.IntervalSeconds:0.##} s"; _range.Text = $"{_stats.Range:0.#} m";
        _crit.Text = $"{_stats.CritChancePercent:0.#}%"; _outputs.Text = $"+{_stats.AverageExtraOutput:0.##}";
        _health.Text = $"{_stats.MaximumPlayerHealth:0.#}";
    }

    private void ShowWeaponDetails()
    {
        if (!_built) return;
        _detailPart = null;
        EquipmentItem? weapon = InspectedItem;
        _detailName.Text = "SHAPE DETAILS"; _detailMeta.Text = "";
        _detailMeta.Visible = false; _attack.Visible = _description.Visible = true;
        _attack.Text = weapon?.Shape?.AttackName ?? "";
        _description.Text = weapon == null ? "" : FpsModulatorPresentation.DescribeAttack(weapon);
        _mods.Text = _stats == null ? "" : $"{_stats.DamageHint}\n\nAttack rate  {_stats.AttacksPerSecond:0.##} / s\nArea  ×{_stats.AreaScale:0.##}\nDuration  ×{_stats.DurationScale:0.##}\nPierce  +{_stats.AveragePierce:0.##}\nRicochet  +{_stats.AverageRicochet:0.##}\nBounces  +{_stats.AverageBounces:0.##}\nChain targets  +{_stats.AverageChains:0.##}\n\nSelect a cartridge to inspect its effects.";
    }

    public void SelectModulatorDetails(WeaponModulatorData part)
    {
        _detailPart = part;
        _detailName.Text = part.DisplayName; _detailMeta.Text = ModulatorQualityText.Header(part);
        _detailMeta.Visible = true; _attack.Visible = _description.Visible = false;
        _detailMeta.AddThemeColorOverride("font_color", ModulatorLoot.GetTierColor(part.PowerTier));
        _attack.Text = ""; _description.Text = "";
        EquipmentItem? weapon = InspectedItem;
        _mods.Text = weapon != null && weapon.Modulators.Contains(part)
            ? FpsModulatorPresentation.DescribePart(part, weapon, _selected == _equipped, _selected, _weapons.Count > 0 ? _weapons[0] : null, includeHeading: false)
            : ModulatorQualityText.Describe(part) + "\n\n" + ModulatorQualityText.Compare(part, weapon);
        if (weapon != null && !WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out string reason)) _mods.Text += "\n\n" + reason;
    }

    private FpsInventoryDragData Payload(EquipmentItem weapon, WeaponModulatorData? part, int arsenalIndex = -1, int sourceSlot = -1)
        => new() { Owner = this, Session = _session, Weapon = weapon, Part = part, ArsenalIndex = arsenalIndex, SourceSlot = sourceSlot };
    private bool Owns(FpsInventoryDragData payload) => _inspectionVisible && _motionPhase == "" && ReferenceEquals(payload.Owner, this) && payload.Session == _session;
    private bool CanEquipPayload(FpsInventoryDragData payload) => Owns(payload) && payload.Part == null && payload.ArsenalIndex >= 0 && payload.ArsenalIndex < _weapons.Count && ReferenceEquals(_weapons[payload.ArsenalIndex], payload.Weapon);
    private void EquipPayload(FpsInventoryDragData payload)
    {
        if (!CanEquipPayload(payload)) return;
        _animateNextSelection = true; ShapeDropped?.Invoke(payload.ArsenalIndex); _animateNextSelection = false;
    }
    private bool CanInstallPayload(FpsInventoryDragData payload, int slot) => Owns(payload) && ReferenceEquals(payload.Weapon, InspectedItem) && payload.Part != null && payload.SourceSlot < 0 && _stored.Contains(payload.Part) && CanInstallPart?.Invoke(payload.Weapon, payload.Part, slot) == true;
    private void InstallPayload(FpsInventoryDragData payload, int slot)
    {
        if (!CanInstallPayload(payload, slot)) return;
        _pendingSeat = slot;
        ModulatorDropRequested?.Invoke(payload.Weapon, payload.Part!, slot);
        _pendingSeat = -1;
        if (_slots.Contains(payload.Part)) SelectModulatorDetails(payload.Part!);
    }
    private bool CanStorePayload(FpsInventoryDragData payload) => Owns(payload) && ReferenceEquals(payload.Weapon, InspectedItem) && payload.Part != null && payload.SourceSlot >= 0 && payload.SourceSlot < _slots.Count && ReferenceEquals(_slots[payload.SourceSlot], payload.Part) && CanRemovePart?.Invoke(payload.Weapon, payload.SourceSlot) == true;
    private void StorePayload(FpsInventoryDragData payload)
    {
        if (!CanStorePayload(payload)) return;
        _pendingRemove = payload.SourceSlot;
        ModulatorRemoveRequested?.Invoke(payload.Weapon, payload.SourceSlot);
        _pendingRemove = -1;
    }
    private static bool Matches(string text, string query) => string.IsNullOrEmpty(query) || text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static (Label, Label) StatRow(VBoxContainer parent, string name)
    {
        HBoxContainer row = new(); parent.AddChild(row);
        Label label = Text(name, 11, Muted); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(label);
        Label value = Text("—", 18, Colors.White); row.AddChild(value); return (label, value);
    }
    internal static Label Text(string text, int size, Color color)
    {
        Label label = new() { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color); return label;
    }
    private static Label Wrapped(string text, int size, Color color) { Label label = Text(text, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; return label; }
    private static Button MakeButton(string text)
    {
        Button button = new() { Text = text, MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 36) };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeStyleboxOverride("normal", CardStyle(Ink, Border, 8));
        button.AddThemeStyleboxOverride("hover", CardStyle(new Color(.09f, .20f, .23f), Accent, 8));
        button.AddThemeStyleboxOverride("pressed", CardStyle(new Color(.1f, .29f, .31f), Accent, 8));
        button.AddThemeStyleboxOverride("disabled", CardStyle(new Color(.035f, .055f, .07f), Border, 8));
        button.AddThemeStyleboxOverride("focus", CardStyle(Colors.Transparent, Accent, 8)); return button;
    }
    internal static StyleBoxFlat CardStyle(Color background, Color border, int padding = 8) => new()
    {
        BgColor = background, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7,
        ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding,
    };
}
