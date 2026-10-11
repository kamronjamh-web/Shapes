using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Validation;

public static class ExplosionAudioChecks
{
    public static async Task Run(Node host, Action<bool, string> check, bool exportSamples)
    {
        Camera3D? previousCamera = host.GetViewport().GetCamera3D();
        var fixture = new Node3D { Name = "ExplosionAudioValidationFixture" };
        host.AddChild(fixture);
        var camera = new Camera3D(); fixture.AddChild(camera); camera.MakeCurrent();
        var audio = new FpsWeaponPolishAudio(); fixture.AddChild(audio); audio.SetProcess(false);
        var near = Voices(audio, "_explosionClose");
        var far = Voices(audio, "_explosionFar");
        AudioManager? manager = host.GetNodeOrNull<AudioManager>("/root/AudioManager");
        string? savedBus = manager?.SfxBusName;
        float savedSfx = manager?.SfxVolume ?? 1f;
        int captureBus = -1;
        int master = AudioServer.GetBusIndex("Master");
        bool masterMuted = AudioServer.IsBusMute(master);
        try
        {
            var settings = audio.ExplosionSettings;
            check(settings.MediumClose is AudioStreamWav wav && !wav.Stereo && wav.MixRate == 44100
                && wav.GetLength() > 7f && wav.GetLength() < 9f,
                "Supplied medium explosion imports as mono44100Hz full recording");
            check(audio.ClassifyExplosion(2.999f) == FpsExplosionSize.Small
                && audio.ClassifyExplosion(3f) == FpsExplosionSize.Medium
                && audio.ClassifyExplosion(7.999f) == FpsExplosionSize.Medium
                && audio.ClassifyExplosion(8f) == FpsExplosionSize.Large,
                "Explosion sound size uses exact3m and8m damage-radius boundaries");
            check(Enum.GetValues<FpsExplosionSize>().All(size =>
                ReferenceEquals(audio.ResolveExplosionStream(size, false), settings.MediumClose)
                && ReferenceEquals(audio.ResolveExplosionStream(size, true), settings.MediumClose)),
                "Six catalog slots resolve missing small/large to suppliedmedium without invented clips");
            var customSettings = new FpsExplosionAudioSettings
                { SmallClose = settings.MediumClose, MediumRadiusStart = 5f, LargeRadiusStart = 12f };
            var customAudio = new FpsWeaponPolishAudio { ExplosionSettings = customSettings };
            fixture.AddChild(customAudio); customAudio.SetProcess(false);
            check(ReferenceEquals(customAudio.ExplosionSettings, customSettings)
                && customAudio.ExplosionSettings.MediumClose == null
                && customAudio.ClassifyExplosion(4f) == FpsExplosionSize.Small
                && ReferenceEquals(customAudio.ResolveExplosionStream(FpsExplosionSize.Small, true), settings.MediumClose),
                "Initialization preserves customsmall-only catalog and radius thresholds without replacingresource");
            customAudio.QueueFree();
            audio.PlayExplosion(new Vector3(0, 0, -3), 4.4f);
            await Frame(host);
            check(near.Count(v => v.Playing) == 1 && far.All(v => !v.Playing)
                && audio.LastExplosionRadius == 4.4f && audio.LastExplosionSize == FpsExplosionSize.Medium,
                "Same-source medium near/far starts one spatial player with actualradius diagnostic");
            check(near.Where(v => v.Playing).All(v => v.AttenuationFilterCutoffHz == 2500f
                && v.AttenuationFilterDb == 0f),
                "Close explosion uses dry distance filter despite authored quiet gain");
            var current = near.First(v => v.Playing);
            float firstPitch = current.PitchScale;
            camera.Position = new Vector3(0, 0, 97); audio._Process(0);
            check(current.AttenuationFilterDb == -24f && current.PitchScale == firstPitch,
                "Moving listener100m away introduces far filter without changing active event pitch");
            camera.Position = Vector3.Zero; audio._Process(0);
            check(current.AttenuationFilterDb == 0f && current.PitchScale == firstPitch,
                "Moving listener close restores dry filter without changing active event pitch");
            var pitches = new List<float>();
            float lowerPitch = Mathf.Pow(2f, -settings.PitchVariationSemitones / 12f);
            float upperPitch = Mathf.Pow(2f, settings.PitchVariationSemitones / 12f);
            bool matchedPairs = true;
            for (int i = 0; i < 32; i++)
            {
                audio.Clear(); audio.PlayExplosion(new Vector3(0, 0, -3), 4.4f);
                int index = Array.FindIndex(near, v => v.Playing);
                float pitch = near[index].PitchScale; pitches.Add(pitch);
                matchedPairs &= pitch == far[index].PitchScale;
            }
            check(pitches.All(p => p >= lowerPitch - .000001f && p <= upperPitch + .000001f)
                && pitches.Max() - pitches.Min() > .04f,
                "Repeated explosions choose varied pitches inside configured semitone bounds");
            check(matchedPairs, "Every explosion pair shares one sampled pitch, including inactive sameclipfar player");
            // Let the last Play request reach the audio server before checking
            // immediate pause of an active voice (pitch selection above is synchronous).
            await Frame(host);
            check(near.Concat(far).All(v => v.MaxDb == v.VolumeDb)
                && near.Where(v => v.Playing).All(v => v.MaxDb == settings.VolumeDb),
                "Explosion distancegain cap preserves authored level at closestlistener positions");
            audio.SetPaused(true);
            check(near.Where(v => v.Playing).All(v => v.StreamPaused), "Pause immediately suspends explosion audio");
            long count = audio.ExplosionPlaybackCount;
            audio.PlayExplosion(Vector3.Zero, 4f);
            check(count == audio.ExplosionPlaybackCount, "Paused explosion requests are rejected");
            audio.SetPaused(false); audio.Clear();
            audio.PlayExplosion(Vector3.Zero, float.NaN); audio.PlayExplosion(Vector3.Zero, 0);
            audio.PlayExplosion(new Vector3(float.PositiveInfinity, 0, 0), 4f);
            audio.PlayExplosion(new Vector3(0, 0, -200), 4f);
            check(count == audio.ExplosionPlaybackCount, "Nonfinite/nonpositive/out-of-range explosion requests are rejected");
            for (int i = 0; i < 2000; i++) audio.PlayExplosion(Vector3.Zero, 4f);
            check(audio.ExplosionPlaybackCount == count + 1, "Same-frame explosion storm coalesces to one accepted event");
            for (int i = 0; i < 12; i++) { audio._Process(.1); audio.PlayExplosion(Vector3.Zero, 4f); }
            await Frame(host);
            check(audio.GetChildCount() == 20 && near.Count(v => v.Playing) <= 4 && far.All(v => !v.Playing),
                "Explosion storm preserves fixed four-pair pool without sameclip doublesumming");
            audio.Enabled = false;
            check(near.Concat(far).All(v => !v.Playing), "Disabling cosmetics stops all explosion players");
            audio.Enabled = true;
            var distinct = new FpsExplosionAudioSettings
            {
                MediumClose = settings.MediumClose,
                MediumFar = new AudioStreamWav { Data = new byte[22050], MixRate = 22050, Format = AudioStreamWav.FormatEnum.Format16Bits },
            };
            audio.ExplosionSettings = distinct;
            audio.Clear(); audio.PlayExplosion(new Vector3(0, 0, -62), 4f); await Frame(host);
            var a = near.First(v => v.Playing); var b = far.First(v => v.Playing);
            check(Mathf.Abs(a.VolumeDb - b.VolumeDb) < .001f,
                "Distinct near/far catalog clips crossfade continuously at band midpoint");
            check(a.MaxDb == a.VolumeDb && b.MaxDb == b.VolumeDb,
                "Both distinctclip crossfade gains also cap inverse-distance amplification");
            check(a.PitchScale == b.PitchScale, "Distinct close/far clips share event pitch to avoid crossfade detuning");
            camera.Position = new Vector3(0, 0, -59); audio._Process(0);
            check(b.VolumeDb < a.VolumeDb - 60f, "Moving listener close updates active crossfade without restarting explosion");
            check(a.MaxDb == a.VolumeDb && b.MaxDb == b.VolumeDb && b.MaxDb < -90f,
                "Close-listener crossfade keeps far endpoint capped near silence");
            audio.Clear(); camera.Position = Vector3.Zero; audio.ExplosionSettings = settings;
            if (manager != null)
            {
                manager.SfxBusName = "ExplosionValidationMissingBus"; manager.SfxVolume = 0;
                audio._Process(.3); audio.PlayExplosion(Vector3.Zero, 4f);
                check(near.Concat(far).All(v => v.Bus == "Master" && v.VolumeDb <= -90f
                    && v.MaxDb == v.VolumeDb && v.MaxDb <= -90f),
                    "Explosion pool preserves mutedSFX gain when dedicatedbus is missing");
                audio.Clear();
            }
            if (exportSamples && DisplayServer.GetName() != "headless" && manager != null)
            {
                AudioServer.AddBus(); captureBus = AudioServer.BusCount - 1;
                AudioServer.SetBusName(captureBus, "ExplosionValidationCapture");
                AudioServer.SetBusSend(captureBus, "Master");
                using var capture = new AudioEffectCapture { BufferLength = 12f };
                AudioServer.AddBusEffect(captureBus, capture);
                manager.SfxBusName = "ExplosionValidationCapture"; manager.SfxVolume = 1f;
                AudioServer.SetBusMute(master, true); // Capture upstream; do not play into OS speakers.
                audio._Process(.3);
                // Clone configuration only; never mutate the shared editor resource.
                using var captureSettings = (FpsExplosionAudioSettings)settings.Duplicate(false);
                captureSettings.PitchVariationSemitones = 0f;
                audio.ExplosionSettings = captureSettings;
                double closeRms = await Capture(host, audio, capture, 3f, "medium-close-actual.wav", check);
                double farRms = await Capture(host, audio, capture, 90f, "medium-far-actual.wav", check);
                check(closeRms > .00001 && farRms > 0 && farRms < closeRms * .7,
                    $"Actual Godot far output is quieter than close (RMS {closeRms:F6}/{farRms:F6})");
                using AudioStreamWav tone = BuildFilterTone();
                captureSettings.MediumClose = captureSettings.MediumFar = tone;
                double nearRatio = await CaptureFilterRatio(host, audio, capture, 3f);
                double farRatio = await CaptureFilterRatio(host, audio, capture, 100f);
                check(nearRatio > .65 && nearRatio < 1.35 && farRatio < nearRatio * .35,
                    $"Actual500/8000Hz normalized amplitude ratio proves dryclose/muffledfar ({nearRatio:F4}/{farRatio:F4})");
                captureSettings.VolumeDb -= 18f;
                double quietRatio = await CaptureFilterRatio(host, audio, capture, 3f);
                check(Math.Abs(quietRatio / nearRatio - 1) < .08,
                    $"Quietclose explosion preserves brightness independently of authored gain ({quietRatio:F4})");
                audio.Clear(); audio.ExplosionSettings = settings;
                AudioServer.RemoveBusEffect(captureBus, 0);
            }
            else GD.Print("LIMIT: actual explosion mix capture requires native audio and sample export; no listening-quality claim.");
        }
        finally
        {
            audio.Clear();
            if (manager != null) { manager.SfxBusName = savedBus!; manager.SfxVolume = savedSfx; }
            AudioServer.SetBusMute(master, masterMuted);
            if (captureBus >= 0) AudioServer.RemoveBus(captureBus);
            if (GodotObject.IsInstanceValid(previousCamera)) previousCamera!.MakeCurrent();
            fixture.QueueFree(); await Frame(host);
        }
    }

    private static AudioStreamWav BuildFilterTone()
    {
        const int rate = 48000, frames = rate * 2;
        byte[] pcm = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            double envelope = Math.Min(1, Math.Min(i, frames - 1 - i) / 480.0);
            double sample = .15 * envelope * (Math.Sin(2 * Math.PI * 500 * i / rate) + Math.Sin(2 * Math.PI * 8000 * i / rate));
            short value = (short)Math.Round(sample * 32767);
            pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
        }
        return new AudioStreamWav { Data = pcm, Stereo = false, MixRate = rate, Format = AudioStreamWav.FormatEnum.Format16Bits };
    }

    private static async Task<double> CaptureFilterRatio(Node host, FpsWeaponPolishAudio audio, AudioEffectCapture capture, float distance)
    {
        audio.Clear();
        // Drain stopped previous playback before clearing the capture ring.
        await Frame(host); await Frame(host);
        capture.ClearBuffer(); audio.PlayExplosion(new Vector3(0, 0, -distance), 4f);
        List<Vector2> samples = new(); Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < .85)
        {
            await Frame(host);
            int available = capture.GetFramesAvailable();
            if (available > 0) samples.AddRange(capture.GetBuffer(available));
        }
        double rate = AudioServer.GetMixRate();
        int count = Math.Min(samples.Count, (int)(rate * .2));
        if (count < rate * .15) throw new InvalidOperationException("Insufficient actualengine samples for spectral regression");
        double Amplitude(double frequency)
        {
            double real = 0, imaginary = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 stereo = samples[samples.Count - count + i];
                if (!stereo.IsFinite()) throw new InvalidOperationException("Nonfinite actual filter capture");
                double sample = (stereo.X + stereo.Y) * .5;
                double angle = 2 * Math.PI * frequency * i / rate;
                real += sample * Math.Cos(angle); imaginary += sample * Math.Sin(angle);
            }
            return Math.Sqrt(real * real + imaginary * imaginary) / count;
        }
        double low = Amplitude(500), high = Amplitude(8000);
        if (low < .000001) throw new InvalidOperationException("Actual filter capture has no audible reference tone");
        return high / low;
    }

    private static async Task<double> Capture(Node host, FpsWeaponPolishAudio audio, AudioEffectCapture capture,
        float distance, string name, Action<bool, string> check)
    {
        audio.Clear(); capture.ClearBuffer();
        audio.PlayExplosion(new Vector3(0, 0, -distance), 4f);
        List<Vector2> samples = new();
        Stopwatch elapsed = Stopwatch.StartNew();
        double duration = Math.Min(10, audio.ExplosionSettings.MediumClose!.GetLength() + .3);
        while (elapsed.Elapsed.TotalSeconds < duration)
        {
            await Frame(host);
            int available = capture.GetFramesAvailable();
            if (available > 0) samples.AddRange(capture.GetBuffer(available));
        }
        byte[] pcm = new byte[samples.Count * 4];
        double energy = 0; bool finite = true;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 sample = samples[i]; finite &= sample.IsFinite();
            energy += sample.X * sample.X + sample.Y * sample.Y;
            for (int channel = 0; channel < 2; channel++)
            {
                short value = (short)Mathf.RoundToInt(Mathf.Clamp(channel == 0 ? sample.X : sample.Y, -1f, 1f) * 32767f);
                int offset = i * 4 + channel * 2;
                pcm[offset] = (byte)value; pcm[offset + 1] = (byte)(value >> 8);
            }
        }
        check(finite && samples.Count > AudioServer.GetMixRate(), $"{name}: captures finite actual engine stereo mix");
        string directory = ProjectSettings.GlobalizePath("res://.tmp/weapon-polish-audio"); Directory.CreateDirectory(directory);
        using var wav = new AudioStreamWav { Data = pcm, Stereo = true, MixRate = (int)AudioServer.GetMixRate(), Format = AudioStreamWav.FormatEnum.Format16Bits };
        check(wav.SaveToWav(Path.Combine(directory, name)) == Error.Ok, $"{name}: exports actualGodot filtered output for listeningreview");
        return Math.Sqrt(energy / Math.Max(1, samples.Count * 2));
    }

    private static AudioStreamPlayer3D[] Voices(FpsWeaponPolishAudio audio, string name)
        => (AudioStreamPlayer3D[])typeof(FpsWeaponPolishAudio).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(audio)!;
    private static async Task Frame(Node host)
    {
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
