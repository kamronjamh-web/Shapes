using Godot;
using System.Linq;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsWeaponInspectionUI
{
    private Camera3D? _inspectionCamera;
    private Vector3 _worldRestPosition;
    private float _raiseProgress = 1f;
    private int _selectedSocket = -1;
    private Button _socketAction = null!;
    public bool IsWorldInspection => _inspectionCamera != null && IsInstanceValid(_inspectionCamera);

    public void BindWorldCamera(Camera3D camera)
    {
        EnsureBuilt();
        _inspectionCamera = camera;
        _gunPivot.Reparent(camera, false);
        _gunPivot.Visible = _inspectionVisible;
        _gunPivot.Rotation = new Vector3(.20f, -1.0f, -.10f);
        _gunSurface.Hide();
        _gunViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        // A short-range fill affects only the inspected model. The arena still supplies
        // its normal lighting; inspection cannot light up enemies or nearby structures.
        _gunPivot.AddChild(new OmniLight3D
        {
            Name = "InspectionFill", Position = new Vector3(0, .6f, 1f),
            LightEnergy = .65f, OmniRange = 3f, LightCullMask = 1u << 19,
            ShadowEnabled = false,
        });
        if (_gunVisual != null) ConfigureInspectionGeometry(_gunVisual);
        FrameWeapon();
    }

    private static void ConfigureInspectionGeometry(Node node)
    {
        if (node is GeometryInstance3D geometry)
        {
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            geometry.Layers = 1u << 19;
        }
        foreach (Node child in node.GetChildren()) ConfigureInspectionGeometry(child);
    }

    private void FrameWorldWeapon()
    {
        Camera3D camera = _inspectionCamera!;
        Rect2 stage = _weaponDropTarget.GetGlobalRect();
        if (stage.Size.X < 1 || stage.Size.Y < 1) return;
        const float depth = .72f;
        Vector2 anchor = stage.Position + stage.Size * new Vector2(.5f, .36f);
        _worldRestPosition = camera.ToLocal(camera.ProjectPosition(anchor, depth));
        Basis orientation = Basis.FromEuler(_gunPivot.Rotation);
        Rect2 inset = new(stage.Position + stage.Size * .08f, stage.Size * .84f);
        // Fit resting geometry in perspective, including depth. Moving assembly parts
        // never change the fit or the core's fixed screen anchor.
        float low = .001f, high = 2f;
        for (int step = 0; step < 14; step++)
        {
            float scale = (low + high) * .5f;
            bool fits = true;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = _worldRestPosition + orientation * _restBounds.GetEndpoint(corner) * scale;
                if (local.Z > -camera.Near * 2f || !inset.HasPoint(camera.UnprojectPosition(camera.ToGlobal(local))))
                { fits = false; break; }
            }
            if (fits) low = scale; else high = scale;
        }
        // Zoom is deliberately modest so large or rotated shapes stay in front of the near plane.
        _gunPivot.Scale = Vector3.One * (low / Mathf.Clamp(_previewZoom, .85f, 1.8f));
        ApplyWorldRaise();
    }

    private void AdvanceWorldPresentation(double delta)
    {
        if (!IsWorldInspection) return;
        _raiseProgress = Mathf.Min(1f, _raiseProgress + (float)delta / .28f);
        ApplyWorldRaise();
    }

    private void ApplyWorldRaise()
    {
        float t = _raiseProgress;
        float remaining = 1f - t * t * (3f - 2f * t);
        _gunPivot.Position = _worldRestPosition + new Vector3(.12f, -.28f, .08f) * remaining;
    }

    private void SelectSocket(int slot)
    {
        if (!_inspectionVisible || _motionPhase != "") return;
        if (slot >= (InspectedItem?.UnlockedModulatorSlots ?? 0))
        { SetStatus($"Socket {slot + 1} is locked. Level up this shape to unlock it.", true); return; }
        _selectedSocket = slot;
        ShowModulatorInventory(true);
        if (slot < _slots.Count && _slots[slot] is { } part) SelectModulatorDetails(part);
        SetStatus(slot < _slots.Count && _slots[slot] != null
            ? $"Socket {slot + 1}: remove the installed part to change it."
            : $"Socket {slot + 1}: choose a compatible part from inventory to install it.");
        RefreshSocketAction();
    }

    private void ChooseSocketPart(WeaponModulatorData part)
    {
        if (!CanUseInventoryActions || !_stored.Contains(part)) return;
        SelectModulatorDetails(part, storedSelection: true);
        if (_selectedSocket < 0 || InspectedItem == null || _motionPhase != "") return;
        if (_selectedSocket < _slots.Count && _slots[_selectedSocket] != null)
        { SetStatus("Remove the installed part before fitting another one.", true); return; }
        var payload = Payload(InspectedItem, part);
        if (!CanInstallPayload(payload, _selectedSocket))
        { SetStatus("This part cannot be installed in the selected socket.", true); return; }
        InstallPayload(payload, _selectedSocket);
    }

    private void RemoveSelectedSocket()
    {
        if (InspectedItem == null || _selectedSocket < 0 || _selectedSocket >= _slots.Count || _slots[_selectedSocket] is not { } part) return;
        StorePayload(Payload(InspectedItem, part, sourceSlot: _selectedSocket));
    }

    private void RefreshSocketAction()
    {
        if (_socketAction == null) return;
        bool installed = _selectedSocket >= 0 && _selectedSocket < _slots.Count && _slots[_selectedSocket] != null;
        _socketAction.Text = installed ? $"Remove part · socket {_selectedSocket + 1}"
            : _selectedSocket >= 0 ? $"Socket {_selectedSocket + 1} · empty" : "Select a socket";
        _socketAction.Disabled = !installed || !CanUseInventoryActions;
    }
}
