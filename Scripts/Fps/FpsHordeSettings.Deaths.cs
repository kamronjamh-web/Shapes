using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeSettings
{
	[ExportGroup("Death Tumbling")]
	[Export] public bool CorpseEnabled { get; set; } = true;
	[Export(PropertyHint.Range, "1,256,1")] public int CorpseCapacity { get; set; } = 64;
	[Export(PropertyHint.Range, "0.1,15,0.1")] public float CorpseLifetimeSeconds { get; set; } = 4f;
	[Export(PropertyHint.Range, "0,24,0.1")] public float CorpseBaseLaunchSpeed { get; set; } = 3f;
	[Export(PropertyHint.Range, "0,5,0.1")] public float CorpseOverkillSpeedScale { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "0,40,0.1")] public float CorpseMaxLaunchSpeed { get; set; } = 24f;
	[Export(PropertyHint.Range, "0,10,0.1")] public float CorpseUpwardSpeed { get; set; } = 2f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float CorpseAngularSpeed { get; set; } = 5f;
}
