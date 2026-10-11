using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public enum FpsExplosionSize { Small, Medium, Large }

public partial class FpsWeaponPolishAudio
{
    private const int ExplosionVoiceCount = 4;
    private readonly AudioStreamPlayer3D[] _explosionClose = new AudioStreamPlayer3D[ExplosionVoiceCount];
    private readonly AudioStreamPlayer3D[] _explosionFar = new AudioStreamPlayer3D[ExplosionVoiceCount];
    private int _explosionCursor;
    private double _nextExplosion;
    private readonly Random _explosionVariation = new(0x4558504C);
    private FpsExplosionAudioSettings? _explosionSettings;
    [Export] public FpsExplosionAudioSettings ExplosionSettings
    {
        get => _explosionSettings ??= new FpsExplosionAudioSettings();
        set => _explosionSettings = value;
    }
    internal float LastExplosionRadius { get; private set; }
    internal FpsExplosionSize LastExplosionSize { get; private set; }
    internal long ExplosionPlaybackCount { get; private set; }

    private void InitializeExplosions()
    {
        _explosionSettings ??= GD.Load<FpsExplosionAudioSettings>("res://Resources/Audio/Explosions/DefaultExplosionAudio.tres")
            ?? new FpsExplosionAudioSettings();
        for (int i = 0; i < ExplosionVoiceCount; i++)
        {
            _explosionClose[i] = new AudioStreamPlayer3D { Name = $"ExplosionCloseVoice{i}", MaxPolyphony = 1 };
            _explosionFar[i] = new AudioStreamPlayer3D { Name = $"ExplosionFarVoice{i}", MaxPolyphony = 1 };
            AddChild(_explosionClose[i]); AddChild(_explosionFar[i]);
        }
    }

    public FpsExplosionSize ClassifyExplosion(float radius)
    {
        float medium = PositiveOr(ExplosionSettings.MediumRadiusStart, 3f);
        float large = Mathf.Max(medium, PositiveOr(ExplosionSettings.LargeRadiusStart, 8f));
        return radius < medium ? FpsExplosionSize.Small : radius < large ? FpsExplosionSize.Medium : FpsExplosionSize.Large;
    }

    internal AudioStream? ResolveExplosionStream(FpsExplosionSize size, bool far)
    {
        var settings = ExplosionSettings;
        AudioStream? selected = (size, far) switch
        {
            (FpsExplosionSize.Small, false) => settings.SmallClose,
            (FpsExplosionSize.Small, true) => settings.SmallFar,
            (FpsExplosionSize.Large, false) => settings.LargeClose,
            (FpsExplosionSize.Large, true) => settings.LargeFar,
            (_, false) => settings.MediumClose,
            _ => settings.MediumFar,
        };
        // A missing distance partner uses its supplied size partner before the
        // temporary medium fallback. No synthetic small/large clip is invented.
        AudioStream? partner = size switch
        {
            FpsExplosionSize.Small => far ? settings.SmallClose : settings.SmallFar,
            FpsExplosionSize.Large => far ? settings.LargeClose : settings.LargeFar,
            _ => far ? settings.MediumClose : settings.MediumFar,
        };
        return selected ?? partner ?? (far ? settings.MediumFar : settings.MediumClose)
            ?? settings.MediumClose ?? settings.MediumFar;
    }

    public void PlayExplosion(Vector3 position, float radius)
    {
        if (!CanPlay() || !position.IsFinite() || !float.IsFinite(radius) || radius <= 0f || _time < _nextExplosion) return;
        Camera3D? listener = GetViewport().GetCamera3D();
        float maxDistance = PositiveOr(ExplosionSettings.MaxDistance, 160f);
        if (listener == null || listener.GlobalPosition.DistanceSquaredTo(position) >= maxDistance * maxDistance) return;
        FpsExplosionSize size = ClassifyExplosion(radius);
        AudioStream? close = ResolveExplosionStream(size, false), far = ResolveExplosionStream(size, true);
        if (close == null || far == null) return;
        int index = _explosionCursor++ % ExplosionVoiceCount;
        AudioStreamPlayer3D nearVoice = _explosionClose[index], farVoice = _explosionFar[index];
        nearVoice.Stop(); farVoice.Stop();
        float spread = Mathf.Clamp(FiniteOr(ExplosionSettings.PitchVariationSemitones, 1.5f), 0f, 6f);
        float pitch = Mathf.Pow(2f, ((float)_explosionVariation.NextDouble() * 2f - 1f) * spread / 12f);
        ConfigureExplosionVoice(nearVoice, close, position, pitch);
        ConfigureExplosionVoice(farVoice, far, position, pitch);
        _nextExplosion = _time + .09;
        UpdateExplosionPair(index, listener);
        nearVoice.Play();
        // Same-source close/far uses one native continuously attenuated/filtered
        // player, avoiding phase interference and doubled gain.
        if (!ReferenceEquals(close, far)) farVoice.Play();
        LastExplosionRadius = radius;
        LastExplosionSize = size;
        ExplosionPlaybackCount++;
    }

    private void ConfigureExplosionVoice(AudioStreamPlayer3D voice, AudioStream stream, Vector3 position, float pitch)
    {
        voice.Stream = stream; voice.GlobalPosition = position;
        voice.PitchScale = pitch;
        voice.UnitSize = PositiveOr(ExplosionSettings.CloseDistance, 24f);
        voice.MaxDistance = PositiveOr(ExplosionSettings.MaxDistance, 160f);
        voice.AttenuationFilterCutoffHz = Mathf.Clamp(PositiveOr(ExplosionSettings.FarFilterCutoffHz, 2500f), 20f, 20500f);
        voice.Bus = _bus;
    }

    private void UpdateExplosions(bool suspended)
    {
        Camera3D? listener = GetViewport().GetCamera3D();
        for (int i = 0; i < ExplosionVoiceCount; i++)
        {
            _explosionClose[i].StreamPaused = _explosionFar[i].StreamPaused = suspended;
            if (listener != null) UpdateExplosionPair(i, listener);
        }
    }

    private void UpdateExplosionRouting()
    {
        Camera3D? listener = GetViewport().GetCamera3D();
        for (int i = 0; i < ExplosionVoiceCount; i++)
        {
            _explosionClose[i].Bus = _explosionFar[i].Bus = _bus;
            if (listener != null) UpdateExplosionPair(i, listener);
        }
    }

    private void UpdateExplosionPair(int index, Camera3D listener)
    {
        AudioStreamPlayer3D close = _explosionClose[index], far = _explosionFar[index];
        float closeDistance = PositiveOr(ExplosionSettings.CloseDistance, 24f);
        float farDistance = Mathf.Max(closeDistance + .1f, PositiveOr(ExplosionSettings.FarDistance, 100f));
        float blend = Mathf.Clamp((listener.GlobalPosition.DistanceTo(close.GlobalPosition) - closeDistance)
            / (farDistance - closeDistance), 0f, 1f);
        // Godot's shelf strength includes VolumeDb, so a constant negative value
        // muffles even a nearby quiet sound. Keep the close band fully dry and
        // introduce the shelf only as physical distance crosses that band.
        float filterBlend = blend * blend * (3f - 2f * blend);
        float filterDb = Mathf.Clamp(FiniteOr(ExplosionSettings.FarFilterDb, -24f), -80f, 0f) * filterBlend;
        close.AttenuationFilterDb = far.AttenuationFilterDb = filterDb;
        // Equal-power continuous crossfade applies only to distinct catalog clips.
        bool same = ReferenceEquals(close.Stream, far.Stream);
        float gain = Mathf.Clamp(FiniteOr(ExplosionSettings.VolumeDb, -12f), -80f, 0f) + _fallbackSfxDb;
        close.VolumeDb = gain + GainDb(same ? 1f : Mathf.Sqrt(1f - blend));
        far.VolumeDb = gain + GainDb(same ? 0f : Mathf.Sqrt(blend));
        // Godot 4.7.2 adds VolumeDb BEFORE clamping to MaxDb. Matching the
        // per-voice cap preserves authored/crossfade/SFX gain even at distance0,
        // where inverse-distance attenuation would otherwise amplify it away.
        close.MaxDb = close.VolumeDb;
        far.MaxDb = far.VolumeDb;
    }

    private void PauseExplosions(bool suspended)
    {
        foreach (AudioStreamPlayer3D? voice in _explosionClose) if (voice != null) voice.StreamPaused = suspended;
        foreach (AudioStreamPlayer3D? voice in _explosionFar) if (voice != null) voice.StreamPaused = suspended;
    }

    private void ClearExplosions()
    {
        foreach (AudioStreamPlayer3D? voice in _explosionClose) voice?.Stop();
        foreach (AudioStreamPlayer3D? voice in _explosionFar) voice?.Stop();
        _nextExplosion = _time;
    }

    private static float GainDb(float gain) => gain <= .0001f ? -80f : Mathf.LinearToDb(gain);
    private static float PositiveOr(float value, float fallback) => float.IsFinite(value) && value > 0f ? value : fallback;
    private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
