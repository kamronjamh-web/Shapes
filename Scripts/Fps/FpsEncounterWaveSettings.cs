using Godot;
using Godot.Collections;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsEncounterWaveSettings : Resource
{
	[Export] public string WaveName { get; set; } = "Breach Wave";
	[Export] public bool Enabled { get; set; } = true;

	[ExportGroup("Spawns")]
	[Export] public Array<FpsEnemySpawnSettings> EnemySpawns { get; set; } = new();

	[ExportGroup("Scaling")]
	[Export(PropertyHint.Range, "0.1,10,0.01")] public float HealthMultiplier { get; set; } = 1f;
	[Export(PropertyHint.Range, "0.1,10,0.01")] public float DamageMultiplier { get; set; } = 1f;
	[Export(PropertyHint.Range, "0.1,10,0.01")] public float MoveSpeedMultiplier { get; set; } = 1f;
	[Export(PropertyHint.Range, "0.1,10,0.01")] public float AttackCadenceMultiplier { get; set; } = 1f;
}
