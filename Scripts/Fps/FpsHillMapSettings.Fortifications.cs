using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHillMapSettings
{
	private bool _fortificationsEnabled;
	private float _gateMaxHealth = 1000f;
	private float _fortWallThickness = 2f, _fortWallWalkWidth = 3f;
	private float _fortPlatformWidth = 8f, _fortPlatformDepth = 6f, _fortDeckThickness = 0.4f;
	private float _fortAccessRampWidth = 3f, _fortAccessRampRun = 18.288f, _fortRailHeight = 0.9f;
	[ExportGroup("Fortifications")]
	[Export] public bool FortificationsEnabled { get => _fortificationsEnabled; set => Change(ref _fortificationsEnabled, value); }
	[Export(PropertyHint.Range, "1,20000,1")] public float GateMaxHealth { get => _gateMaxHealth; set => Change(ref _gateMaxHealth, value); }
	[Export(PropertyHint.Range, "0.5,6,0.1")] public float FortWallThickness { get => _fortWallThickness; set => Change(ref _fortWallThickness, value); }
	[Export(PropertyHint.Range, "2,8,0.1")] public float FortWallWalkWidth { get => _fortWallWalkWidth; set => Change(ref _fortWallWalkWidth, value); }
	[Export(PropertyHint.Range, "4,16,0.1")] public float FortPlatformWidth { get => _fortPlatformWidth; set => Change(ref _fortPlatformWidth, value); }
	[Export(PropertyHint.Range, "2,12,0.1")] public float FortPlatformDepth { get => _fortPlatformDepth; set => Change(ref _fortPlatformDepth, value); }
	[Export(PropertyHint.Range, "0.2,1,0.05")] public float FortDeckThickness { get => _fortDeckThickness; set => Change(ref _fortDeckThickness, value); }
	[Export(PropertyHint.Range, "2,6,0.1")] public float FortAccessRampWidth { get => _fortAccessRampWidth; set => Change(ref _fortAccessRampWidth, value); }
	[Export(PropertyHint.Range, "10,40,0.1")] public float FortAccessRampRun { get => _fortAccessRampRun; set => Change(ref _fortAccessRampRun, value); }
	[Export(PropertyHint.Range, "0,1.2,0.05")] public float FortRailHeight { get => _fortRailHeight; set => Change(ref _fortRailHeight, value); }
}
