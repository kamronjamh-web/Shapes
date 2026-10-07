using Godot;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsEnemySpawnSettings : Resource
{
	[Export] public string SpawnName { get; set; } = "Enemy";
	[Export] public bool Enabled { get; set; } = true;
	[Export] public FpsEnemyRole Role { get; set; } = FpsEnemyRole.Bruiser;
	[Export] public Vector3 Position { get; set; } = Vector3.Zero;
	[Export(PropertyHint.Range, "1,5000,1")] public int Count { get; set; } = 1;
	[Export(PropertyHint.Range, "1,32,1")] public int FormationColumns { get; set; } = 6;
	[Export(PropertyHint.Range, "1,8,0.1")] public float FormationSpacing { get; set; } = 2.8f;
	[Export] public FpsLaneDirection Lane { get; set; } = FpsLaneDirection.North;
}
