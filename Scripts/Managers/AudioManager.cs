using Godot;
using SuperGatchaRebuild.UI;

namespace SuperGatchaRebuild.Managers;

// Central audio manager for volume settings and one-shot SFX playback.
public partial class AudioManager : Node, IAudioManager
{
    [Export] public string SfxBusName = "SFX";
    [Export] public string MusicBusName = "Music";

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MasterVolume { get; set; } = 1f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SfxVolume { get; set; } = 1f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MusicVolume { get; set; } = 1f;

    private CanvasLayer? _screenSpaceSfxLayer;
    private Node2D? _screenSpaceSfxRoot;

    public override void _Ready()
    {
        EnsureScreenSpaceSfxRoot();
        ApplyVolumes();
    }

    public void PlayOneShot(AudioStream? stream, float volumeDb = 0f, float pitchScale = 1f)
    {
        if (stream == null)
        {
            return;
        }

        AudioStreamPlayer player = new()
        {
            Stream = stream,
            Bus = ResolveBusName(SfxBusName),
            VolumeDb = volumeDb,
            PitchScale = pitchScale,
        };

        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    public void PlayScreenSpaceOneShot(
        AudioStream? stream,
        Vector2 screenPosition,
        Vector2 viewportSize,
        float volumeDb = 0f,
        float pitchScale = 1f,
        float panningStrength = 1f)
    {
        if (stream == null)
        {
            return;
        }

        Node2D root = EnsureScreenSpaceSfxRoot();
        if (!IsInstanceValid(root))
        {
            PlayOneShot(stream, volumeDb, pitchScale);
            return;
        }

        Vector2 safeViewportSize = new(
            Mathf.Max(viewportSize.X, 1f),
            Mathf.Max(viewportSize.Y, 1f));
        Vector2 clampedScreenPosition = new(
            Mathf.Clamp(screenPosition.X, 0f, safeViewportSize.X),
            Mathf.Clamp(screenPosition.Y, 0f, safeViewportSize.Y));

        AudioStreamPlayer2D player = new()
        {
            Stream = stream,
            Bus = ResolveBusName(SfxBusName),
            VolumeDb = volumeDb,
            PitchScale = pitchScale,
            Position = clampedScreenPosition,
            PanningStrength = panningStrength,
            Attenuation = 0f,
            MaxDistance = Mathf.Max(safeViewportSize.Length(), 1f),
        };

        root.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    public void ApplyVolumes()
    {
        SetBusLinear("Master", MasterVolume);
        SetBusLinear(SfxBusName, SfxVolume);
        SetBusLinear(MusicBusName, MusicVolume);
    }

    private Node2D EnsureScreenSpaceSfxRoot()
    {
        if (IsInstanceValid(_screenSpaceSfxRoot))
        {
            return _screenSpaceSfxRoot;
        }

        if (!IsInstanceValid(_screenSpaceSfxLayer))
        {
            _screenSpaceSfxLayer = new CanvasLayer
            {
                Name = "ScreenSpaceSfxLayer",
            };
            AddChild(_screenSpaceSfxLayer);
        }

        _screenSpaceSfxRoot = new Node2D
        {
            Name = "ScreenSpaceSfxRoot",
        };
        _screenSpaceSfxLayer.AddChild(_screenSpaceSfxRoot);
        return _screenSpaceSfxRoot;
    }

    private static string ResolveBusName(string preferredBusName)
    {
        return AudioServer.GetBusIndex(preferredBusName) >= 0
            ? preferredBusName
            : "Master";
    }

    private static void SetBusLinear(string busName, float linear)
    {
        int busIndex = AudioServer.GetBusIndex(busName);
        if (busIndex < 0)
        {
            return;
        }

        float clamped = Mathf.Clamp(linear, 0f, 1f);
        float db = clamped <= 0.0001f ? -80f : Mathf.LinearToDb(clamped);
        AudioServer.SetBusVolumeDb(busIndex, db);
    }
}
