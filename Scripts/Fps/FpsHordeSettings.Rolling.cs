using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeSettings
{
	[ExportGroup("Cube Rolling Gait")]
	[Export] public bool RollingEnabled { get; set; } = true;
	// Zero selects a speed-based roll time. A positive minimum slows unusually fast rolls.
	[Export(PropertyHint.Range, "0,1,0.01")] public float RollingDurationSeconds { get; set; } = 0f;
	[Export(PropertyHint.Range, "0,0.3,0.005")] public float RollingPauseSeconds { get; set; } = 0.055f;
}
