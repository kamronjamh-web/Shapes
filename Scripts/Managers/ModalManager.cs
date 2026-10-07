using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild.Managers;

// Centralized modal lifecycle and blocking-state tracking.
public partial class ModalManager : Node
{
    [Signal]
    public delegate void ModalOpenedEventHandler(ModalBase modal);

    [Signal]
    public delegate void ModalClosedEventHandler(ModalBase modal);

    [Signal]
    public delegate void BlockingModalStateChangedEventHandler(bool blockingOpen);

    [Signal]
    public delegate void HubInteractableChangedEventHandler(bool enabled);

    private readonly List<ModalBase> _openModals = new();
    private bool _hubInteractable = true;

    public bool HubInteractable => _hubInteractable;

    public void OpenModal(Control modalControl)
    {
        if (modalControl is not ModalBase modal)
        {
            GD.PushWarning("ModalManager.OpenModal requires ModalBase.");
            return;
        }

        if (_openModals.Contains(modal))
        {
            modal.Open();
            return;
        }

        if (modal.GetParent() == null)
        {
            Node root = GetTree().CurrentScene ?? GetTree().Root;
            root.AddChild(modal);
        }

        modal.Closed += () => HandleModalClosed(modal);

        _openModals.Add(modal);
        modal.Open();

        EmitSignal(SignalName.ModalOpened, modal);
        EmitSignal(SignalName.BlockingModalStateChanged, IsAnyBlockingModalOpen());
    }

    public void CloseModal(Control modalControl)
    {
        if (modalControl is not ModalBase modal)
        {
            return;
        }

        modal.Close();
    }

    public void CloseAllModals()
    {
        for (int i = _openModals.Count - 1; i >= 0; i--)
        {
            ModalBase modal = _openModals[i];
            modal.Close();
        }
    }

    public bool IsAnyBlockingModalOpen()
    {
        for (int i = 0; i < _openModals.Count; i++)
        {
            if (_openModals[i].IsOpen && _openModals[i].BlockingModal)
            {
                return true;
            }
        }

        return false;
    }

    public void SetHubInteractable(bool enabled)
    {
        if (_hubInteractable == enabled)
        {
            return;
        }

        _hubInteractable = enabled;
        EmitSignal(SignalName.HubInteractableChanged, enabled);
    }

    private void HandleModalClosed(ModalBase modal)
    {
        if (_openModals.Remove(modal))
        {
            EmitSignal(SignalName.ModalClosed, modal);
            EmitSignal(SignalName.BlockingModalStateChanged, IsAnyBlockingModalOpen());
        }
    }
}
