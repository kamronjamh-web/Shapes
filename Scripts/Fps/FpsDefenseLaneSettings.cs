using Godot;

namespace SuperGatchaRebuild.Fps;

public enum FpsLaneDirection { North, East, South, West }

[Tool, GlobalClass]
public partial class FpsDefenseLaneSettings : Resource
{
	private FpsLaneDirection _direction;
	private bool _enabled = true;
	private float _width = 20f;
	private float _length = 60f;
	private Color _accentColor = new(1f, 0.65f, 0.22f);
	[Export] public FpsLaneDirection Direction { get => _direction; set => Change(ref _direction, value); }
	[Export] public bool Enabled { get => _enabled; set => Change(ref _enabled, value); }
	[Export(PropertyHint.Range, "8,40,0.5")] public float Width { get => _width; set => Change(ref _width, value); }
	[Export(PropertyHint.Range, "16,160,1")] public float Length { get => _length; set => Change(ref _length, value); }
	[Export] public Color AccentColor { get => _accentColor; set => Change(ref _accentColor, value); }

	private void Change<T>(ref T field, T value)
	{
		if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		EmitChanged();
	}

	public Vector3 Outward => Direction switch
	{
		FpsLaneDirection.East => Vector3.Right,
		FpsLaneDirection.South => Vector3.Back,
		FpsLaneDirection.West => Vector3.Left,
		_ => Vector3.Forward,
	};
}
