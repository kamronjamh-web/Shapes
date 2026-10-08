using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Explicit old map fixture keeps combat regressions independent of the new default fort.
internal static class LegacyHillFixture
{
	public static void Configure(FpsVerticalSlice map)
	{
		if (map.HillMapSettings == null) return;
		map.HillMapSettings = (FpsHillMapSettings)map.HillMapSettings.Duplicate(true);
		FpsHillMapSettings hill = map.HillMapSettings;
		hill.FortificationsEnabled = false;
		hill.PlayableSize = 200f; hill.SummitHeight = 8f; hill.SummitHalfSize = 16f;
		hill.HillFootHalfSize = 65f; hill.RampWidth = 24f;
		hill.RoomHalfSize = 12f; hill.RoomWallHeight = 7f; hill.DoorWidth = 8f; hill.DoorHeight = 6f;
		hill.RoofEnabled = true; hill.EastDoorEnabled = false; hill.SouthDoorEnabled = false; hill.WestDoorEnabled = false;
		if (map.HordeSettings != null)
		{
			map.HordeSettings = (FpsHordeSettings)map.HordeSettings.Duplicate(true);
			map.HordeSettings.MapExtent = 200f;
		}
		if (map.ArenaLayoutSettings != null)
		{
			map.ArenaLayoutSettings = (FpsArenaLayoutSettings)map.ArenaLayoutSettings.Duplicate(true);
			map.ArenaLayoutSettings.HalfWidth = map.ArenaLayoutSettings.HalfDepth = 100f;
			map.ArenaLayoutSettings.PlayerSpawnPosition = new Vector3(0f, 8.1f, -5f);
		}
		if (map.DefenseSettings != null)
		{
			map.DefenseSettings = (FpsDefenseSettings)map.DefenseSettings.Duplicate(true);
			var lanes = new Godot.Collections.Array<FpsDefenseLaneSettings>();
			for (int side = 0; side < 4; side++) lanes.Add(new FpsDefenseLaneSettings
			{
				Direction = (FpsLaneDirection)side, Enabled = side == 0, Width = 52f, Length = 90f,
			});
			map.DefenseSettings.Lanes = lanes;
			map.DefenseSettings.CorePosition = new Vector3(0f, 8f, 3f);
			map.DefenseSettings.ShopPosition = new Vector3(-7f, 8f, 5f);
		}
		if (map.EncounterSettings != null)
		{
			map.EncounterSettings = (FpsEncounterSettings)map.EncounterSettings.Duplicate(true);
			map.EncounterSettings.EnemySpawns = new();
			map.EncounterSettings.Waves = new()
			{
				new FpsEncounterWaveSettings
				{
					WaveName = "Legacy hill combat fixture", EnemySpawns = new()
					{
						new FpsEnemySpawnSettings { SpawnName = "NorthHorde", Role = FpsEnemyRole.Bruiser,
							Position = new Vector3(0f, 0f, -76f), Count = 100, FormationColumns = 18,
							FormationSpacing = 2.8f, Lane = FpsLaneDirection.North },
					},
				},
			};
		}
	}
}
