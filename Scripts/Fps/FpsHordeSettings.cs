using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Ordinary enemies use data slots and shared rendering, not physics bodies.</summary>
[GlobalClass]
public partial class FpsHordeSettings : Resource
{
	[ExportGroup("Budget")]
	[Export] public bool Enabled { get; set; } = true;
	[Export(PropertyHint.Range, "100,10000,100")] public int Capacity { get; set; } = 1000;
	[Export(PropertyHint.Range, "5,60,1")] public float SimulationHz { get; set; } = 30f;
	[Export(PropertyHint.Range, "1,30,1")] public float DecisionHz { get; set; } = 6f;
	[Export(PropertyHint.Range, "1,128,1")] public int MaxSeparationNeighbors { get; set; } = 12;
	[Export(PropertyHint.Range, "1,8,1")] public int MaxCatchUpSteps { get; set; } = 3;
	[ExportGroup("Spatial Grid")]
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float GridCellSize { get; set; } = 3f;
	[Export(PropertyHint.Range, "50,1000,1")] public float MapExtent { get; set; } = 200f;
	[ExportGroup("Steering")]
	[Export(PropertyHint.Range, "0,6,0.05")] public float SeparationRadius { get; set; } = 1.4f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float SeparationStrength { get; set; } = 1.1f;
	[Export(PropertyHint.Range, "0,100,0.1")] public float PlayerAggroRadius { get; set; } = 6f;
	[Export(PropertyHint.Range, "0,10,0.05")] public float CoreContactRadius { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HitFlashSeconds { get; set; } = 0.1f;
	[ExportGroup("Repeatability")]
	[Export] public int Seed { get; set; } = 137;
}
