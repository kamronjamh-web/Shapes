using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Manual playback of imported Blender clips while gameplay is paused.</summary>
public sealed class FpsWeaponWorkbenchAnimations
{
    private readonly AnimationPlayer? _player;
    private readonly List<(Node3D Node, Transform3D Pose)> _rest = new();
    private readonly Node3D? _core;
    private readonly Transform3D _coreRest;
    private readonly Node3D _root;
    private double _elapsed;
    private string _clip = "";
    public bool HasClips => ValidPlayer && _player!.HasAnimation("Disassemble") && _player.HasAnimation("Assemble");
    public bool IsPlaying { get; private set; }
    public double Duration { get; private set; }
    public double Progress => Duration > 0 ? Math.Clamp(_elapsed / Duration, 0, 1) : 0;
    private bool ValidPlayer => _player != null && GodotObject.IsInstanceValid(_player) && !_player.IsQueuedForDeletion();

    public FpsWeaponWorkbenchAnimations(Node3D root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        foreach (Node node in Descendants(root))
        {
            if (node is Node3D part) _rest.Add((part, part.Transform));
            if (node is Node3D core && core.Name == "ShapeSocket") _core = core;
            if (node is AnimationPlayer player && player.HasAnimation("Assemble")) _player = player;
        }
        if (_core != null) _coreRest = InRoot(_core);
        if (ValidPlayer)
        {
            if (!_player!.IsInsideTree()) _player.Autoplay = "";
            _player.Stop();
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        }
    }

    public bool PlayDisassemble() => Play("Disassemble");
    public bool PlayAssemble(bool startCollapsed = true)
    {
        if (!startCollapsed) { Reset(); return HasClips; }
        return Play("Assemble");
    }
    public bool PlaySocketSeat(int zeroBasedSlot) => zeroBasedSlot is >= 0 and < 10 && Play($"SocketSeat{zeroBasedSlot + 1:00}");
    public bool PlaySocketRemove(int zeroBasedSlot) => zeroBasedSlot is >= 0 and < 10 && Play($"SocketRemove{zeroBasedSlot + 1:00}");

    private bool Play(string clip)
    {
        if (!ValidPlayer || !_player!.HasAnimation(clip)) return false;
        Reset();
        _clip = clip;
        Duration = _player.GetAnimation(clip).Length;
        _elapsed = 0;
        _player.SpeedScale = 1;
        _player.Play(clip);
        _player.Advance(0);
        PinCore();
        IsPlaying = Duration > 0;
        return true;
    }

    public void Advance(double delta)
    {
        if (!IsPlaying || !ValidPlayer || !double.IsFinite(delta) || delta <= 0) return;
        double step = Math.Min(delta, Math.Max(0, Duration - _elapsed));
        _player!.Advance(step);
        PinCore();
        _elapsed += step;
        if (_elapsed + .000001 < Duration) return;
        IsPlaying = false;
        // Disassembled and removed poses remain visible until the caller replaces the model.
        if (_clip == "Assemble" || _clip.StartsWith("SocketSeat", StringComparison.Ordinal)) RestoreTransforms();
    }

    public void Reset()
    {
        if (ValidPlayer) _player!.Stop();
        RestoreTransforms();
        IsPlaying = false;
        _elapsed = Duration = 0;
        _clip = "";
    }

    private void RestoreTransforms()
    {
        foreach ((Node3D node, Transform3D pose) in _rest)
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) node.Transform = pose;
    }

    private void PinCore()
    {
        if (_clip is not ("Disassemble" or "Assemble") || _core == null || !GodotObject.IsInstanceValid(_core)) return;
        Transform3D parent = _core.GetParent() is Node3D node ? InRoot(node) : Transform3D.Identity;
        _core.Transform = parent.AffineInverse() * _coreRest;
    }

    private Transform3D InRoot(Node3D node)
    {
        Transform3D pose = Transform3D.Identity;
        for (Node? current = node; current != null && current != _root; current = current.GetParent())
            if (current is Node3D part) pose = part.Transform * pose;
        return pose;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }
}
