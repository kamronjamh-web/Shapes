using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Six clip slots; missing small/large clips temporarily use medium.</summary>
[GlobalClass]
public partial class FpsExplosionAudioSettings : Resource
{
    [Export] public AudioStream? SmallClose { get; set; }
    [Export] public AudioStream? SmallFar { get; set; }
    [Export] public AudioStream? MediumClose { get; set; }
    [Export] public AudioStream? MediumFar { get; set; }
    [Export] public AudioStream? LargeClose { get; set; }
    [Export] public AudioStream? LargeFar { get; set; }
    [Export(PropertyHint.Range, ".1,30,.1")] public float MediumRadiusStart { get; set; } = 3f;
    [Export(PropertyHint.Range, ".1,50,.1")] public float LargeRadiusStart { get; set; } = 8f;
    [Export] public float CloseDistance { get; set; } = 24f;
    // End of the distance-filter transition and distinct-clip crossfade.
    [Export] public float FarDistance { get; set; } = 100f;
    [Export] public float MaxDistance { get; set; } = 160f;
    [Export] public float VolumeDb { get; set; } = -12f;
    [Export] public float FarFilterCutoffHz { get; set; } = 2500f;
    [Export] public float FarFilterDb { get; set; } = -24f;
    [Export(PropertyHint.Range, "0,6,0.1")] public float PitchVariationSemitones { get; set; } = 1.5f;
}
