using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Ordinary enemies use data slots and shared rendering, not physics bodies.</summary>
[GlobalClass]
public partial class FpsHordeSettings : Resource
{
	public const int MaximumCapacity = 100_000;
	[ExportGroup("Budget")]
	[Export] public bool Enabled { get; set; } = true;
	[Export] public bool GpuCrowdRenderingEnabled { get; set; }
	[Export(PropertyHint.Range, "100,100000,100")] public int Capacity { get; set; } = 1000;
	[Export(PropertyHint.Range, "5,60,1")] public float SimulationHz { get; set; } = 30f;
	[Export(PropertyHint.Range, "1,30,1")] public float DecisionHz { get; set; } = 6f;
	[Export(PropertyHint.Range, "1,128,1")] public int MaxSeparationNeighbors { get; set; } = 12;
	[Export(PropertyHint.Range, "1,8,1")] public int MaxCatchUpSteps { get; set; } = 3;
	[ExportGroup("Spatial Grid")]
	[Export(PropertyHint.Range, "0.5,12,0.1")] public float GridCellSize { get; set; } = 3f;
	[Export(PropertyHint.Range, "50,2048,1")] public float MapExtent { get; set; } = 200f;
	[ExportGroup("Steering")]
	[Export(PropertyHint.Range, "0,6,0.05")] public float SeparationRadius { get; set; } = 1.4f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float SeparationStrength { get; set; } = 1.1f;
	[Export(PropertyHint.Range, "0,100,0.1")] public float PlayerAggroRadius { get; set; } = 6f;
	[Export(PropertyHint.Range, "0,10,0.05")] public float CoreContactRadius { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HitFlashSeconds { get; set; } = 0.1f;
	[ExportGroup("Body Contact And Piling")]
	[Export] public bool GpuCrowdContactsEnabled { get; set; }
	[Export] public bool SoftCollisionEnabled { get; set; } = true;
	[Export] public bool StackingEnabled { get; set; } = true;
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float ContactStiffness { get; set; } = 0.65f;
	[Export(PropertyHint.Range, "1,4,1")] public int ContactIterations { get; set; } = 1;
	[Export(PropertyHint.Range, "8,64,1")] public int ContactNeighborBudget { get; set; } = 32;
	[Export(PropertyHint.Range, "0.1,3,0.05")] public float CrowdPushStrength { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,8,0.1")] public float ClimbSpeed { get; set; } = 2.8f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float Gravity { get; set; } = 18f;
	[Export(PropertyHint.Range, "0,12,0.1")] public float MaxPileHeight { get; set; } = 10f;
	[ExportGroup("Repeatability")]
	[Export] public int Seed { get; set; } = 137;
}
