using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Live-editable dimensions for the hill and its summit room, in metres.</summary>
[Tool, GlobalClass]
public partial class FpsHillMapSettings : Resource
{
	private float _playableSize = 200f;
	private float _summitHeight = 8f;
	private float _summitHalfSize = 16f;
	private float _hillFootHalfSize = 65f;
	private float _rampWidth = 24f;
	private float _roomHalfSize = 12f;
	private float _roomWallHeight = 7f;
	private float _doorWidth = 8f;
	private float _doorHeight = 6f;
	private bool _roofEnabled;
	private bool _eastDoorEnabled;
	private bool _southDoorEnabled;
	private bool _westDoorEnabled;
	private Color _groundColor = new(0.16f, 0.22f, 0.19f);
	private Color _hillColor = new(0.24f, 0.31f, 0.23f);
	private Color _roomColor = new(0.36f, 0.40f, 0.42f);

	[ExportGroup("Terrain")]
	[Export(PropertyHint.Range, "100,2048,0.001")] public float PlayableSize { get => _playableSize; set => Change(ref _playableSize, value); }
	[Export(PropertyHint.Range, "1,20,0.5")] public float SummitHeight { get => _summitHeight; set => Change(ref _summitHeight, value); }
	[Export(PropertyHint.Range, "8,400,0.5")] public float SummitHalfSize { get => _summitHalfSize; set => Change(ref _summitHalfSize, value); }
	[Export(PropertyHint.Range, "24,1000,1")] public float HillFootHalfSize { get => _hillFootHalfSize; set => Change(ref _hillFootHalfSize, value); }
	[Export(PropertyHint.Range, "8,48,0.5")] public float RampWidth { get => _rampWidth; set => Change(ref _rampWidth, value); }
	[Export] public Color GroundColor { get => _groundColor; set => Change(ref _groundColor, value); }
	[Export] public Color HillColor { get => _hillColor; set => Change(ref _hillColor, value); }

	[ExportGroup("Summit Room")]
	[Export(PropertyHint.Range, "6,398,0.5")] public float RoomHalfSize { get => _roomHalfSize; set => Change(ref _roomHalfSize, value); }
	[Export(PropertyHint.Range, "4,16,0.5")] public float RoomWallHeight { get => _roomWallHeight; set => Change(ref _roomWallHeight, value); }
	[Export(PropertyHint.Range, "4,20,0.5")] public float DoorWidth { get => _doorWidth; set => Change(ref _doorWidth, value); }
	[Export(PropertyHint.Range, "3,12,0.5")] public float DoorHeight { get => _doorHeight; set => Change(ref _doorHeight, value); }
	[Export] public bool RoofEnabled { get => _roofEnabled; set => Change(ref _roofEnabled, value); }
	[Export] public bool EastDoorEnabled { get => _eastDoorEnabled; set => Change(ref _eastDoorEnabled, value); }
	[Export] public bool SouthDoorEnabled { get => _southDoorEnabled; set => Change(ref _southDoorEnabled, value); }
	[Export] public bool WestDoorEnabled { get => _westDoorEnabled; set => Change(ref _westDoorEnabled, value); }
	[Export] public Color RoomColor { get => _roomColor; set => Change(ref _roomColor, value); }

	private void Change<T>(ref T field, T value)
	{
		if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		EmitChanged();
	}
}
