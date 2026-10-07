using Godot;
using Godot.Collections;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsArenaLayoutSettings : Resource
{
	[ExportGroup("Bounds")]
	[Export(PropertyHint.Range, "6,256,0.1")] public float HalfWidth { get; set; } = 30f;
	[Export(PropertyHint.Range, "6,256,0.1")] public float HalfDepth { get; set; } = 30f;

	[ExportGroup("Surfaces")]
	[Export(PropertyHint.Range, "0.2,8,0.05")] public float FloorThickness { get; set; } = 1f;
	[Export(PropertyHint.Range, "1,16,0.1")] public float WallHeight { get; set; } = 6f;
	[Export(PropertyHint.Range, "0.2,8,0.05")] public float WallThickness { get; set; } = 1.5f;
	[Export] public Color FloorColor { get; set; } = new Color(0.08f, 0.10f, 0.14f, 1f);
	[Export] public Color WallColor { get; set; } = new Color(0.10f, 0.12f, 0.18f, 1f);

	[ExportGroup("Spawn")]
	[Export] public Vector3 PlayerSpawnPosition { get; set; } = new Vector3(0f, 0f, 18f);

	[ExportGroup("Cover")]
	[Export] public Array<FpsArenaBlockSettings> CoverBlocks { get; set; } = CreateDefaultCoverBlocks();

	private static Array<FpsArenaBlockSettings> CreateDefaultCoverBlocks()
	{
		return new Array<FpsArenaBlockSettings>
		{
			new FpsArenaBlockSettings
			{
				BlockName = "CenterCoverA",
				Size = new Vector3(4f, 4f, 4f),
				Position = new Vector3(-8f, 2f, -2f),
				Color = new Color(0.18f, 0.18f, 0.24f, 1f),
			},
			new FpsArenaBlockSettings
			{
				BlockName = "CenterCoverB",
				Size = new Vector3(4f, 4f, 4f),
				Position = new Vector3(8f, 2f, 4f),
				Color = new Color(0.18f, 0.18f, 0.24f, 1f),
			},
			new FpsArenaBlockSettings
			{
				BlockName = "SideCoverA",
				Size = new Vector3(3.2f, 3f, 3.2f),
				Position = new Vector3(-17f, 1.5f, 12f),
				Color = new Color(0.15f, 0.17f, 0.22f, 1f),
			},
			new FpsArenaBlockSettings
			{
				BlockName = "SideCoverB",
				Size = new Vector3(3.2f, 3f, 3.2f),
				Position = new Vector3(16f, 1.5f, -13f),
				Color = new Color(0.15f, 0.17f, 0.22f, 1f),
			},
		};
	}
}
