using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsWeaponInspectionUI
{
    private SubViewport _gunViewport = null!;
    private SubViewportContainer _gunSurface = null!;
    private FpsInventoryDropTarget _weaponDropTarget = null!;
    private Camera3D _gunCamera = null!;
    private Node3D _gunPivot = null!;
    private Node3D? _gunVisual;
    private FpsWeaponWorkbenchAnimations? _workbenchAnimation;
    private EquipmentItem? _previewWeapon, _nextPreviewWeapon;
    private string _previewSignature = "", _motionPhase = "";
    private bool _draggingView;
    private float _previewZoom = 1f;
    private Aabb _restBounds = new(new Vector3(-.5f, -.5f, -.5f), Vector3.One);
    private readonly Node3D?[] _socketAnchors = new Node3D?[10];
    private readonly List<(int Slot, Vector2 Point, float Depth)> _projectedSockets = new(10);

    private void BuildWeaponPreview(VBoxContainer center)
    {
        PanelContainer panel = new() { Name = "WeaponPreviewPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(280, 260) };
        panel.AddThemeStyleboxOverride("panel", CardStyle(new Color(.025f, .045f, .061f), Border, 0));
        center.AddChild(panel);
        Control stage = new() { Name = "WeaponPreviewStage", ClipContents = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(stage);
        _gunSurface = new SubViewportContainer { Name = "HandgunPreview", Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        stage.AddChild(_gunSurface); _gunSurface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _gunViewport = new SubViewport { Name = "WeaponPreviewViewport", OwnWorld3D = true, TransparentBg = true, Size = new Vector2I(900, 650), HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        _gunSurface.AddChild(_gunViewport);
        _gunViewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.025f, .045f, .061f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.69f, .81f, .91f), AmbientLightEnergy = .7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        } });
        _gunPivot = new Node3D { Name = "WeaponPreviewPivot", Rotation = new Vector3(.34f, -.72f, 0) }; _gunViewport.AddChild(_gunPivot);
        _gunCamera = new Camera3D { Name = "WeaponPreviewCamera", Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Near = .01f, Far = 30f, Position = new Vector3(0, 0, 4), Size = 1.8f, KeepAspect = Camera3D.KeepAspectEnum.Height }; _gunViewport.AddChild(_gunCamera);
        _gunViewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -35, 0), LightEnergy = 1.7f });
        _gunViewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(22, 145, 0), LightColor = new Color(.48f, .79f, 1), LightEnergy = 1.2f });
        _weaponDropTarget = new FpsInventoryDropTarget { Name = "WeaponDropTarget", AcceptPayload = CanEquipPayload, CommitPayload = EquipPayload, MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Drag };
        stage.AddChild(_weaponDropTarget); _weaponDropTarget.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _weaponDropTarget.GuiInput += HandlePreviewInput;
        for (int slot = 0; slot < 10; slot++)
        {
            FpsInventoryTile target = MakeSocket(slot, true); target.Name = $"SocketDropTarget{slot}";
            _weaponDropTarget.AddChild(target); _socketTargets.Add(target); target.Visible = false;
        }
        stage.Resized += () => { if (IsInsideTree()) Callable.From(FrameWeapon).CallDeferred(); };
    }

    private void HandlePreviewInput(InputEvent input)
    {
        if (_motionPhase != "") { _draggingView = false; return; }
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _draggingView = button.Pressed;
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _previewZoom = Mathf.Clamp(_previewZoom * (button.ButtonIndex == MouseButton.WheelUp ? .9f : 1.1f), .55f, 1.8f);
                FrameWeapon(); _weaponDropTarget.AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion && _draggingView && (motion.ButtonMask & MouseButtonMask.Left) != 0 && !GetViewport().GuiIsDragging())
        {
            Vector3 rotation = _gunPivot.Rotation;
            rotation.Y += motion.Relative.X * .008f;
            rotation.X = Mathf.Clamp(rotation.X + motion.Relative.Y * .008f, -1.15f, 1.15f);
            _gunPivot.Rotation = rotation; FrameWeapon(); _weaponDropTarget.AcceptEvent();
        }
    }

    private static string WeaponPreviewSignature(EquipmentItem weapon)
    {
        StringBuilder text = new();
        text.Append(weapon.InstanceId).Append('|').Append(weapon.Shape?.Key).Append('|').Append(weapon.Rarity?.Key).Append('|').Append(weapon.Level);
        IReadOnlyList<WeaponModulatorData?> slots = FpsWeaponSocketLayout.GetSlots(weapon);
        foreach (WeaponModulatorData? part in slots)
            text.Append('|').Append(part == null ? "" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(part).ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(part?.Tier).Append(':').Append(part?.NormalizedKey);
        return text.ToString();
    }

    private void SyncWeaponPreview(EquipmentItem weapon, bool animate)
    {
        string signature = WeaponPreviewSignature(weapon);
        if (signature == _previewSignature && ReferenceEquals(weapon, _nextPreviewWeapon ?? _previewWeapon) && !animate) return;
        bool sameWeapon = ReferenceEquals(weapon, _previewWeapon);
        FinishPreviewImmediately();
        // Stamp before SetWeapons/SetInventory's second refresh so it cannot cancel playback.
        _previewSignature = signature;
        _nextPreviewWeapon = weapon;
        _draggingView = false;
        if (sameWeapon && _gunVisual != null && _pendingRemove >= 0 && _workbenchAnimation?.PlaySocketRemove(_pendingRemove) == true)
        {
            _motionPhase = "remove";
            return;
        }
        if (animate && _gunVisual != null && _workbenchAnimation?.PlayDisassemble() == true)
        {
            _motionPhase = "disassemble";
            return;
        }
        ReplaceWeaponPreview(weapon);
        if (sameWeapon && _pendingSeat >= 0 && _workbenchAnimation?.PlaySocketSeat(_pendingSeat) == true) _motionPhase = "seat";
        else if (animate && _workbenchAnimation?.PlayAssemble() == true) _motionPhase = "assemble";
        else _motionPhase = "";
    }

    /// <summary>Equipping an already inspected shape still plays its authored conversion sequence.</summary>
    public void AnimateCurrentEquip()
    {
        if (_inspectionVisible && _motionPhase == "" && InspectedItem is { } weapon)
            SyncWeaponPreview(weapon, true);
    }

    private void ReplaceWeaponPreview(EquipmentItem weapon)
    {
        RemovePreviewVisual();
        _gunVisual = FpsWeaponVisualFactory.Create(weapon);
        _gunVisual.Name = "InspectedWeaponModel";
        _gunPivot.AddChild(_gunVisual);
        Aabb bounds = FpsWeaponVisualFactory.GetVisualBounds(_gunVisual) ?? new Aabb(new Vector3(-.5f, -.5f, -.5f), Vector3.One);
        Node3D? core = FindPreviewNode(_gunVisual, "ShapeSocket");
        Vector3 coreCenter = core == null ? bounds.GetCenter() : _gunVisual.GlobalTransform.AffineInverse() * core.GlobalPosition;
        // Static normalization makes differently sized authored cores share one stable screen anchor.
        float scale = 1f / Mathf.Max(.001f, Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z)));
        _gunVisual.Scale = Vector3.One * scale;
        _gunVisual.Position = -coreCenter * scale;
        _restBounds = _gunVisual.Transform * bounds;
        _workbenchAnimation = new FpsWeaponWorkbenchAnimations(_gunVisual);
        for (int slot = 0; slot < 10; slot++) _socketAnchors[slot] = FindPreviewNode(_gunVisual, $"ModulatorSocket{slot + 1:00}");
        _previewWeapon = weapon; _nextPreviewWeapon = null;
        FrameWeapon();
    }

    private void RemovePreviewVisual()
    {
        _workbenchAnimation?.Reset(); _workbenchAnimation = null;
        if (_gunVisual != null && GodotObject.IsInstanceValid(_gunVisual))
        {
            _gunPivot.RemoveChild(_gunVisual); _gunVisual.QueueFree();
        }
        _gunVisual = null;
        Array.Clear(_socketAnchors);
    }

    private void ClearWeaponPreview()
    {
        RemovePreviewVisual(); _previewWeapon = _nextPreviewWeapon = null;
        _previewSignature = _motionPhase = "";
        foreach (FpsInventoryTile target in _socketTargets) target.Visible = false;
    }

    private void FinishPreviewImmediately()
    {
        EquipmentItem? target = _nextPreviewWeapon;
        _workbenchAnimation?.Reset(); _motionPhase = "";
        if (target != null) ReplaceWeaponPreview(target);
        _nextPreviewWeapon = null;
    }

    private void AdvancePreview(double delta)
    {
        if (_motionPhase == "" || _workbenchAnimation == null) return;
        _workbenchAnimation.Advance(delta);
        if (_workbenchAnimation.IsPlaying) return;
        string completed = _motionPhase;
        _motionPhase = "";
        if (completed is "disassemble" or "remove")
        {
            EquipmentItem? next = _nextPreviewWeapon ?? InspectedItem;
            if (next == null) { ClearWeaponPreview(); return; }
            ReplaceWeaponPreview(next);
            if (completed == "disassemble" && _workbenchAnimation?.PlayAssemble() == true) _motionPhase = "assemble";
        }
        UpdateSockets();
    }

    private void FrameWeapon()
    {
        if (_gunSurface == null || _gunSurface.Size.X < 1 || _gunSurface.Size.Y < 1) return;
        // Rest bounds are sampled once per replacement. Never fit moving Blender parts each frame.
        if (_motionPhase != "") return;
        Aabb oriented = _gunPivot.Transform * _restBounds;
        Vector3 low = oriented.Position, high = oriented.End;
        float halfWidth = Mathf.Max(Mathf.Abs(low.X), Mathf.Abs(high.X));
        float aspect = _gunSurface.Size.X / _gunSurface.Size.Y;
        // Keep the core at 36% of viewport height. Most gun bodies extend below it;
        // fitting symmetric extents around that high anchor wasted half the stage.
        float verticalFit = Mathf.Max(oriented.Size.Y / .60f,
            Mathf.Max(Mathf.Max(0f, high.Y) / .28f, Mathf.Max(0f, -low.Y) / .56f));
        float horizontalFit = halfWidth * 2f / (aspect * .78f);
        _gunCamera.Size = Mathf.Max(.45f, Mathf.Max(verticalFit, horizontalFit)) * _previewZoom;
        _gunCamera.VOffset = -_gunCamera.Size * .14f;
    }

    private FpsInventoryTile MakeSocket(int slot, bool projected)
    {
        FpsInventoryTile tile = new() { CustomMinimumSize = new Vector2(projected ? 30 : 50, projected ? 30 : 50), MouseDefaultCursorShape = CursorShape.PointingHand, MouseFilter = MouseFilterEnum.Stop };
        tile.AddThemeFontSizeOverride("font_size", projected ? 11 : 12);
        tile.AcceptPayload = payload => (projected && CanEquipPayload(payload)) || CanInstallPayload(payload, slot);
        tile.CommitPayload = payload =>
        {
            if (projected && CanEquipPayload(payload)) EquipPayload(payload);
            else InstallPayload(payload, slot);
        };
        tile.CreatePayload = () =>
        {
            EquipmentItem? weapon = InspectedItem;
            WeaponModulatorData? part = slot < _slots.Count ? _slots[slot] : null;
            return weapon != null && part != null && _inspectionVisible && _motionPhase == "" && CanRemovePart?.Invoke(weapon, slot) == true
                ? Payload(weapon, part, sourceSlot: slot) : null;
        };
        tile.Pressed += () => { if (slot < _slots.Count && _slots[slot] is { } part) SelectModulatorDetails(part); };
        return tile;
    }

    private void UpdateSockets()
    {
        int unlocked = InspectedItem?.UnlockedModulatorSlots ?? 0;
        int installed = 0;
        for (int slot = 0; slot < 10; slot++)
        {
            WeaponModulatorData? part = slot < _slots.Count ? _slots[slot] : null;
            if (part != null) installed++;
            bool open = slot < unlocked;
            string caption = !open ? $"×\n{slot + 1}" : part == null ? $"+\n{slot + 1}" : $"◆\n{slot + 1}";
            string tooltip = !open ? $"Socket {slot + 1}: locked. Level up this shape to unlock more sockets."
                : part == null ? $"Socket {slot + 1}: empty. Drop a compatible recovered part here."
                : $"Socket {slot + 1}: {part.DisplayName}\n{ModulatorQualityText.Header(part)}\nDrag back to inventory to remove.";
            for (int view = 0; view < 2; view++)
            {
                List<FpsInventoryTile> tiles = view == 0 ? _socketDock : _socketTargets;
                FpsInventoryTile? tile = tiles.Count > slot ? tiles[slot] : null;
                if (tile == null) continue;
                tile.Text = caption; tile.TooltipText = tooltip; tile.DragCaption = part?.DisplayName ?? "Socket";
                tile.SetMeta("slot_index", slot); tile.SetMeta("socket_state", !open ? "locked" : part == null ? "empty" : "installed");
                tile.AddThemeColorOverride("font_color", !open ? Muted.Darkened(.3f) : part?.UiColor ?? Muted);
                tile.AddThemeStyleboxOverride("normal", CardStyle(Ink, !open ? Border.Darkened(.3f) : part?.UiColor.Darkened(.35f) ?? Border, 2));
                tile.AddThemeStyleboxOverride("hover", CardStyle(new Color(.065f, .15f, .18f), open ? Accent : Border, 2));
                tile.AddThemeStyleboxOverride("focus", CardStyle(Colors.Transparent, Accent, 2));
            }
        }
        if (_socketCount != null) _socketCount.Text = $"{installed} / {unlocked} installed";
    }

    private void PositionSocketTargets()
    {
        if (_gunVisual == null || !_gunCamera.IsInsideTree() || _gunSurface.Size.X < 1 || _gunSurface.Size.Y < 1) return;
        _projectedSockets.Clear();
        for (int slot = 0; slot < 10; slot++)
        {
            _socketTargets[slot].Visible = false;
            Node3D? anchor = _socketAnchors[slot];
            if (_motionPhase != "" || anchor == null || !GodotObject.IsInstanceValid(anchor) || _gunCamera.IsPositionBehind(anchor.GlobalPosition)) continue;
            Vector2 point = _gunCamera.UnprojectPosition(anchor.GlobalPosition);
            point *= new Vector2(_gunSurface.Size.X / _gunViewport.Size.X, _gunSurface.Size.Y / _gunViewport.Size.Y);
            if (point.X < 16 || point.Y < 16 || point.X > _gunSurface.Size.X - 16 || point.Y > _gunSurface.Size.Y - 16) continue;
            _projectedSockets.Add((slot, point, _gunCamera.GlobalPosition.DistanceSquaredTo(anchor.GlobalPosition)));
        }
        _projectedSockets.Sort((left, right) => left.Depth.CompareTo(right.Depth));
        for (int index = 0; index < _projectedSockets.Count; index++)
        {
            var socket = _projectedSockets[index];
            bool overlap = false;
            for (int nearer = 0; nearer < index; nearer++)
                if (_socketTargets[_projectedSockets[nearer].Slot].Visible && socket.Point.DistanceSquaredTo(_projectedSockets[nearer].Point) < 32f * 32f) { overlap = true; break; }
            if (overlap) continue;
            FpsInventoryTile tile = _socketTargets[socket.Slot];
            tile.Size = new Vector2(30, 30); tile.Position = socket.Point - tile.Size * .5f; tile.Visible = true;
        }
    }

    private void UpdateDragHighlights()
    {
        FpsInventoryDragData? payload = GetViewport().GuiIsDragging() ? FpsInventoryTile.Read(GetViewport().GuiGetDragData()) : null;
        for (int slot = 0; slot < 10; slot++)
        {
            bool valid = payload != null && CanInstallPayload(payload, slot);
            Color tint = valid ? new Color(.55f, 1f, .77f) : payload?.Part != null ? new Color(.7f, .7f, .7f) : Colors.White;
            _socketDock[slot].Modulate = tint;
            _socketTargets[slot].Modulate = tint;
        }
    }

    private static Node3D? FindPreviewNode(Node root, string name)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Node3D spatial && child.Name == name) return spatial;
            if (FindPreviewNode(child, name) is { } found) return found;
        }
        return null;
    }
}
