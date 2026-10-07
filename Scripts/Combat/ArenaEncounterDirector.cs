using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const int ArenaAuthoredWaveCount = 10;
    private const float ArenaWaveDurationSeconds = 60f;
    private const float ArenaOverrunStartSeconds = ArenaAuthoredWaveCount * ArenaWaveDurationSeconds;
    private const float ArenaResponseSampleSeconds = 15f;
    private const float ArenaResponseGateNoticed = 32f;
    private const float ArenaResponseGateTracking = 90f;
    private const float ArenaResponseGateEscalation = 200f;
    private const float ArenaResponseGateOverclock = 360f;
    private const float ArenaResponseGateNeutralization = 460f;
    private const int ArenaEnemyKindCount = 5;

    private static readonly EnemyKind[] ArenaEnemySpawnOrder =
    {
        EnemyKind.Grunt,
        EnemyKind.Bulwark,
        EnemyKind.Charger,
        EnemyKind.Mortar,
        EnemyKind.Interceptor,
    };

    private static readonly ArenaWaveDefinition[] ArenaEncounterWaves = BuildArenaEncounterWaves();

    private readonly List<ArenaSpawnStream> _arenaSpawnStreams = new();
    private readonly HashSet<string> _arenaTriggeredEvents = new(StringComparer.Ordinal);
    private readonly int[] _arenaAmbientRemaining = new int[ArenaEnemyKindCount];

    private ArenaWaveDefinition? _arenaCurrentWave;
    private bool _arenaDirectorActive;
    private bool _arenaOverrunActive;
    private float _arenaOverrunTimer;
    private float _arenaOverrunCycleTimer;
    private float _arenaResponseMeter;
    private float _arenaLoadoutPowerScore;
    private float _arenaPerformanceChargeBonus;
    private float _arenaResponseSampleTimer;
    private float _arenaRelayCompletionTimer;
    private float _arenaCountermeasureTimer;
    private float _arenaAwarenessPulseTimer;
    private float _arenaAwarenessHudFlashTimer;
    private float _arenaAwarenessEventTimer;
    private int _arenaResponseTier;
    private int _arenaLastSampleKills;
    private float _arenaLastSampleHp;
    private Label _arenaAwarenessLabel = null!;

    private sealed class ArenaWaveDefinition
    {
        public int Wave = 1;
        public string Name = string.Empty;
        public int LiveCap = 8;
        public float StartCadence = 2f;
        public float EndCadence = 2f;
        public ArenaEnemyMix AmbientMix;
        public ArenaTimelineEvent[] Events = Array.Empty<ArenaTimelineEvent>();
    }

    private sealed class ArenaTimelineEvent
    {
        public float TimeSeconds;
        public string Banner = string.Empty;
        public ArenaEnemyMix SpawnMix;
        public float SpawnDuration;
        public int LiveCapBonus;
        public bool AllowResponseEscorts = true;
        public bool SpawnBoss;
        public BossKind BossKind;
        public bool DeployRelay;

        public static ArenaTimelineEvent Packet(float timeSeconds, ArenaEnemyMix mix, float duration, string banner = "", int liveCapBonus = 4, bool allowResponseEscorts = true)
        {
            return new ArenaTimelineEvent
            {
                TimeSeconds = timeSeconds,
                SpawnMix = mix,
                SpawnDuration = duration,
                Banner = banner,
                LiveCapBonus = liveCapBonus,
                AllowResponseEscorts = allowResponseEscorts,
            };
        }

        public static ArenaTimelineEvent Boss(float timeSeconds, BossKind bossKind, string banner)
        {
            return new ArenaTimelineEvent
            {
                TimeSeconds = timeSeconds,
                SpawnBoss = true,
                BossKind = bossKind,
                Banner = banner,
                AllowResponseEscorts = false,
            };
        }

        public static ArenaTimelineEvent Relay(float timeSeconds)
        {
            return new ArenaTimelineEvent
            {
                TimeSeconds = timeSeconds,
                DeployRelay = true,
                Banner = "OVERCLOCK RELAY DEPLOYED",
                AllowResponseEscorts = false,
            };
        }

        public string GetKey()
        {
            return $"{TimeSeconds:0.00}:{Banner}:{SpawnBoss}:{DeployRelay}:{SpawnMix.Total}";
        }
    }

    private readonly struct ArenaEnemyMix
    {
        public ArenaEnemyMix(int grunts, int bulwarks, int chargers, int mortars, int interceptors)
        {
            Grunts = Mathf.Max(0, grunts);
            Bulwarks = Mathf.Max(0, bulwarks);
            Chargers = Mathf.Max(0, chargers);
            Mortars = Mathf.Max(0, mortars);
            Interceptors = Mathf.Max(0, interceptors);
        }

        public int Grunts { get; }
        public int Bulwarks { get; }
        public int Chargers { get; }
        public int Mortars { get; }
        public int Interceptors { get; }
        public int Total => Grunts + Bulwarks + Chargers + Mortars + Interceptors;

        public static ArenaEnemyMix Of(int grunts, int bulwarks, int chargers, int mortars, int interceptors)
        {
            return new ArenaEnemyMix(grunts, bulwarks, chargers, mortars, interceptors);
        }

        public ArenaEnemyMix Add(ArenaEnemyMix other)
        {
            return new ArenaEnemyMix(
                Grunts + other.Grunts,
                Bulwarks + other.Bulwarks,
                Chargers + other.Chargers,
                Mortars + other.Mortars,
                Interceptors + other.Interceptors);
        }

        public ArenaEnemyMix ReplaceGrunts(int count, float chargerShare)
        {
            int replacements = Mathf.Clamp(count, 0, Grunts);
            int chargers = Mathf.RoundToInt(replacements * Mathf.Clamp(chargerShare, 0f, 1f));
            int interceptors = replacements - chargers;
            return new ArenaEnemyMix(
                Grunts - replacements,
                Bulwarks,
                Chargers + chargers,
                Mortars,
                Interceptors + interceptors);
        }

        public void CopyTo(int[] target)
        {
            Array.Clear(target, 0, target.Length);
            target[(int)EnemyKind.Grunt] = Grunts;
            target[(int)EnemyKind.Bulwark] = Bulwarks;
            target[(int)EnemyKind.Charger] = Chargers;
            target[(int)EnemyKind.Mortar] = Mortars;
            target[(int)EnemyKind.Interceptor] = Interceptors;
        }
    }

    private sealed class ArenaSpawnStream
    {
        public readonly int[] Remaining = new int[ArenaEnemyKindCount];
        public float SpawnInterval = 0.5f;
        public float SpawnTimer;
        public int LiveCap;

        public int TotalRemaining
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Remaining.Length; i++)
                {
                    total += Remaining[i];
                }

                return total;
            }
        }
    }

    private static ArenaWaveDefinition[] BuildArenaEncounterWaves()
    {
        return new[]
        {
            new ArenaWaveDefinition
            {
                Wave = 1,
                Name = "CALIBRATION",
                LiveCap = 8,
                StartCadence = 2.6f,
                EndCadence = 2.2f,
                AmbientMix = ArenaEnemyMix.Of(8, 0, 0, 0, 0),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(12f, ArenaEnemyMix.Of(4, 0, 0, 0, 0), 3f),
                    ArenaTimelineEvent.Packet(35f, ArenaEnemyMix.Of(4, 2, 0, 0, 0), 4f),
                    ArenaTimelineEvent.Packet(50f, ArenaEnemyMix.Of(4, 2, 0, 0, 0), 4f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 2,
                Name = "ACQUISITION",
                LiveCap = 10,
                StartCadence = 2.2f,
                EndCadence = 1.9f,
                AmbientMix = ArenaEnemyMix.Of(16, 4, 4, 0, 0),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(80f, ArenaEnemyMix.Of(2, 0, 2, 0, 0), 3.5f),
                    ArenaTimelineEvent.Packet(105f, ArenaEnemyMix.Of(0, 2, 2, 0, 0), 3.5f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 3,
                Name = "BREACH TEST",
                LiveCap = 14,
                StartCadence = 1.9f,
                EndCadence = 1.9f,
                AmbientMix = ArenaEnemyMix.Of(12, 4, 6, 0, 2),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(150f, ArenaEnemyMix.Of(14, 2, 8, 0, 0), 20f, "BREACH HORDE DETECTED", 8, false),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 4,
                Name = "CROSSFIRE MATRIX",
                LiveCap = 16,
                StartCadence = 1.6f,
                EndCadence = 1.6f,
                AmbientMix = ArenaEnemyMix.Of(12, 4, 6, 2, 2),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(195f, ArenaEnemyMix.Of(0, 2, 0, 4, 0), 4f),
                    ArenaTimelineEvent.Packet(220f, ArenaEnemyMix.Of(0, 0, 2, 0, 2), 4f),
                    ArenaTimelineEvent.Packet(232f, ArenaEnemyMix.Of(0, 2, 2, 2, 0), 4f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 5,
                Name = "ESCALATION PRIME",
                LiveCap = 16,
                StartCadence = 1.5f,
                EndCadence = 1.5f,
                AmbientMix = ArenaEnemyMix.Of(8, 6, 6, 8, 2),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(260f, ArenaEnemyMix.Of(0, 2, 2, 0, 2), 4f),
                    ArenaTimelineEvent.Packet(285f, ArenaEnemyMix.Of(0, 0, 0, 0, 0), 0f, "BOSS SIGNAL LOCKING", 0, false),
                    ArenaTimelineEvent.Packet(290f, ArenaEnemyMix.Of(0, 0, 2, 0, 2), 4f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 6,
                Name = "OVERSIGHT PROTOCOL",
                LiveCap = 10,
                StartCadence = 1.5f,
                EndCadence = 1.5f,
                AmbientMix = ArenaEnemyMix.Of(0, 0, 0, 0, 0),
                Events = new[]
                {
                    ArenaTimelineEvent.Boss(300f, BossKind.Overseer, "THE OVERSEER DESCENDS"),
                    ArenaTimelineEvent.Packet(315f, ArenaEnemyMix.Of(2, 0, 2, 0, 0), 4f),
                    ArenaTimelineEvent.Packet(325f, ArenaEnemyMix.Of(2, 0, 0, 2, 0), 4f),
                    ArenaTimelineEvent.Packet(340f, ArenaEnemyMix.Of(0, 2, 0, 2, 0), 4f),
                    ArenaTimelineEvent.Packet(350f, ArenaEnemyMix.Of(2, 2, 2, 0, 2), 5f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 7,
                Name = "REASSESSMENT",
                LiveCap = 18,
                StartCadence = 1.45f,
                EndCadence = 1.45f,
                AmbientMix = ArenaEnemyMix.Of(8, 10, 8, 10, 10),
                Events = new[]
                {
                    ArenaTimelineEvent.Relay(380f),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 8,
                Name = "PURSUIT LOOP",
                LiveCap = 22,
                StartCadence = 1.35f,
                EndCadence = 1.35f,
                AmbientMix = ArenaEnemyMix.Of(4, 8, 8, 4, 4),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(450f, ArenaEnemyMix.Of(4, 4, 12, 4, 8), 24f, "PURSUIT HORDE DETECTED", 10, false),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 9,
                Name = "SYSTEM COLLAPSE",
                LiveCap = 22,
                StartCadence = 1.25f,
                EndCadence = 1.25f,
                AmbientMix = ArenaEnemyMix.Of(4, 8, 8, 8, 10),
                Events = new[]
                {
                    ArenaTimelineEvent.Packet(495f, ArenaEnemyMix.Of(0, 2, 2, 0, 2), 4f),
                    ArenaTimelineEvent.Packet(520f, ArenaEnemyMix.Of(0, 2, 0, 2, 2), 4f),
                    ArenaTimelineEvent.Packet(530f, ArenaEnemyMix.Of(0, 0, 2, 2, 2), 4f, "TERMINAL WARNING", 4, false),
                },
            },
            new ArenaWaveDefinition
            {
                Wave = 10,
                Name = "TERMINAL JUDGMENT",
                LiveCap = 12,
                StartCadence = 1.3f,
                EndCadence = 1.3f,
                AmbientMix = ArenaEnemyMix.Of(0, 0, 0, 0, 0),
                Events = new[]
                {
                    ArenaTimelineEvent.Boss(540f, BossKind.Cube, "TERMINAL BOSS RELEASED"),
                    ArenaTimelineEvent.Packet(555f, ArenaEnemyMix.Of(2, 2, 2, 0, 0), 4f),
                    ArenaTimelineEvent.Packet(570f, ArenaEnemyMix.Of(2, 0, 2, 0, 2), 4f),
                    ArenaTimelineEvent.Packet(585f, ArenaEnemyMix.Of(0, 2, 0, 2, 2), 4f),
                    ArenaTimelineEvent.Packet(595f, ArenaEnemyMix.Of(0, 4, 4, 2, 0), 5f, "OVERRUN IMMINENT", 5, false),
                },
            },
        };
    }

    private void StartArenaDirector()
    {
        _arenaDirectorActive = true;
        _arenaOverrunActive = false;
        _arenaOverrunTimer = 0f;
        _arenaOverrunCycleTimer = 0f;
        _arenaResponseTier = 0;
        _arenaLoadoutPowerScore = CalculateArenaLoadoutPowerScore();
        _arenaResponseMeter = GetArenaInitialResponseMeter(_arenaLoadoutPowerScore);
        _arenaPerformanceChargeBonus = 0f;
        _arenaResponseSampleTimer = ArenaResponseSampleSeconds;
        _arenaLastSampleKills = _kills;
        _arenaLastSampleHp = IsInstanceValid(_player) ? _player.Hp : BaseMaxHp;
        _arenaRelayCompletionTimer = 0f;
        _arenaCountermeasureTimer = 24f;
        _arenaAwarenessPulseTimer = 0f;
        _arenaAwarenessHudFlashTimer = 0f;
        _arenaAwarenessEventTimer = 0f;
        _arenaSpawnStreams.Clear();
        _arenaTriggeredEvents.Clear();
        _waveNum = 1;
        _waveTimer = 0f;
        _spawnCd = 0f;
        BeginArenaWave(1, false);
    }

    private void TickArenaDirector(float dt)
    {
        if (!_arenaDirectorActive)
        {
            StartArenaDirector();
        }

        UpdateArenaResponse(dt);
        TickArenaCountermeasures(dt);
        TickArenaAwarenessVisualState(dt);

        if (_gameTimer >= ArenaOverrunStartSeconds)
        {
            if (!_arenaOverrunActive)
            {
                BeginArenaOverrun();
            }

            TickArenaOverrun(dt);
        }
        else
        {
            int expectedWave = Mathf.Clamp(Mathf.FloorToInt(_gameTimer / ArenaWaveDurationSeconds) + 1, 1, ArenaAuthoredWaveCount);
            if (expectedWave != _waveNum)
            {
                BeginArenaWave(expectedWave, true);
            }

            _waveTimer = _gameTimer - ((_waveNum - 1) * ArenaWaveDurationSeconds);
            TickArenaTimelineEvents();
            TickArenaAmbientSpawns(dt);
        }

        TickArenaSpawnStreams(dt);
        TickArenaRelayCompletion(dt);
        UpdateArenaWaveLeft();
    }

    private void BeginArenaWave(int wave, bool showBanner)
    {
        _arenaCurrentWave = wave >= 1 && wave <= ArenaEncounterWaves.Length ? ArenaEncounterWaves[wave - 1] : null;
        _waveNum = wave;
        _waveTimer = 0f;
        _spawnCd = 0f;
        RebuildWaveBalance(_waveNum);

        if (_challengePillar != null && IsInstanceValid(_challengePillar) && !_challengePillar.IsActivated)
        {
            ClearChallengePillar();
        }

        if (_arenaCurrentWave != null)
        {
            ApplyArenaResponseToMix(_arenaCurrentWave.AmbientMix, false).CopyTo(_arenaAmbientRemaining);
        }
        else
        {
            Array.Clear(_arenaAmbientRemaining, 0, _arenaAmbientRemaining.Length);
        }

        UpdateArenaWaveLeft();

        if (showBanner)
        {
            ShowWaveBanner(_waveNum);
        }
    }

    private void BeginArenaOverrun()
    {
        _arenaOverrunActive = true;
        _arenaCurrentWave = null;
        _arenaOverrunTimer = 0f;
        _arenaOverrunCycleTimer = 0f;
        _waveNum = ArenaAuthoredWaveCount + 1;
        _waveTimer = 0f;
        Array.Clear(_arenaAmbientRemaining, 0, _arenaAmbientRemaining.Length);
        RebuildWaveBalance(_waveNum);
        ShowStatusBanner("OVERRUN PROTOCOL: SURVIVE");
        TriggerArenaResponsePulse(Mathf.Max(_arenaResponseTier, 1));
        QueueArenaOverrunCycle();
    }

    private void TickArenaOverrun(float dt)
    {
        _arenaOverrunTimer += dt;
        _waveTimer = _arenaOverrunTimer;
        _arenaOverrunCycleTimer -= dt;
        if (_arenaOverrunCycleTimer <= 0f)
        {
            QueueArenaOverrunCycle();
        }
    }

    private void QueueArenaOverrunCycle()
    {
        int cycle = Mathf.Max(0, Mathf.FloorToInt(_arenaOverrunTimer / 30f));
        ArenaEnemyMix mix = ArenaEnemyMix.Of(2, 10, 10, 6, 8);

        int extraElitePairs = cycle / 2;
        if (extraElitePairs > 0)
        {
            mix = mix.Add(ArenaEnemyMix.Of(0, extraElitePairs, extraElitePairs, extraElitePairs / 2, extraElitePairs));
        }

        mix = ApplyArenaResponseToMix(mix, true);
        QueueArenaSpawnStream(mix, 30f, GetCurrentArenaLiveCap() + 2);

        if (cycle > 0 && cycle % 4 == 0)
        {
            QueueArenaSpawnStream(ApplyArenaResponseToMix(ArenaEnemyMix.Of(4, 4, 8, 2, 6), true), 18f, GetCurrentArenaLiveCap() + 8);
            ShowStatusBanner("OVERRUN HORDE INBOUND");
        }

        RebuildWaveBalance(_waveNum);
        _arenaOverrunCycleTimer += 30f;
    }

    private void TickArenaTimelineEvents()
    {
        if (_arenaCurrentWave == null)
        {
            return;
        }

        foreach (ArenaTimelineEvent timelineEvent in _arenaCurrentWave.Events)
        {
            if (_gameTimer < timelineEvent.TimeSeconds)
            {
                continue;
            }

            string eventKey = timelineEvent.GetKey();
            if (!_arenaTriggeredEvents.Add(eventKey))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(timelineEvent.Banner))
            {
                ShowStatusBanner(timelineEvent.Banner);
            }

            if (timelineEvent.SpawnBoss)
            {
                SpawnBossForWave(_waveNum);
            }

            if (timelineEvent.DeployRelay)
            {
                DeployArenaRelay();
            }

            if (timelineEvent.SpawnMix.Total > 0)
            {
                ArenaEnemyMix mix = ApplyArenaResponseToMix(timelineEvent.SpawnMix, timelineEvent.AllowResponseEscorts);
                QueueArenaSpawnStream(mix, timelineEvent.SpawnDuration, GetCurrentArenaLiveCap() + timelineEvent.LiveCapBonus);
            }
        }
    }

    private void TickArenaAmbientSpawns(float dt)
    {
        if (_arenaCurrentWave == null || GetRemainingEnemyCount(_arenaAmbientRemaining) <= 0)
        {
            return;
        }

        _spawnCd -= dt;
        _waveSpawnCadence = GetArenaDirectorAdjustedCadence(Mathf.Lerp(_arenaCurrentWave.StartCadence, _arenaCurrentWave.EndCadence, Mathf.Clamp(_waveTimer / ArenaWaveDurationSeconds, 0f, 1f)));

        int safety = 0;
        while (_spawnCd <= 0f && safety < 4 && GetRemainingEnemyCount(_arenaAmbientRemaining) > 0)
        {
            if (_enemies.Count >= GetCurrentArenaLiveCap())
            {
                _spawnCd = Mathf.Max(_spawnCd, -_waveSpawnCadence);
                break;
            }

            EnemyKind kind = PickRemainingEnemyKind(_arenaAmbientRemaining);
            SpawnEnemyForWaveKind(kind);
            _arenaAmbientRemaining[(int)kind] = Mathf.Max(0, _arenaAmbientRemaining[(int)kind] - 1);
            _spawnCd += _waveSpawnCadence;
            safety++;
        }
    }

    private void TickArenaSpawnStreams(float dt)
    {
        for (int i = _arenaSpawnStreams.Count - 1; i >= 0; i--)
        {
            ArenaSpawnStream stream = _arenaSpawnStreams[i];
            stream.SpawnTimer -= dt;

            int safety = 0;
            while (stream.SpawnTimer <= 0f && stream.TotalRemaining > 0 && safety < 6)
            {
                int cap = Mathf.Max(stream.LiveCap, GetCurrentArenaLiveCap());
                if (_enemies.Count >= cap)
                {
                    stream.SpawnTimer = Mathf.Max(stream.SpawnTimer, -stream.SpawnInterval);
                    break;
                }

                EnemyKind kind = PickRemainingEnemyKind(stream.Remaining);
                SpawnEnemyForWaveKind(kind);
                stream.Remaining[(int)kind] = Mathf.Max(0, stream.Remaining[(int)kind] - 1);
                stream.SpawnTimer += stream.SpawnInterval;
                safety++;
            }

            if (stream.TotalRemaining <= 0)
            {
                _arenaSpawnStreams.RemoveAt(i);
            }
        }
    }

    private void QueueArenaSpawnStream(ArenaEnemyMix mix, float duration, int liveCap)
    {
        if (mix.Total <= 0)
        {
            return;
        }

        ArenaSpawnStream stream = new()
        {
            SpawnInterval = duration <= 0f ? 0.05f : Mathf.Max(0.08f, duration / Mathf.Max(1, mix.Total)),
            SpawnTimer = 0f,
            LiveCap = Mathf.Max(1, liveCap),
        };
        mix.CopyTo(stream.Remaining);
        _arenaSpawnStreams.Add(stream);
        UpdateArenaWaveLeft();
    }

    private void DeployArenaRelay()
    {
        if (_challengePillar != null && IsInstanceValid(_challengePillar))
        {
            return;
        }

        Vector2 pos = GetChallengePillarSpawnPosition();
        _challengePillar = new ArenaChallengePillarActor();
        AddChild(_challengePillar);
        _challengePillar.Configure(pos);
        TriggerArenaInteractableSpawnCue(pos);
    }

    private bool TryActivateArenaDirectorChallengePillar()
    {
        if (!_arenaDirectorActive || _waveNum != 7)
        {
            return false;
        }

        QueueArenaSpawnStream(ApplyArenaResponseToMix(ArenaEnemyMix.Of(0, 2, 2, 2, 2), true), 8f, GetCurrentArenaLiveCap() + 8);
        _arenaRelayCompletionTimer = 12f;
        ShowStatusBanner("OVERCLOCK RELAY AWAKENED");
        TriggerArenaResponsePulse(Mathf.Max(_arenaResponseTier, 2));
        return true;
    }

    private void TickArenaRelayCompletion(float dt)
    {
        if (_arenaRelayCompletionTimer <= 0f)
        {
            return;
        }

        _arenaRelayCompletionTimer = Mathf.Max(0f, _arenaRelayCompletionTimer - dt);
        if (_arenaRelayCompletionTimer <= 0f)
        {
            CompleteChallengePillar();
        }
    }

    private void UpdateArenaResponse(float dt)
    {
        _arenaResponseSampleTimer -= dt;
        if (_arenaResponseSampleTimer <= 0f)
        {
            SampleArenaResponsePerformance();
            _arenaResponseSampleTimer += ArenaResponseSampleSeconds;
        }

        float chargeRate = GetArenaPassiveResponseRate(_arenaLoadoutPowerScore) + _arenaPerformanceChargeBonus;
        _arenaResponseMeter = Mathf.Max(0f, _arenaResponseMeter + Mathf.Max(0.002f, chargeRate) * dt);

        int targetTier = GetArenaResponseTierForMeter(_arenaResponseMeter, _arenaLoadoutPowerScore);
        if (targetTier > _arenaResponseTier)
        {
            for (int tier = _arenaResponseTier + 1; tier <= targetTier; tier++)
            {
                _arenaResponseTier = tier;
                TriggerArenaResponseTier(tier);
            }

            RebuildWaveBalance(_waveNum);
        }
    }

    private void SampleArenaResponsePerformance()
    {
        int killsGained = Mathf.Max(0, _kills - _arenaLastSampleKills);
        float expectedKills = Mathf.Lerp(4f, 12f, Mathf.Clamp(_gameTimer / ArenaOverrunStartSeconds, 0f, 1f));
        float killPace = expectedKills > 0f ? killsGained / expectedKills : 0f;
        float hpLostPct = 0f;
        if (IsInstanceValid(_player))
        {
            hpLostPct = Mathf.Max(0f, _arenaLastSampleHp - _player.Hp) / Mathf.Max(1f, _player.MaxHp);
            _arenaLastSampleHp = _player.Hp;
        }

        _arenaLoadoutPowerScore = Mathf.Lerp(_arenaLoadoutPowerScore, CalculateArenaLoadoutPowerScore(), 0.25f);
        float performanceScale = GetArenaPerformancePressureScale(_arenaLoadoutPowerScore);
        float performancePressure = ((killPace - 1f) * 0.09f - hpLostPct * 0.12f) * performanceScale;
        _arenaPerformanceChargeBonus = Mathf.Clamp(performancePressure, -0.015f, 0.045f);
        _arenaLastSampleKills = _kills;
    }

    private int GetArenaResponseTierForMeter(float meter, float loadoutPower)
    {
        int targetTier;
        if (meter >= ArenaResponseGateNeutralization)
        {
            targetTier = 5;
        }
        else if (meter >= ArenaResponseGateOverclock)
        {
            targetTier = 4;
        }
        else if (meter >= ArenaResponseGateEscalation)
        {
            targetTier = 3;
        }
        else if (meter >= ArenaResponseGateTracking)
        {
            targetTier = 2;
        }
        else if (meter >= ArenaResponseGateNoticed)
        {
            targetTier = 1;
        }
        else
        {
            targetTier = 0;
        }

        return Mathf.Min(targetTier, GetArenaLoadoutTierCap(loadoutPower));
    }

    private static float GetArenaInitialResponseMeter(float loadoutPower)
    {
        return Mathf.Pow(Mathf.Clamp(loadoutPower, 0f, 1f), 2.25f) * 90f;
    }

    private static float GetArenaPassiveResponseRate(float loadoutPower)
    {
        float clampedPower = Mathf.Clamp(loadoutPower, 0f, 1f);
        return 0.006f + Mathf.Pow(clampedPower, 2.10f) * 0.57f;
    }

    private static float GetArenaPerformancePressureScale(float loadoutPower)
    {
        float clampedPower = Mathf.Clamp(loadoutPower, 0f, 1f);
        return 0.10f + 0.60f * Mathf.Pow(clampedPower, 0.85f);
    }

    private static int GetArenaLoadoutTierCap(float loadoutPower)
    {
        float clampedPower = Mathf.Clamp(loadoutPower, 0f, 1f);
        if (clampedPower >= 0.90f)
        {
            return 5;
        }

        if (clampedPower >= 0.72f)
        {
            return 4;
        }

        if (clampedPower >= 0.52f)
        {
            return 3;
        }

        if (clampedPower >= 0.32f)
        {
            return 2;
        }

        return 1;
    }

    private static float GetArenaMinimumResponseMeterForTier(int tier)
    {
        return tier switch
        {
            1 => ArenaResponseGateNoticed,
            2 => ArenaResponseGateTracking,
            3 => ArenaResponseGateEscalation,
            4 => ArenaResponseGateOverclock,
            5 => ArenaResponseGateNeutralization,
            _ => 0f,
        };
    }

    public void AdvanceArenaAwarenessTier()
    {
        int nextTier = Mathf.Clamp(_arenaResponseTier + 1, 1, 5);
        if (nextTier == _arenaResponseTier)
        {
            return;
        }

        _arenaResponseMeter = Mathf.Max(_arenaResponseMeter, GetArenaMinimumResponseMeterForTier(nextTier));
        _arenaResponseTier = nextTier;
        TriggerArenaResponseTier(nextTier);

        if (_waveNum > 0)
        {
            RebuildWaveBalance(_waveNum);
        }
    }

    private void TriggerArenaResponseTier(int tier)
    {
        string banner = tier switch
        {
            1 => "THE MACHINE REACTS TO YOUR POWER",
            2 => "THREAT CLASSIFICATION ELEVATED",
            3 => "COUNTERMEASURES DEPLOYED",
            4 => "ARENA OVERCLOCK ENGAGED",
            _ => "NEUTRALIZATION PROTOCOL ENGAGED",
        };

        ShowStatusBanner(banner);
        TriggerArenaResponsePulse(tier);
        QueueArenaResponseCounterPacket(tier, true);
        _arenaAwarenessEventTimer = GetArenaAwarenessEventInterval(tier);
    }

    private void TriggerArenaResponsePulse(int tier)
    {
        if (!IsInstanceValid(_player))
        {
            return;
        }

        Color pulseColor = tier switch
        {
            1 => new Color(0.25f, 0.92f, 0.78f, 1f),
            2 => new Color(0.30f, 0.70f, 1f, 1f),
            3 => new Color(1f, 0.70f, 0.22f, 1f),
            4 => new Color(1f, 0.28f, 0.20f, 1f),
            _ => new Color(0.96f, 0.98f, 1f, 1f),
        };

        TriggerCubeArenaImpact(_player.Position, 0.55f + tier * 0.18f, 0f, pulseColor);

        int sweepCount = tier switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            _ => 6,
        };
        for (int sweepIndex = 0; sweepIndex < sweepCount; sweepIndex++)
        {
            Color sweepColor = LerpColor(pulseColor, Colors.White, sweepIndex == 0 ? 0f : 0.18f + sweepIndex * 0.04f);
            TriggerArenaPerimeterSweep(
                sweepColor,
                0.45f + tier * 0.12f + sweepIndex * 0.06f,
                1.05f + sweepIndex * 0.18f + tier * 0.04f);
        }

        SpawnArenaAwarenessScan(_player.Position, pulseColor, tier);
        SpawnArenaAwarenessLockGeometry(_player.Position, pulseColor, tier, intensified: true);
        ApplyCubeScreenShake(_player.Position, 999f, 0.10f + tier * 0.10f, 0.07f + tier * 0.06f);
        _arenaAwarenessPulseTimer = 3.2f;
        _arenaAwarenessHudFlashTimer = 3.0f;
    }

    private void SpawnArenaAwarenessScan(Vector2 origin, Color color, int tier)
    {
        float tierScale = 1f + tier * 0.22f;
        SpawnArenaGroundCue(origin, color, 0.95f, 8f * tierScale, 1.1f + tier * 0.30f, 20f + tier * 3f);
        SpawnArenaBeaconCue(origin, color, 0.72f, 2.4f + tier * 0.32f, 18f + tier * 2.4f, 1.6f + tier * 0.45f, 22f + tier * 4f);

        if (tier >= 2)
        {
            float radius = 12f + tier * 3.5f;
            int ringCount = tier >= 5 ? 8 : 4;
            for (int i = 0; i < ringCount; i++)
            {
                float angle = i * Mathf.Tau / ringCount + tier * 0.18f;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                SpawnArenaBeaconCue(origin + offset, color, 0.62f, 1.2f, 9f + tier * 1.5f, 0.75f + tier * 0.18f, 12f + tier * 2f);
            }
        }
    }

    private void TickArenaAwarenessVisualState(float dt)
    {
        _arenaAwarenessPulseTimer = Mathf.Max(0f, _arenaAwarenessPulseTimer - dt);
        _arenaAwarenessHudFlashTimer = Mathf.Max(0f, _arenaAwarenessHudFlashTimer - dt);

        if (_arenaResponseTier <= 0)
        {
            _arenaAwarenessEventTimer = 0f;
            return;
        }

        _arenaAwarenessEventTimer -= dt;
        if (_arenaAwarenessEventTimer > 0f)
        {
            return;
        }

        TriggerArenaAwarenessHeartbeat(_arenaResponseTier);
        _arenaAwarenessEventTimer = GetArenaAwarenessEventInterval(_arenaResponseTier);
    }

    private static float GetArenaAwarenessEventInterval(int tier)
    {
        return tier switch
        {
            1 => 16f,
            2 => 11.5f,
            3 => 8f,
            4 => 5.75f,
            _ => 4.25f,
        };
    }

    private void TriggerArenaAwarenessHeartbeat(int tier)
    {
        if (!IsInstanceValid(_player))
        {
            return;
        }

        Color color = GetArenaAwarenessColor();
        TriggerArenaPerimeterSweep(color, 0.18f + tier * 0.08f, 0.90f + tier * 0.08f);

        if (tier >= 2)
        {
            SpawnArenaAwarenessLockGeometry(_player.Position, color, tier, intensified: false);
        }

        if (tier >= 4)
        {
            TriggerCubeArenaImpact(_player.Position, 0.16f + tier * 0.06f, 0f, color);
        }

        if (tier >= 5)
        {
            ApplyCubeScreenShake(_player.Position, 999f, 0.08f, 0.05f);
        }
    }

    private void SpawnArenaAwarenessLockGeometry(Vector2 origin, Color color, int tier, bool intensified)
    {
        Vector2 forward = _playerVel.LengthSquared() > 0.25f
            ? _playerVel.Normalized()
            : new Vector2(Mathf.Cos(_gameTimer * 1.6f), Mathf.Sin(_gameTimer * 1.6f));
        Vector2 lateral = new(-forward.Y, forward.X);

        float forwardDistance = 8f + tier * 2.8f;
        float sideDistance = 7f + tier * 2.2f;
        float cueDuration = intensified ? 0.88f + tier * 0.10f : 0.56f + tier * 0.08f;
        float cueRadius = intensified ? 2.8f + tier * 0.40f : 1.9f + tier * 0.28f;
        float lightEnergy = intensified ? 1.15f + tier * 0.24f : 0.72f + tier * 0.16f;
        float lightRange = intensified ? 15f + tier * 2.4f : 11f + tier * 1.8f;

        SpawnArenaGroundCue(origin + forward * forwardDistance, color, cueDuration, cueRadius, lightEnergy, lightRange);
        SpawnArenaGroundCue(origin - forward * (forwardDistance * 0.55f), color, cueDuration * 0.92f, cueRadius * 0.82f, lightEnergy * 0.90f, lightRange * 0.90f);

        if (tier >= 2)
        {
            SpawnArenaGroundCue(origin + lateral * sideDistance, color, cueDuration, cueRadius * 0.88f, lightEnergy * 0.92f, lightRange);
            SpawnArenaGroundCue(origin - lateral * sideDistance, color, cueDuration, cueRadius * 0.88f, lightEnergy * 0.92f, lightRange);
        }

        if (tier >= 3)
        {
            SpawnArenaBeaconCue(origin + forward * forwardDistance, color, cueDuration, 1.2f + tier * 0.10f, 10f + tier * 1.2f, 0.85f + tier * 0.12f, 12f + tier * 1.8f);
            SpawnArenaBeaconCue(origin - forward * (forwardDistance * 0.55f), color, cueDuration * 0.88f, 1.0f + tier * 0.10f, 8f + tier, 0.72f + tier * 0.10f, 10f + tier * 1.4f);
        }

        if (tier >= 4)
        {
            SpawnArenaAwarenessEdgeLances(color, tier, intensified ? 4 : 2);
        }

        if (tier >= 5)
        {
            SpawnArenaAwarenessEdgeLances(color, tier, intensified ? 8 : 4);
            TriggerArenaPerimeterSweep(LerpColor(color, Colors.White, 0.28f), 1.05f, 1.80f);
        }
    }

    private void SpawnArenaAwarenessEdgeLances(Color color, int tier, int count)
    {
        float baseAngle = _rng.RandfRange(0f, Mathf.Tau);
        float edgeDistance = Mathf.Min(ArenaHalfW, ArenaHalfH) - ArenaLayout.EnemySpawnMargin * 0.65f;

        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + i * Mathf.Tau / count;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 edgePos = ClampResolve(_player.Position + direction * edgeDistance, 1f);
            SpawnArenaBeaconCue(edgePos, color, 0.66f + tier * 0.06f, 1.3f + tier * 0.10f, 11f + tier * 1.4f, 0.88f + tier * 0.12f, 15f + tier * 2f);
        }
    }

    private void TickArenaCountermeasures(float dt)
    {
        if (_arenaResponseTier < 2)
        {
            return;
        }

        _arenaCountermeasureTimer -= dt;
        if (_arenaCountermeasureTimer > 0f)
        {
            return;
        }

        QueueArenaResponseCounterPacket(_arenaResponseTier, false);
        _arenaCountermeasureTimer = GetArenaCountermeasureInterval(_arenaResponseTier) + _rng.RandfRange(-1.5f, 1.5f);
    }

    private static float GetArenaCountermeasureInterval(int tier)
    {
        return tier switch
        {
            2 => 26f,
            3 => 19f,
            4 => 13f,
            _ => 8.5f,
        };
    }

    private void QueueArenaResponseCounterPacket(int tier, bool immediate)
    {
        ArenaEnemyMix mix = GetArenaCountermeasureMix(tier);
        if (mix.Total <= 0)
        {
            return;
        }

        QueueArenaSpawnStream(mix, immediate ? 4f : 7f, GetCurrentArenaLiveCap() + tier * 2);

        if (tier >= 3)
        {
            SpawnArenaContainmentLane(tier);
        }
    }

    private ArenaEnemyMix GetArenaCountermeasureMix(int tier)
    {
        float playerSpeed = _playerVel.Length();
        bool lowMovement = playerSpeed < BaseMoveSpeed * 0.22f;
        bool highKillPace = _arenaPerformanceChargeBonus > 0.022f;

        return tier switch
        {
            1 => ArenaEnemyMix.Of(0, 1, 2, 0, 0),
            2 when lowMovement => ArenaEnemyMix.Of(0, 2, 1, 3, 0),
            2 => ArenaEnemyMix.Of(0, 2, 2, 1, 1),
            3 when lowMovement => ArenaEnemyMix.Of(0, 3, 1, 4, 2),
            3 when highKillPace => ArenaEnemyMix.Of(0, 3, 2, 2, 3),
            3 => ArenaEnemyMix.Of(0, 2, 3, 2, 2),
            4 when lowMovement => ArenaEnemyMix.Of(0, 4, 2, 5, 3),
            4 when highKillPace => ArenaEnemyMix.Of(0, 4, 3, 4, 4),
            4 => ArenaEnemyMix.Of(0, 3, 4, 3, 4),
            5 when lowMovement => ArenaEnemyMix.Of(0, 5, 3, 6, 5),
            5 when highKillPace => ArenaEnemyMix.Of(0, 6, 4, 5, 6),
            5 => ArenaEnemyMix.Of(0, 4, 5, 5, 6),
            _ => ArenaEnemyMix.Of(0, 0, 0, 0, 0),
        };
    }

    private void SpawnArenaContainmentLane(int tier)
    {
        if (!IsInstanceValid(_player))
        {
            return;
        }

        Color color = GetArenaAwarenessColor();
        float spawnDistance = Mathf.Lerp(ArenaLayout.EnemySpawnMinDistance, ArenaLayout.EnemySpawnMaxDistance, 0.78f);
        int laneCount = tier >= 5 ? 8 : tier >= 4 ? 5 : 3;
        float baseAngle = _rng.RandfRange(0f, Mathf.Tau);

        for (int i = 0; i < laneCount; i++)
        {
            float angle = baseAngle + i * Mathf.Tau / laneCount;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 spawnPos = ClampResolve(_player.Position + direction * spawnDistance, 1f);

            SpawnArenaGroundCue(spawnPos, color, 0.72f, 4f + tier, 1f + tier * 0.25f, 14f + tier * 2f);
            if (tier >= 5)
            {
                SpawnArenaBeaconCue(spawnPos, color, 0.66f, 1.6f, 12f, 1.1f, 16f + tier * 2f);
            }

            if (_enemies.Count >= GetCurrentArenaLiveCap() + tier * 2)
            {
                continue;
            }

            EnemyKind kind = tier >= 5
                ? ((i % 3) switch
                {
                    0 => EnemyKind.Bulwark,
                    1 => EnemyKind.Mortar,
                    _ => EnemyKind.Interceptor,
                })
                : i % 2 == 0 ? EnemyKind.Bulwark : tier >= 4 ? EnemyKind.Interceptor : EnemyKind.Charger;
            SpawnArenaDirectedEnemy(kind, spawnPos);
        }
    }

    private void SpawnEnemyForWaveKind(EnemyKind kind)
    {
        SpawnArenaDirectedEnemy(kind, GetEnemySpawnPosition());
    }

    private void SpawnArenaDirectedEnemy(EnemyKind kind, Vector2 position)
    {
        EnemyActor enemy = new();
        AddChild(enemy);
        Vector2 pos = ClampResolve(position, 1f);
        enemy.Configure(kind, _enemyHpScale, _enemyDmgScale, pos, _player.Position);
        ApplyEquipmentEnemyStatModifiers(enemy);
        _enemies.Add(enemy);
        TriggerArenaEnemySpawnCue(pos, kind);
    }

    private ArenaEnemyMix ApplyArenaResponseToMix(ArenaEnemyMix mix, bool allowEliteEscort)
    {
        if (_arenaResponseTier <= 0 || mix.Total <= 0)
        {
            return mix;
        }

        int replacements = Mathf.FloorToInt(mix.Grunts * GetArenaResponseGruntReplacementShare());
        ArenaEnemyMix adjusted = mix.ReplaceGrunts(replacements, _arenaResponseTier >= 3 ? 0.55f : 0.75f);

        if (allowEliteEscort && _arenaResponseTier >= 2)
        {
            ArenaEnemyMix escort = _arenaResponseTier switch
            {
                2 => ArenaEnemyMix.Of(0, 1, 1, 0, 0),
                3 => ArenaEnemyMix.Of(0, 1, 1, 1, 0),
                4 => ArenaEnemyMix.Of(0, 2, 1, 1, 1),
                _ => ArenaEnemyMix.Of(0, 2, 2, 1, 2),
            };
            adjusted = adjusted.Add(escort);
        }

        return adjusted;
    }

    private float CalculateArenaLoadoutPowerScore()
    {
        float score = 0f;
        int slots = 5;

        for (int slotIndex = 0; slotIndex < slots; slotIndex++)
        {
            EquipmentItem? item = slotIndex < _equippedLoadout.Length ? _equippedLoadout[slotIndex] : null;
            if (item == null || !item.IsValid)
            {
                continue;
            }

            float rarityScore = GetArenaRarityPowerScore(item.Rarity?.Key);
            float shapeScore = GetArenaShapePowerScore(item);
            float levelScore = Mathf.Clamp((item.Level - 1f) / Mathf.Max(1f, EquipmentCatalogDefaults.MaxWeaponLevel - 1f), 0f, 1f);
            float substatScore = GetArenaSubstatBudgetScore(item);
            float modulatorScore = GetArenaModulatorScore(item);
            float essenceScore = item.HasEquippedEssence ? GetArenaModifierPowerScore(item.Essence?.Key) : 0f;

            score += rarityScore * 0.25f
                + shapeScore * 0.20f
                + levelScore * 0.15f
                + substatScore * 0.20f
                + modulatorScore * 0.15f
                + essenceScore * 0.05f;
        }

        return Mathf.Clamp(score / slots, 0f, 1f);
    }

    private static float GetArenaRarityPowerScore(string? rarityKey)
    {
        return EquipmentCatalogDefaults.NormalizeRarityKey(rarityKey) switch
        {
            "divine" => 1.00f,
            "mythic" => 0.88f,
            "legendary" => 0.74f,
            "rare" => 0.56f,
            "uncommon" => 0.34f,
            _ => 0.18f,
        };
    }

    private static float GetArenaShapePowerScore(EquipmentItem item)
    {
        float groupScore = item.Shape != null ? (int)item.Shape.ShapeGroup / 3f : 0f;
        float starScore = Mathf.Clamp((item.Stars - 1f) / 3f, 0f, 1f);
        return Mathf.Clamp(groupScore * 0.65f + starScore * 0.35f, 0f, 1f);
    }

    private static float GetArenaSubstatBudgetScore(EquipmentItem item)
    {
        float score = 0f;
        int count = 0;

        AddArenaSubstatBudget(item, item.Substats, ref score, ref count);
        for (int i = 0; i < item.Modulators.Count; i++)
        {
            AddArenaModulatorBudget(item, item.Modulators[i], ref score, ref count);
        }

        if (count == 0)
        {
            return 0f;
        }

        return Mathf.Clamp(score / count, 0f, 1f);
    }

    private static void AddArenaSubstatBudget(EquipmentItem item, IReadOnlyList<SubstatRoll> rolls, ref float score, ref int count)
    {
        for (int i = 0; i < rolls.Count; i++)
        {
            SubstatRoll roll = rolls[i];
            if (roll.Type == SubstatType.None)
            {
                continue;
            }

            float cap = EquipmentCatalogDefaults.GetSubstatCap(item.Rarity?.Key, roll.Type, Mathf.Max(1f, Mathf.Abs(roll.Value)));
            score += Mathf.Clamp(Mathf.Abs(roll.Value) / Mathf.Max(0.001f, cap), 0f, 1f);
            count++;
        }
    }

    private static void AddArenaModulatorBudget(EquipmentItem item, WeaponModulatorData modulator, ref float score, ref int count)
    {
        IReadOnlyList<WeaponModulatorEffectData> effects = modulator.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            if (!effects[i].TryConvertToSubstatRoll(out SubstatRoll roll))
            {
                continue;
            }

            AddArenaSubstatBudget(item, new[] { roll }, ref score, ref count);
        }
    }

    private static float GetArenaModulatorScore(EquipmentItem item)
    {
        int unlockedSlots = Mathf.Max(1, item.UnlockedModulatorSlots);
        float fillScore = Mathf.Clamp(item.Modulators.Count / (float)unlockedSlots, 0f, 1f);
        float qualityScore = item.Modulators.Count > 0 ? 0.75f : 0f;
        return Mathf.Clamp(fillScore * 0.70f + qualityScore * 0.30f, 0f, 1f);
    }

    private static float GetArenaModifierPowerScore(ModifierKey? key)
    {
        return key switch
        {
            ModifierKey.Prismatic => 1.0f,
            ModifierKey.Cursed => 0.85f,
            ModifierKey.Thorned => 0.85f,
            ModifierKey.Parasite => 0.85f,
            ModifierKey.Electric => 0.70f,
            ModifierKey.Unstable => 0.70f,
            ModifierKey.Shielded => 0.64f,
            ModifierKey.Dark => 0.60f,
            ModifierKey.Shiny => 0.56f,
            _ => 0f,
        };
    }

    private int GetCurrentArenaLiveCap()
    {
        int baseCap = _arenaOverrunActive
            ? 24 + Mathf.FloorToInt(_arenaOverrunTimer / 90f) * 2
            : _arenaCurrentWave?.LiveCap ?? 8;

        return baseCap + GetArenaResponseLiveCapBonus();
    }

    private float GetArenaDirectorAdjustedCadence(float baseCadence)
    {
        float overrunReduction = _arenaOverrunActive ? Mathf.Min(0.30f, Mathf.FloorToInt(_arenaOverrunTimer / 60f) * 0.05f) : 0f;
        return Mathf.Clamp(baseCadence - GetArenaResponseCadenceReduction() - overrunReduction, 0.18f, 4f);
    }

    private float GetArenaResponseGruntReplacementShare()
    {
        return _arenaResponseTier switch
        {
            1 => 0.12f,
            2 => 0.24f,
            3 => 0.38f,
            4 => 0.58f,
            5 => 0.82f,
            _ => 0f,
        };
    }

    private int GetArenaResponseLiveCapBonus()
    {
        return _arenaResponseTier switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 6,
            5 => 9,
            _ => 0,
        };
    }

    private float GetArenaResponseCadenceReduction()
    {
        return _arenaResponseTier switch
        {
            1 => 0.04f,
            2 => 0.11f,
            3 => 0.20f,
            4 => 0.31f,
            5 => 0.45f,
            _ => 0f,
        };
    }

    private float GetArenaResponseEnemyHpMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.04f,
            2 => 1.10f,
            3 => 1.20f,
            4 => 1.34f,
            5 => 1.52f,
            _ => 1f,
        };
    }

    private float GetArenaResponseEnemyDamageMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.02f,
            2 => 1.06f,
            3 => 1.12f,
            4 => 1.20f,
            5 => 1.30f,
            _ => 1f,
        };
    }

    private float GetArenaResponseBossHpMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.06f,
            2 => 1.14f,
            3 => 1.28f,
            4 => 1.46f,
            5 => 1.72f,
            _ => 1f,
        };
    }

    private float GetArenaResponseBossDamageMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.02f,
            2 => 1.07f,
            3 => 1.14f,
            4 => 1.24f,
            5 => 1.36f,
            _ => 1f,
        };
    }

    private float GetArenaOverrunEnemyHpMultiplier()
    {
        if (!_arenaOverrunActive)
        {
            return 1f;
        }

        return 1f + Mathf.FloorToInt(_arenaOverrunTimer / 30f) * 0.08f;
    }

    private float GetArenaOverrunEnemyDamageMultiplier()
    {
        if (!_arenaOverrunActive)
        {
            return 1f;
        }

        return 1f + Mathf.FloorToInt(_arenaOverrunTimer / 30f) * 0.05f;
    }

    private bool IsArenaBossWave(int wave)
    {
        return wave == 6 || wave == 10;
    }

    private string GetArenaWaveHudText()
    {
        return _arenaOverrunActive ? "OVR" : _waveNum.ToString();
    }

    private void UpdateArenaAwarenessHud()
    {
        if (!IsInstanceValid(_arenaAwarenessLabel))
        {
            return;
        }

        _arenaAwarenessLabel.Text = _arenaResponseTier switch
        {
            1 => "SCAN",
            2 => "TRACK",
            3 => "LOCK",
            4 => "OVRCLK",
            5 => "PURGE",
            _ => "QUIET",
        };

        Color color = _arenaResponseTier > 0 ? GetArenaAwarenessColor() : new Color(0.64f, 0.72f, 0.82f, 0.88f);
        if (_arenaAwarenessHudFlashTimer > 0f)
        {
            float flash = Mathf.Clamp(_arenaAwarenessHudFlashTimer / 2f, 0f, 1f);
            color = LerpColor(color, Colors.White, flash * 0.55f);
        }
        else if (_arenaResponseTier >= 3)
        {
            float strobe = 0.5f + 0.5f * Mathf.Sin(_gameTimer * (1.2f + _arenaResponseTier * 0.35f));
            color = LerpColor(color, Colors.White, strobe * 0.16f);
        }

        _arenaAwarenessLabel.AddThemeColorOverride("font_color", color);
    }

    private Color GetArenaAwarenessColor()
    {
        return _arenaResponseTier switch
        {
            1 => new Color(0.25f, 0.92f, 0.78f, 1f),
            2 => new Color(0.30f, 0.70f, 1f, 1f),
            3 => new Color(1f, 0.70f, 0.22f, 1f),
            4 => new Color(1f, 0.28f, 0.20f, 1f),
            5 => new Color(0.96f, 0.98f, 1f, 1f),
            _ => new Color(0.62f, 0.78f, 1f, 1f),
        };
    }

    private float GetArenaAwarenessLightingIntensity()
    {
        float tierIntensity = _arenaResponseTier switch
        {
            1 => 0.12f,
            2 => 0.26f,
            3 => 0.46f,
            4 => 0.70f,
            5 => 0.92f,
            _ => 0f,
        };
        float pulseIntensity = _arenaAwarenessPulseTimer > 0f ? Mathf.Clamp(_arenaAwarenessPulseTimer / 3.2f, 0f, 1f) * 0.82f : 0f;
        return Mathf.Clamp(tierIntensity + pulseIntensity, 0f, 1f);
    }

    private float GetArenaAwarenessXpMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.02f,
            2 => 1.08f,
            3 => 1.18f,
            4 => 1.34f,
            5 => 1.56f,
            _ => 1f,
        };
    }

    private float GetArenaAwarenessCoinChanceBonus()
    {
        return _arenaResponseTier switch
        {
            1 => 0.01f,
            2 => 0.03f,
            3 => 0.06f,
            4 => 0.10f,
            5 => 0.16f,
            _ => 0f,
        };
    }

    private float GetArenaAwarenessRareDropMultiplier()
    {
        return _arenaResponseTier switch
        {
            1 => 1.06f,
            2 => 1.18f,
            3 => 1.36f,
            4 => 1.62f,
            5 => 1.95f,
            _ => 1f,
        };
    }

    private int GetArenaAwarenessRewardTier()
    {
        return _arenaResponseTier;
    }

    private int GetRemainingEnemyCount(int[] counts)
    {
        int total = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            total += counts[i];
        }

        return total;
    }

    private EnemyKind PickRemainingEnemyKind(int[] remaining)
    {
        int total = GetRemainingEnemyCount(remaining);
        if (total <= 0)
        {
            return EnemyKind.Grunt;
        }

        int roll = _rng.RandiRange(1, total);
        foreach (EnemyKind kind in ArenaEnemySpawnOrder)
        {
            roll -= remaining[(int)kind];
            if (roll <= 0)
            {
                return kind;
            }
        }

        return EnemyKind.Grunt;
    }

    private void UpdateArenaWaveLeft()
    {
        int left = GetRemainingEnemyCount(_arenaAmbientRemaining);
        for (int i = 0; i < _arenaSpawnStreams.Count; i++)
        {
            left += _arenaSpawnStreams[i].TotalRemaining;
        }

        _waveLeft = left;
    }
}