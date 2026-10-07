using Godot;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsArenaBlockSettings : Resource
{
	[Export] public string BlockName { get; set; } = "CoverBlock";
	[Export] public bool Enabled { get; set; } = true;
	[Export] public Vector3 Size { get; set; } = new Vector3(3f, 3f, 3f);
	[Export] public Vector3 Position { get; set; } = Vector3.Zero;
	[Export] public Color Color { get; set; } = new Color(0.16f, 0.18f, 0.23f, 1f);
}
