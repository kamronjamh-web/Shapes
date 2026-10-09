using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Validation;

/// <summary>Checks real engine playback and pool behavior; listening quality remains a manual gate.</summary>
public static class WeaponPolishAudioChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly WeaponAttackType[] Types =
    {
        WeaponAttackType.Single, WeaponAttackType.Sawblade, WeaponAttackType.Laser, WeaponAttackType.Rift,
        WeaponAttackType.Mine, WeaponAttackType.Nova, WeaponAttackType.Orbital, WeaponAttackType.Homing,
        WeaponAttackType.FlameCone, WeaponAttackType.Mortar, WeaponAttackType.ChainLightning, WeaponAttackType.Drill,
        WeaponAttackType.VoidSingularity, WeaponAttackType.TemporalEcho, WeaponAttackType.WrathAura, WeaponAttackType.ShadowClone,
    };

    public static async Task Run(Node host, Action<bool, string> check, bool exportSamples = true)
    {
        Camera3D? previousCamera = host.GetViewport().GetCamera3D();
        var fixture = new Node3D { Name = "WeaponAudioValidationFixture" };
        host.AddChild(fixture);
        var camera = new Camera3D { Current = true };
        fixture.AddChild(camera);
        camera.MakeCurrent();
        var audio = new FpsWeaponPolishAudio();
        fixture.AddChild(audio);
        audio.SetProcess(false);
        AudioManager? manager = host.GetNodeOrNull<AudioManager>("/root/AudioManager");
        string? originalBus = manager?.SfxBusName;
        float originalSfx = manager?.SfxVolume ?? 1f;
        float originalMaster = manager?.MasterVolume ?? 1f;
        float originalMasterDb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master"));
        try
        {
            await Frame(host);
            var shots = Field<AudioStreamPlayer[]>(audio, "_shots");
            var impacts = Field<AudioStreamPlayer3D[]>(audio, "_impacts");
            var streams = (AudioStreamWav[])typeof(FpsWeaponPolishAudio)
                .GetField("_cachedStreams", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            check(shots.Length == 4 && impacts.Length == 8 && audio.GetChildCount() == 12,
                "Audio creates exactly four local and eight spatial pooled voices");
            check(streams.Length == 49, "Audio builds one shared 49-stream cache for sixteen shots, impacts, activations, and the legacy rift pulse");
            byte[][] buffers = streams.Select(stream => stream.Data).ToArray();
            for (int streamIndex = 0; streamIndex < streams.Length; streamIndex++)
            {
                AudioStreamWav stream = streams[streamIndex];
                check(stream.Format == AudioStreamWav.FormatEnum.Format16Bits && !stream.Stereo && stream.MixRate == 22050,
                    "Synthesized audio is mono signed 16-bit PCM at 22050Hz");
                byte[] pcm = buffers[streamIndex];
                float peak = 0f;
                bool finite = pcm.Length > 0 && pcm.Length % 2 == 0;
                for (int i = 0; i + 1 < pcm.Length; i += 2)
                {
                    float value = (short)(pcm[i] | pcm[i + 1] << 8) / 32767f;
                    finite &= float.IsFinite(value);
                    peak = Mathf.Max(peak, Mathf.Abs(value));
                }
                short first = (short)(pcm[0] | pcm[1] << 8);
                short last = (short)(pcm[^2] | pcm[^1] << 8);
                check(finite && peak > 0.1f && peak <= 0.521f && first == 0 && Math.Abs((int)last) <= 2,
                    "Actual cached PCM is audible, finite, unclipped, and faded at both endpoints");
            }
            check(Types.Length == Enum.GetValues<WeaponAttackType>().Length && Types.Distinct().Count() == 16,
                "Audio fixture covers every current weapon attack type exactly once");
            check(Enumerable.Range(0, streams.Length).All(i => Enumerable.Range(i + 1, streams.Length - i - 1)
                .All(j => !buffers[i].SequenceEqual(buffers[j]))),
                "All forty-nine weapon and event cues use distinct synthesized PCM buffers");
            // Golden PCM hashes from the nine accepted, exported first-pass WAVs.
            string[] acceptedHashes =
            {
                "D39C699C80019CD956E29FF1009E157AF839F8E36A4FBB0A1E783A82C536053C",
                "6775FAA0EE9374569285C37082714D027D6230510D1C7A283361D35C1ACA4DAF",
                "B39358C6C6863B6E766B1A38331A13FC62EB41B3AB3341EDB02ECA93084B22E4",
                "A7A22AEA5D89FA7605618361B28A4638AEEC74D275B18FE0D149DD9925AB5FA1",
                "35EC61EBE979B1E3A02E7453A236113F487BFEE7FB607CE92D371154D9D08E14",
                "A53CFAD07A587E64C8E2EBC0B4C744E1755A9EDB8844636BE74EBC23FE368911",
                "32D54D8DBC0AE8511631A446A973005930D4A575D33D66A376768CB0FB9B7AD6",
                "016D77C3310712F40A050E064E2F43BBC16360F754011BB98AF2ED07526D49C9",
                "BD2BED2E7E664F709C5D71B92BC7F633B3577EB7FB16BA8B16908A0C79B3B941",
            };
            check(Enumerable.Range(0, acceptedHashes.Length).All(i => Convert.ToHexString(SHA256.HashData(buffers[i])) == acceptedHashes[i]),
                "Original four shot and impact sounds and rift pulse remain byte-identical to accepted exports");

            for (int i = 0; i < Types.Length; i++)
            {
                audio.Clear();
                audio.PlayShot(Types[i], Vector3.Zero);
                int shotIndex = i < 4 ? i : 9 + (i - 4) * 2;
                int impactIndex = i < 4 ? i + 4 : 10 + (i - 4) * 2;
                check(shots.Any(voice => voice.Playing && ReferenceEquals(voice.Stream, streams[shotIndex])),
                    $"{Types[i]} starts its actual cached shot on a pooled player");
                audio.PlayImpact(Types[i], Vector3.Zero);
                await Frame(host);
                check(impacts.Any(voice => voice.Playing && ReferenceEquals(voice.Stream, streams[impactIndex])),
                    $"{Types[i]} starts its actual cached spatial impact");
                audio.Clear();
                audio.PlayActivation(Types[i], Vector3.Zero);
                await Frame(host);
                check(impacts.Any(voice => voice.Playing && ReferenceEquals(voice.Stream, streams[33 + i])),
                    $"{Types[i]} starts its distinct cached activation on the shared spatial pool");
            }
            audio.Clear();
            audio.PlayRiftPulse(Vector3.Zero);
            await Frame(host);
            check(impacts.Any(voice => voice.Playing && ReferenceEquals(voice.Stream, streams[8])),
                "Dimensional field pulse starts on the bounded spatial pool");

            audio.Clear();
            audio.PlayShot(WeaponAttackType.Single, Vector3.Zero);
            audio.PlayImpact(WeaponAttackType.Single, Vector3.Zero);
            await Frame(host);
            audio.SetPaused(true);
            check(shots.Where(voice => voice.Playing).All(voice => voice.StreamPaused)
                && impacts.Where(voice => voice.Playing).All(voice => voice.StreamPaused),
                "Explicit gameplay pause suspends active local and spatial audio");
            audio.Clear();
            audio.PlayShot(WeaponAttackType.Laser, Vector3.Zero);
            audio.PlayImpact(WeaponAttackType.Laser, Vector3.Zero);
            audio.PlayRiftPulse(Vector3.Zero);
            audio.PlayActivation(WeaponAttackType.Mine, Vector3.Zero);
            check(shots.All(voice => !voice.Playing) && impacts.All(voice => !voice.Playing),
                "Paused audio rejects all new playback requests");
            audio.SetPaused(false);
            audio.PlayShot(WeaponAttackType.Laser, Vector3.Zero);
            check(shots.Any(voice => voice.Playing), "Audio resumes after explicit gameplay pause");
            audio.Enabled = false;
            audio.PlayShot(WeaponAttackType.Single, Vector3.Zero);
            audio.PlayImpact(WeaponAttackType.Single, Vector3.Zero);
            audio.PlayRiftPulse(Vector3.Zero);
            audio.PlayActivation(WeaponAttackType.Mine, Vector3.Zero);
            check(shots.All(voice => !voice.Playing) && impacts.All(voice => !voice.Playing),
                "Disabling polish stops existing playback and rejects new playback");
            audio.Enabled = true;
            audio.Clear();
            for (int i = 0; i < 2000; i++)
            {
                audio.PlayShot(Types[i % Types.Length], Vector3.Zero);
                audio.PlayImpact(Types[i % Types.Length], Vector3.Zero);
                audio.PlayActivation(Types[i % Types.Length], Vector3.Zero);
            }
            await Frame(host);
            check(shots.Count(voice => voice.Playing) == 1 && impacts.Count(voice => voice.Playing) == 1,
                "Same-frame hit storms coalesce to one shot and one world impact");
            audio.Clear();
            for (int i = 0; i < 2000; i++)
            {
                audio._Process(0.003);
                audio.PlayShot(Types[i % Types.Length], Vector3.Zero);
                audio.PlayImpact(Types[i % Types.Length], Vector3.Zero);
                audio.PlayRiftPulse(Vector3.Zero);
                audio.PlayActivation(Types[i % Types.Length], Vector3.Zero);
            }
            await Frame(host);
            check(audio.GetChildCount() == 12 && shots.Count(voice => voice.Playing) <= 4
                && impacts.Count(voice => voice.Playing) <= 8,
                "Eight thousand dense audio events across sixteen weapons preserve fixed pool and active voice bounds");
            check(ReferenceEquals(streams, typeof(FpsWeaponPolishAudio)
                    .GetField("_cachedStreams", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))
                && shots.All(voice => voice.Stream == null || streams.Any(stream => ReferenceEquals(stream, voice.Stream)))
                && impacts.All(voice => voice.Stream == null || streams.Any(stream => ReferenceEquals(stream, voice.Stream))),
                "Dense playback reuses only cached audio resources");
            audio.Clear();
            audio.PlayImpact(WeaponAttackType.Single, new Vector3(100f, 0f, 0f));
            audio.PlayRiftPulse(new Vector3(100f, 0f, 0f));
            audio.PlayActivation(WeaponAttackType.Mine, new Vector3(100f, 0f, 0f));
            check(impacts.All(voice => !voice.Playing), "World audio culls events beyond its 48m listener range");

            check(manager != null, "Audio validation resolves the existing volume manager");
            if (manager != null)
            {
                manager.SfxBusName = "WeaponPolishValidationMissingBus";
                manager.SfxVolume = 0.25f;
                audio._Process(0.3);
                check(shots.All(voice => voice.Bus == "Master"
                    && Mathf.Abs(voice.VolumeDb - (-14f + Mathf.LinearToDb(0.25f))) < 0.01f),
                    "Missing SFX bus falls back to Master while preserving the SFX slider gain");
                manager.SfxVolume = 0f;
                audio._Process(0.3);
                check(shots.All(voice => voice.VolumeDb <= -90f) && impacts.All(voice => voice.VolumeDb <= -90f),
                    "Zero SFX volume silences both fallback voice pools");
                check(manager.MasterVolume == originalMaster
                    && AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")) == originalMasterDb,
                    "Audio routing leaves global Master settings and bus gain unchanged");
            }
            if (exportSamples)
            {
                string path = ProjectSettings.GlobalizePath("res://.tmp/weapon-polish-audio");
                Directory.CreateDirectory(path);
                string[] names = new string[streams.Length];
                string[] shapes = { "square", "circle", "triangle", "tesseract", "hexagon", "sphere", "cube", "pyramid",
                    "cone", "hyperphere", "pentachoron", "hypercone", "impossiblecube", "mobius", "klein", "boys" };
                names[8] = "rift-pulse";
                for (int i = 0; i < shapes.Length; i++)
                {
                    names[i < 4 ? i : 9 + (i - 4) * 2] = shapes[i] + "-shot";
                    names[i < 4 ? i + 4 : 10 + (i - 4) * 2] = shapes[i] + "-impact";
                    names[33 + i] = shapes[i] + "-activation";
                }
                for (int i = 0; i < streams.Length; i++)
                    check(streams[i].SaveToWav(Path.Combine(path, names[i] + ".wav")) == Error.Ok,
                        $"Export actual cached {names[i]} sample for listening review");
            }
        }
        finally
        {
            if (manager != null)
            {
                manager.SfxBusName = originalBus!;
                manager.SfxVolume = originalSfx;
            }
            audio.Clear();
            if (GodotObject.IsInstanceValid(previousCamera)) previousCamera!.MakeCurrent();
            fixture.QueueFree();
            await Frame(host);
        }
    }

    private static T Field<T>(object target, string name) => (T)typeof(FpsWeaponPolishAudio).GetField(name, Private)!.GetValue(target)!;
    private static async Task Frame(Node host)
    {
        // Spatial Play() begins during physics processing, after the PhysicsFrame signal.
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
