using Godot;

namespace SuperGatchaRebuild.Fps;

[GlobalClass]
public partial class FpsEnemyArchetypeSettings : Resource
{
	[ExportGroup("Combat")]
	[Export(PropertyHint.Range, "1,500,0.1")] public float MaxHealth { get; set; } = 90f;
	[Export(PropertyHint.Range, "0,25,0.05")] public float MoveSpeed { get; set; } = 5.6f;
	[Export(PropertyHint.Range, "0,60,0.1")] public float DesiredRange { get; set; } = 0f;
	[Export(PropertyHint.Range, "0,20,0.05")] public float AttackRange { get; set; } = 1.95f;
	[Export(PropertyHint.Range, "0,100,0.1")] public float AttackDamage { get; set; } = 16f;
	[Export(PropertyHint.Range, "0.05,10,0.01")] public float AttackCooldownSeconds { get; set; } = 0.95f;
	[Export(PropertyHint.Range, "0,100,0.1")] public float ProjectileDamage { get; set; } = 0f;
	[Export(PropertyHint.Range, "0,80,0.1")] public float ProjectileSpeed { get; set; } = 0f;

	[ExportGroup("Collision")]
	[Export(PropertyHint.Range, "0.1,4,0.01")] public float CollisionRadius { get; set; } = 0.55f;
	[Export(PropertyHint.Range, "0.1,6,0.01")] public float CollisionHeight { get; set; } = 1.2f;

	[ExportGroup("Visual")]
	[Export] public Color BodyColor { get; set; } = new Color(0.82f, 0.28f, 0.22f, 1f);
	[Export] public Color CoreColor { get; set; } = new Color(1f, 0.62f, 0.20f, 1f);

	public static FpsEnemyArchetypeSettings CreateBruiserDefaults()
	{
		return new FpsEnemyArchetypeSettings
		{
			MaxHealth = 90f,
			MoveSpeed = 5.6f,
			DesiredRange = 0f,
			AttackRange = 1.95f,
			AttackDamage = 16f,
			AttackCooldownSeconds = 0.95f,
			ProjectileDamage = 0f,
			ProjectileSpeed = 0f,
			CollisionRadius = 0.55f,
			CollisionHeight = 1.2f,
			BodyColor = new Color(0.82f, 0.28f, 0.22f, 1f),
			CoreColor = new Color(1f, 0.62f, 0.20f, 1f),
		};
	}

	public static FpsEnemyArchetypeSettings CreateStrikerDefaults()
	{
		return new FpsEnemyArchetypeSettings
		{
			MaxHealth = 62f,
			MoveSpeed = 4.5f,
			DesiredRange = 15.5f,
			AttackRange = 0f,
			AttackDamage = 0f,
			AttackCooldownSeconds = 1.35f,
			ProjectileDamage = 11f,
			ProjectileSpeed = 26f,
			CollisionRadius = 0.46f,
			CollisionHeight = 1.0f,
			BodyColor = new Color(0.28f, 0.72f, 0.98f, 1f),
			CoreColor = new Color(0.58f, 1f, 0.98f, 1f),
		};
	}
}
