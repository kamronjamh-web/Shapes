using Godot;

namespace SuperGatchaRebuild.Shapes;

[GlobalClass]
public partial class ShapeVisualSettings : Resource
{
	[ExportGroup("Cel Core")]
	[Export] public Color NeutralCelBaseColor { get; set; } = new(0.430f, 0.445f, 0.485f, 1f);
	[Export] public Color NeutralCelShadowColor { get; set; } = new(0.236f, 0.254f, 0.302f, 1f);
	[Export] public Color NeutralCelHighlightColor { get; set; } = new(0.876f, 0.896f, 0.934f, 1f);
	[Export] public Color NeutralCelRimColor { get; set; } = new(0.860f, 0.886f, 0.930f, 1f);
	[Export] public Color NeutralCelPointColor { get; set; } = new(0.840f, 0.875f, 0.925f, 1f);

	[ExportGroup("Outline Shape")]
	[Export(PropertyHint.Range, "0.001,0.200,0.001")] public float StandardCelOutlineWidth { get; set; } = 0.036f;
	[Export(PropertyHint.Range, "0.001,0.200,0.001")] public float SlimCelOutlineWidth { get; set; } = 0.030f;

	[ExportGroup("Outline Color")]
	[Export] public Color OutlineBaseDarkColor { get; set; } = new(0.03f, 0.035f, 0.05f, 1f);
	[Export(PropertyHint.Range, "0.000,1.000,0.010")] public float OutlineTintMix { get; set; } = 0.74f;
	[Export(PropertyHint.Range, "0.000,1.000,0.010")] public float OutlineTintDarken { get; set; } = 0.08f;

	[ExportGroup("Outline Glow")]
	[Export(PropertyHint.Range, "0.000,1.000,0.010")] public float OutlineEmissionLightenAmount { get; set; } = 0.30f;
	[Export(PropertyHint.Range, "0.000,1.000,0.010")] public float OutlineEmissionWhiteMix { get; set; } = 0.18f;
	[Export(PropertyHint.Range, "0.000,10.000,0.010")] public float OutlineEmissionEnergy { get; set; } = 1.50f;
}
