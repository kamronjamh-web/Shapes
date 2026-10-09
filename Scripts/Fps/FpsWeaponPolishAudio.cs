using System;
using Godot;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Fps;

/// <summary>Bounded, cosmetic audio for all sixteen weapons. Call on the scene thread.</summary>
public partial class FpsWeaponPolishAudio : Node
{
    private const int ShotVoiceCount = 4;
    private const int ImpactVoiceCount = 8;
    private const float AudibleDistance = 48f;
    private const int SampleRate = 22050;
    private static AudioStreamWav[]? _cachedStreams;
    private readonly AudioStreamPlayer[] _shots = new AudioStreamPlayer[ShotVoiceCount];
    private readonly AudioStreamPlayer3D[] _impacts = new AudioStreamPlayer3D[ImpactVoiceCount];
    private readonly double[] _nextImpactByType = new double[16];
    private readonly double[] _nextActivationByType = new double[16];
    private readonly float[] _impactIntensityDb = new float[ImpactVoiceCount];
    private readonly Random _variation = new(0x53485045);
    private AudioManager? _audioManager;
    private bool _enabled = true;
    private bool _paused;
    private double _time;
    private double _nextShot;
    private double _nextImpact;
    private double _nextRift;
    private double _nextRoutingUpdate;
    private int _shotCursor;
    private int _impactCursor;
    private string _bus = "Master";
    private float _fallbackSfxDb;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) Clear();
        }
    }

    public override void _Ready()
    {
        // Explicit gameplay pause does not pause the tree; keep this helper able to resume.
        ProcessMode = ProcessModeEnum.Always;
        _audioManager = GetNodeOrNull<AudioManager>("/root/AudioManager");
        _cachedStreams ??= BuildStreams();
        for (int i = 0; i < ShotVoiceCount; i++)
        {
            _shots[i] = new AudioStreamPlayer { Name = $"WeaponShotVoice{i}", MaxPolyphony = 1 };
            AddChild(_shots[i]);
        }
        for (int i = 0; i < ImpactVoiceCount; i++)
        {
            _impacts[i] = new AudioStreamPlayer3D
            {
                Name = $"WeaponImpactVoice{i}", MaxPolyphony = 1,
                MaxDistance = AudibleDistance, UnitSize = 4f, MaxDb = -22f,
            };
            AddChild(_impacts[i]);
        }
        UpdateRouting();
    }

    public override void _Process(double delta)
    {
        bool suspended = _paused || GetTree().Paused;
        foreach (AudioStreamPlayer voice in _shots) voice.StreamPaused = suspended;
        foreach (AudioStreamPlayer3D voice in _impacts) voice.StreamPaused = suspended;
        if (!suspended) _time += delta;
        if (_time >= _nextRoutingUpdate)
        {
            UpdateRouting();
            _nextRoutingUpdate = _time + 0.25;
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        // Apply immediately so pause does not leak an additional audio frame.
        foreach (AudioStreamPlayer? voice in _shots)
            if (voice != null) voice.StreamPaused = paused || (IsInsideTree() && GetTree().Paused);
        foreach (AudioStreamPlayer3D? voice in _impacts)
            if (voice != null) voice.StreamPaused = paused || (IsInsideTree() && GetTree().Paused);
    }

    public void PlayShot(WeaponAttackType type, Vector3 origin)
    {
        int index = SoundIndex(type);
        // Shots belong to the local player and remain audible without spatial rolloff.
        _ = origin;
        if (!CanPlay() || index < 0 || _time < _nextShot) return;
        _nextShot = _time + 0.045;
        AudioStreamPlayer voice = _shots[_shotCursor++ % ShotVoiceCount];
        voice.Stop();
        voice.Stream = _cachedStreams![ShotStreamIndex(index)];
        voice.PitchScale = NextPitch();
        voice.Play();
    }

    public void PlayImpact(WeaponAttackType type, Vector3 position, float intensity = 1f)
    {
        int index = SoundIndex(type);
        if (!CanPlay() || index < 0 || !float.IsFinite(intensity) || intensity <= 0f
            || _time < _nextImpact || _time < _nextImpactByType[index] || !InRange(position)) return;
        AudioStreamPlayer3D? voice = FindFreeImpactVoice();
        if (voice == null) return;
        _nextImpact = _time + 0.045;
        _nextImpactByType[index] = _time + 0.09;
        PlayWorld(voice, _cachedStreams![ImpactStreamIndex(index)], position, Mathf.Clamp(intensity, 0.1f, 1f));
    }

    /// <summary>Arming, detonation, summoning, or field onset; shares the spatial impact budget.</summary>
    public void PlayActivation(WeaponAttackType type, Vector3 position)
    {
        int index = SoundIndex(type);
        if (!CanPlay() || index < 0 || _time < _nextImpact
            || _time < _nextActivationByType[index] || !InRange(position)) return;
        AudioStreamPlayer3D? voice = FindFreeImpactVoice();
        if (voice == null) return;
        _nextActivationByType[index] = _time + 0.22;
        _nextImpact = _time + 0.045;
        PlayWorld(voice, _cachedStreams![33 + index], position, 0.9f);
    }

    public void PlayRiftPulse(Vector3 position)
    {
        if (!CanPlay() || _time < _nextRift || _time < _nextImpact || !InRange(position)) return;
        AudioStreamPlayer3D? voice = FindFreeImpactVoice();
        if (voice == null) return;
        _nextRift = _time + 0.22;
        _nextImpact = _time + 0.045;
        PlayWorld(voice, _cachedStreams![8], position, 0.8f);
    }

    public void Clear()
    {
        foreach (AudioStreamPlayer? voice in _shots) voice?.Stop();
        foreach (AudioStreamPlayer3D? voice in _impacts) voice?.Stop();
        Array.Clear(_nextImpactByType);
        Array.Clear(_nextActivationByType);
        _nextShot = _nextImpact = _nextRift = _time;
    }

    private bool CanPlay() => _enabled && !_paused && IsInsideTree() && !GetTree().Paused
        && _cachedStreams != null && _shots[0] != null;

    private bool InRange(Vector3 position)
    {
        if (!position.IsFinite()) return false;
        Camera3D? listener = GetViewport().GetCamera3D();
        return listener != null && listener.GlobalPosition.DistanceSquaredTo(position) <= AudibleDistance * AudibleDistance;
    }

    private AudioStreamPlayer3D? FindFreeImpactVoice()
    {
        for (int i = 0; i < ImpactVoiceCount; i++)
        {
            int index = (_impactCursor + i) % ImpactVoiceCount;
            if (_impacts[index].Playing) continue;
            _impactCursor = (index + 1) % ImpactVoiceCount;
            return _impacts[index];
        }
        return null;
    }

    private void PlayWorld(AudioStreamPlayer3D voice, AudioStreamWav stream, Vector3 position, float intensity)
    {
        voice.GlobalPosition = position;
        voice.Stream = stream;
        float intensityDb = Mathf.LinearToDb(intensity);
        _impactIntensityDb[Array.IndexOf(_impacts, voice)] = intensityDb;
        voice.VolumeDb = -22f + _fallbackSfxDb + intensityDb;
        voice.PitchScale = NextPitch();
        voice.Play();
    }

    private float NextPitch() => 0.96f + (float)_variation.NextDouble() * 0.08f;

    private void UpdateRouting()
    {
        string preferred = IsInstanceValid(_audioManager) ? _audioManager!.SfxBusName : "SFX";
        bool dedicatedBus = preferred != "Master" && AudioServer.GetBusIndex(preferred) >= 0;
        _bus = dedicatedBus ? preferred : "Master";
        float fallback = !dedicatedBus && IsInstanceValid(_audioManager)
            ? Mathf.Clamp(_audioManager!.SfxVolume, 0f, 1f) : 1f;
        _fallbackSfxDb = fallback <= 0.0001f ? -80f : Mathf.LinearToDb(fallback);
        foreach (AudioStreamPlayer voice in _shots)
        {
            voice.Bus = _bus;
            voice.VolumeDb = -14f + _fallbackSfxDb;
        }
        for (int i = 0; i < ImpactVoiceCount; i++)
        {
            AudioStreamPlayer3D voice = _impacts[i];
            voice.Bus = _bus;
            // Active world voices preserve their intensity adjustment when settings change.
            voice.VolumeDb = -22f + _fallbackSfxDb + _impactIntensityDb[i];
        }
    }

    private static int SoundIndex(WeaponAttackType type) => type switch
    {
        WeaponAttackType.Single => 0,
        WeaponAttackType.Sawblade => 1,
        WeaponAttackType.Laser => 2,
        WeaponAttackType.Rift => 3,
        WeaponAttackType.Mine => 4,
        WeaponAttackType.Nova => 5,
        WeaponAttackType.Orbital => 6,
        WeaponAttackType.Homing => 7,
        WeaponAttackType.FlameCone => 8,
        WeaponAttackType.Mortar => 9,
        WeaponAttackType.ChainLightning => 10,
        WeaponAttackType.Drill => 11,
        WeaponAttackType.VoidSingularity => 12,
        WeaponAttackType.TemporalEcho => 13,
        WeaponAttackType.WrathAura => 14,
        WeaponAttackType.ShadowClone => 15,
        _ => -1,
    };

    // Keep the accepted original nine slots and synthesis exactly unchanged.
    private static int ShotStreamIndex(int index) => index < 4 ? index : 9 + (index - 4) * 2;
    private static int ImpactStreamIndex(int index) => index < 4 ? index + 4 : 10 + (index - 4) * 2;

    private static AudioStreamWav[] BuildStreams()
    {
        AudioStreamWav[] streams = new AudioStreamWav[49];
        for (int i = 0; i < 9; i++)
        {
            int kind = i == 8 ? 3 : i % 4;
            bool impact = i >= 4;
            double duration = i == 8 ? 0.42 : kind switch
            {
                0 => impact ? 0.12 : 0.22,
                1 => impact ? 0.13 : 0.29,
                2 => impact ? 0.11 : 0.19,
                _ => impact ? 0.26 : 0.43,
            };
            int count = (int)(duration * SampleRate);
            byte[] pcm = new byte[count * 2];
            uint noiseState = (uint)(0x53484150 + i * 7349);
            for (int sample = 0; sample < count; sample++)
            {
                double t = (double)sample / SampleRate;
                double u = t / duration;
                noiseState ^= noiseState << 13;
                noiseState ^= noiseState >> 17;
                noiseState ^= noiseState << 5;
                double noise = noiseState / (double)uint.MaxValue * 2.0 - 1.0;
                double phase = Math.Tau * t;
                double raw = kind switch
                {
                    // Square: tight low body, broadband crack, delayed mechanical clack.
                    0 => Math.Sin(phase * 105) * Math.Exp(-t * 22) * 0.8
                        + noise * Math.Exp(-t * 95) * 1.5
                        + (t > 0.055 ? Math.Sin(phase * 1450) * Math.Exp(-(t - 0.055) * 85) * 0.4 : 0),
                    // Circle: descending rotor harmonics with a gritty cutting edge.
                    1 => (Math.Sin(phase * (630 - 330 * u)) * 0.65
                        + Math.Sin(phase * (1260 - 510 * u)) * 0.25
                        + noise * 0.32) * (0.72 + Math.Sin(phase * 38) * 0.28) * Math.Exp(-u * 3),
                    // Triangle: fast descending electrical snap above a short bright tone.
                    2 => Math.Sin(phase * (2400 - 1250 * u)) * Math.Exp(-u * 5)
                        + Math.Sin(phase * 820) * Math.Exp(-u * 7) * 0.35
                        + noise * Math.Exp(-t * 100) * 0.8,
                    // Tesseract: beating, inharmonic partials fold into a low dimensional pulse.
                    _ => (Math.Sin(phase * (155 + 45 * u)) * 0.8
                        + Math.Sin(phase * 223) * 0.42 + Math.Sin(phase * (472 - 180 * u)) * 0.3)
                        * (0.6 + Math.Cos(phase * 13) * 0.4) * Math.Exp(-u * 3),
                };
                double fadeIn = Math.Min(t / 0.002, 1.0);
                double fadeOut = Math.Min((duration - t) / 0.012, 1.0);
                short value = (short)(Math.Tanh(raw * (impact ? 0.8 : 1.0)) * 0.52 * fadeIn * fadeOut * short.MaxValue);
                pcm[sample * 2] = (byte)(value & 0xff);
                pcm[sample * 2 + 1] = (byte)((value >> 8) & 0xff);
            }
            streams[i] = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = SampleRate, Stereo = false, Data = pcm,
            };
        }
        for (int index = 4; index < 16; index++)
        {
            streams[ShotStreamIndex(index)] = BuildAdditionalStream(index, 0);
            streams[ImpactStreamIndex(index)] = BuildAdditionalStream(index, 1);
        }
        for (int index = 0; index < 16; index++) streams[33 + index] = BuildAdditionalStream(index, 2);
        return streams;
    }

    private static AudioStreamWav BuildAdditionalStream(int kind, int eventKind)
    {
        // Every cue is finite PCM, generated once; no loops or per-event streaming work.
        double duration = kind switch
        {
            4 => eventKind == 0 ? 0.24 : 0.34, // Mine: chamber latch / hexagonal charge.
            5 => 0.35,                       // Nova: pressure pulse and harmonic halo.
            6 => 0.32,                       // Orbital: resonant solid-block torque.
            7 => eventKind == 0 ? 0.28 : 0.20,// Homing: rising seeker whistle.
            8 => 0.27,                       // Flame: combustion pressure and grit.
            9 => eventKind == 0 ? 0.30 : 0.46,// Mortar: tubular launch / deep detonation.
            10 => 0.21,                      // Chain: staggered electrical cracks.
            11 => 0.31,                      // Drill: serrated motor spool.
            12 => 0.44,                      // Void: contracting bass and dissonance.
            13 => 0.39,                      // Echo: paired, receding time folds.
            14 => 0.42,                      // Aura: liquid glass resonances.
            15 => 0.43,                      // Clone: mirrored airy choir.
            _ => 0.23 + kind * 0.031,
        };
        if (eventKind == 2) duration += 0.055;
        int count = (int)(duration * SampleRate);
        byte[] pcm = new byte[count * 2];
        uint noiseState = (uint)(0x46585300 + kind * 9127 + eventKind * 421);
        double color = eventKind == 0 ? 1.0 : eventKind == 1 ? 0.72 : 0.88;
        for (int sample = 0; sample < count; sample++)
        {
            double t = (double)sample / SampleRate;
            double u = t / duration;
            double p = Math.Tau * t * color;
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2.0 - 1.0;
            double raw = kind switch
            {
                4 => Math.Sin(p * (115 - 30 * u)) * 1.05 * Math.Exp(-u * 5)
                    + noise * Math.Exp(-t * 75) * 0.9
                    + Math.Sin(p * 1810) * Math.Exp(-Math.Abs(t - 0.06) * 125) * 0.5,
                5 => (Math.Sin(p * (310 - 120 * u)) + Math.Sin(p * 620) * 0.42
                    + Math.Sin(p * 1240) * 0.22) * Math.Exp(-u * 4),
                6 => (Math.Sin(p * 190) + Math.Sin(p * 380) * 0.55 + Math.Sin(p * 762) * 0.25)
                    * (0.7 + Math.Cos(p * 24) * 0.3) * Math.Exp(-u * 4.5),
                7 => Math.Sin(p * (510 + 950 * u)) * Math.Exp(-u * 4)
                    + noise * Math.Exp(-t * 90) * 0.4,
                8 => (noise * 1.15 + Math.Sin(p * 88) * 0.65)
                    * (0.72 + Math.Sin(p * 43) * 0.28) * Math.Exp(-u * 4),
                9 => Math.Sin(p * (78 - 28 * u)) * 1.15 * Math.Exp(-u * 4)
                    + Math.Sin(p * 236) * Math.Exp(-u * 8) * 0.5 + noise * Math.Exp(-t * 32) * 0.65,
                10 => (noise * 0.8 + Math.Sin(p * (3100 - 1800 * u)) * 0.85)
                    * (Math.Exp(-t * 100) + Math.Exp(-Math.Abs(t - 0.047) * 115) * 0.7
                        + Math.Exp(-Math.Abs(t - 0.092) * 130) * 0.45),
                11 => (Math.Sin(p * (360 + 280 * u)) * 0.8 + Math.Sin(p * (1090 + 680 * u)) * 0.35
                    + noise * 0.25) * (0.66 + Math.Sin(p * 64) * 0.34) * Math.Exp(-u * 3.8),
                12 => (Math.Sin(p * (122 - 62 * u)) + Math.Sin(p * 173) * 0.45)
                    * (0.7 + Math.Cos(p * 9) * 0.3) * Math.Exp(-u * 3.7),
                13 => Math.Sin(p * 690) * Math.Exp(-t * 33) * 0.9
                    + Math.Sin(p * 647) * Math.Exp(-Math.Abs(t - 0.10) * 28) * 0.7
                    + Math.Sin(p * 606) * Math.Exp(-Math.Abs(t - 0.20) * 32) * 0.4,
                14 => (Math.Sin(p * (430 + 140 * Math.Sin(u * Math.PI))) * 0.9
                    + Math.Sin(p * 891) * 0.4 + Math.Sin(p * 1347) * 0.2)
                    * (0.75 + Math.Sin(p * 17) * 0.25) * Math.Exp(-u * 3.6),
                15 => (Math.Sin(p * (280 + 190 * u)) * 0.7 + Math.Sin(p * (563 - 140 * u)) * 0.55
                    + noise * 0.22) * (0.65 + Math.Sin(p * 7) * 0.35) * Math.Exp(-u * 3.8),
                _ => (Math.Sin(p * (210 + kind * 117)) + Math.Sin(p * (419 + kind * 151)) * 0.4
                    + noise * 0.2) * Math.Exp(-u * 4),
            };
            // Activation carries a brief second geometric resonance, rather than copying the impact.
            if (eventKind == 2) raw += Math.Sin(p * (370 + kind * 47))
                * Math.Exp(-Math.Abs(t - duration * 0.25) * 45) * 0.35;
            double fade = Math.Min(t / 0.003, 1.0) * Math.Min((duration - t) / 0.020, 1.0);
            short value = sample == 0 || sample == count - 1 ? (short)0
                : (short)(Math.Tanh(raw) * 0.52 * fade * short.MaxValue);
            pcm[sample * 2] = (byte)(value & 0xff);
            pcm[sample * 2 + 1] = (byte)((value >> 8) & 0xff);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate, Stereo = false, Data = pcm,
        };
    }
}
