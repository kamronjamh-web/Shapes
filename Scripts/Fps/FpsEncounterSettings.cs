using Godot;
using Godot.Collections;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsEncounterSettings : Resource
{
	[ExportGroup("Legacy Fallback")]
	[Export] public Array<FpsEnemySpawnSettings> EnemySpawns { get; set; } = CreateDefaultEnemySpawns();

	[ExportGroup("Wave Flow")]
	[Export] public Array<FpsEncounterWaveSettings> Waves { get; set; } = CreateDefaultWaves();
	[Export(PropertyHint.Range, "0,120,0.5")] public float InterWaveDelaySeconds { get; set; } = 2.2f;
	[Export] public bool AdvanceOnWaveClear { get; set; } = true;
	[Export] public bool ScaleArchetypesPerWave { get; set; } = true;

	[ExportGroup("Wave Step Scaling")]
	[Export(PropertyHint.Range, "0,3,0.01")] public float HealthMultiplierPerWave { get; set; } = 0.22f;
	[Export(PropertyHint.Range, "0,3,0.01")] public float DamageMultiplierPerWave { get; set; } = 0.15f;
	[Export(PropertyHint.Range, "0,3,0.01")] public float MoveSpeedMultiplierPerWave { get; set; } = 0.06f;
	[Export(PropertyHint.Range, "0,3,0.01")] public float AttackCadenceMultiplierPerWave { get; set; } = 0.05f;

	private static FpsEnemySpawnSettings CreateSpawn(string name, FpsEnemyRole role, Vector3 position)
	{
		return new FpsEnemySpawnSettings
		{
			SpawnName = name,
			Role = role,
			Position = position,
		};
	}

	private static Array<FpsEnemySpawnSettings> CreateDefaultEnemySpawns()
	{
		return new Array<FpsEnemySpawnSettings>
		{
			CreateSpawn("BruiserNorthWest", FpsEnemyRole.Bruiser, new Vector3(-18f, 0f, -18f)),
			CreateSpawn("BruiserNorthEast", FpsEnemyRole.Bruiser, new Vector3(18f, 0f, -16f)),
			CreateSpawn("BruiserWest", FpsEnemyRole.Bruiser, new Vector3(-14f, 0f, 8f)),
			CreateSpawn("StrikerEast", FpsEnemyRole.Striker, new Vector3(14f, 0f, 10f)),
			CreateSpawn("StrikerNorth", FpsEnemyRole.Striker, new Vector3(0f, 0f, -22f)),
		};
	}

	private static Array<FpsEncounterWaveSettings> CreateDefaultWaves()
	{
		return new Array<FpsEncounterWaveSettings>
		{
			new FpsEncounterWaveSettings
			{
				WaveName = "Breach Wave 1",
				EnemySpawns = new Array<FpsEnemySpawnSettings>
				{
					CreateSpawn("BruiserNorthWest", FpsEnemyRole.Bruiser, new Vector3(-18f, 0f, -18f)),
					CreateSpawn("BruiserNorthEast", FpsEnemyRole.Bruiser, new Vector3(18f, 0f, -16f)),
					CreateSpawn("BruiserWest", FpsEnemyRole.Bruiser, new Vector3(-14f, 0f, 8f)),
				},
			},
			new FpsEncounterWaveSettings
			{
				WaveName = "Breach Wave 2",
				HealthMultiplier = 1.05f,
				DamageMultiplier = 1.08f,
				EnemySpawns = new Array<FpsEnemySpawnSettings>
				{
					CreateSpawn("BruiserNorthWest", FpsEnemyRole.Bruiser, new Vector3(-18f, 0f, -18f)),
					CreateSpawn("BruiserNorthEast", FpsEnemyRole.Bruiser, new Vector3(18f, 0f, -16f)),
					CreateSpawn("StrikerEast", FpsEnemyRole.Striker, new Vector3(14f, 0f, 10f)),
					CreateSpawn("StrikerNorth", FpsEnemyRole.Striker, new Vector3(0f, 0f, -22f)),
				},
			},
			new FpsEncounterWaveSettings
			{
				WaveName = "Breach Wave 3",
				HealthMultiplier = 1.15f,
				DamageMultiplier = 1.12f,
				MoveSpeedMultiplier = 1.04f,
				AttackCadenceMultiplier = 1.08f,
				EnemySpawns = new Array<FpsEnemySpawnSettings>
				{
					CreateSpawn("BruiserNorthWest", FpsEnemyRole.Bruiser, new Vector3(-18f, 0f, -18f)),
					CreateSpawn("BruiserNorthEast", FpsEnemyRole.Bruiser, new Vector3(18f, 0f, -16f)),
					CreateSpawn("BruiserWest", FpsEnemyRole.Bruiser, new Vector3(-14f, 0f, 8f)),
					CreateSpawn("StrikerEast", FpsEnemyRole.Striker, new Vector3(14f, 0f, 10f)),
					CreateSpawn("StrikerNorth", FpsEnemyRole.Striker, new Vector3(0f, 0f, -22f)),
					CreateSpawn("StrikerCenter", FpsEnemyRole.Striker, new Vector3(0f, 0f, -10f)),
				},
			},
		};
	}
}
