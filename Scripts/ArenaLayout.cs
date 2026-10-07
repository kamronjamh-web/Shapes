using Godot;

namespace SuperGatchaRebuild;

public static class ArenaLayout
{
	public const float ArenaWidth = 800f;
	public const float ArenaHeight = 800f;
	public const float HalfWidth = 400f;
	public const float HalfHeight = 400f;

	public const float WallThickness = 4f;
	public const float WallHeight = 8f;

	public const float PillarMinSize = 2.5f;
	public const float PillarMaxSize = 5.5f;
	public const float PillarMinHeight = 4f;
	public const float PillarMaxHeight = 12f;

	public const float PlayerStartX = 0f;
	public const float PlayerStartZ = 0f;

	public const float EnemySpawnMargin = 2f;
	public const float EnemySpawnMinDistance = 60f;
	public const float EnemySpawnMaxDistance = 110f;
	public const float InteractableSpawnMargin = 10f;
	public const float BossMoveMargin = 5f;

	public static readonly Vector2[] PillarPositions =
	{
		new(-120f, -120f), new(120f, -120f), new(-120f, 120f), new(120f, 120f),
		new(0f, -200f), new(0f, 200f), new(-200f, 0f), new(200f, 0f),
		new(-240f, -240f), new(240f, -240f), new(-240f, 240f), new(240f, 240f),
		new(-80f, 0f), new(80f, 0f), new(0f, -80f), new(0f, 80f),
		new(-180f, 100f), new(180f, -100f), new(100f, 180f), new(-100f, -180f),
		new(-300f, -50f), new(300f, 50f), new(-50f, 300f), new(50f, -300f),
		new(-160f, -280f), new(160f, 280f), new(280f, -160f), new(-280f, 160f),
		new(0f, 0f), new(-350f, -350f), new(350f, 350f), new(-350f, 350f), new(350f, -350f),
		new(-60f, 250f), new(60f, -250f), new(250f, 60f), new(-250f, -60f),
		new(-180f, -180f), new(180f, 180f), new(0f, 350f), new(0f, -350f), new(-350f, 0f), new(350f, 0f),
	};
}
