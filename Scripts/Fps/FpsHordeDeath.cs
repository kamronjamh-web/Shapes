using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>The fatal hit retains excess damage and travel direction before the live slot is recycled.</summary>
public readonly record struct FpsHordeDeath(
	FpsHordeEnemySnapshot Enemy, Color BodyColor, Transform3D VisualTransform,
	Vector3 TravelDirection, float FatalDamage, float Overkill);
