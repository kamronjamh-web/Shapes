using Godot;

namespace SuperGatchaRebuild.Hub;

// Optional character-only view behavior used inside hub preview scenes.
public partial class HubCharacterView : Node3D
{
    [Export] public MeshInstance3D? CharacterMesh;
    [Export] public float RotationSpeed = 0.18f;
    [Export] public float BobAmplitude = 0.06f;
    [Export] public float BobSpeed = 1.2f;

    private float _time;
    private float _baseY;

    public override void _Ready()
    {
        CharacterMesh ??= GetNodeOrNull<MeshInstance3D>("CharacterMesh");

        _baseY = Position.Y;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        RotateY(dt * RotationSpeed);

        _time += dt;
        Vector3 pos = Position;
        pos.Y = _baseY + BobAmplitude * Mathf.Sin(_time * BobSpeed);
        Position = pos;
    }
}
