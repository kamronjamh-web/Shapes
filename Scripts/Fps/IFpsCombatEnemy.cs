using Godot;

namespace SuperGatchaRebuild.Fps;

// Combat targets keep their identity independently of their rendering or physics representation.
public interface IFpsCombatEnemy
{
	Vector3 GlobalPosition { get; set; }
	Vector3 HeadPosition { get; }
	float CollisionRadius { get; }
	float CollisionHeight { get; }
	Vector3 CollisionCenter { get; }
	float CurrentHealth { get; }
	float MaxHealth { get; }
	FpsEnemyRole Role { get; }
	bool IsAlive { get; }
	bool IsValid { get; }
	void ApplyDamage(float damage);
	void ApplyDamageFrom(float damage, Vector3 travelDirection) => ApplyDamage(damage);
	void MoveBy(Vector3 displacement);
}

public sealed class FpsHordeCombatEnemy : IFpsCombatEnemy
{
	public FpsHordeManager Manager { get; }
	public FpsHordeHandle Handle { get; }
	private FpsHordeEnemySnapshot _lastSnapshot;

	public FpsHordeCombatEnemy(FpsHordeManager manager, FpsHordeHandle handle)
	{
		Manager = manager;
		Handle = handle;
		manager.TryGet(handle, out _lastSnapshot);
	}

	private FpsHordeEnemySnapshot Snapshot
	{
		get
		{
			if (IsManagerValid && Manager.TryGet(Handle, out FpsHordeEnemySnapshot current)) _lastSnapshot = current;
			return _lastSnapshot;
		}
	}
	private bool IsManagerValid => GodotObject.IsInstanceValid(Manager) && !Manager.IsQueuedForDeletion();
	public Vector3 GlobalPosition { get => Snapshot.Position; set { if (IsManagerValid) Manager.TrySetPosition(Handle, value); } }
	public Vector3 HeadPosition => Snapshot.HeadPosition;
	public float CollisionRadius => Snapshot.Radius;
	public float CollisionHeight => Snapshot.CollisionHeight;
	public Vector3 CollisionCenter => GlobalPosition + Vector3.Up * CollisionHeight * 0.5f;
	public float CurrentHealth => IsAlive ? Snapshot.CurrentHealth : 0f;
	public float MaxHealth => Snapshot.MaxHealth;
	public FpsEnemyRole Role => Snapshot.Role;
	public bool IsAlive => IsManagerValid && Manager.TryGet(Handle, out FpsHordeEnemySnapshot current) && current.CurrentHealth > 0f;
	public bool IsValid => IsAlive;
	public void ApplyDamage(float damage) { if (IsManagerValid) Manager.ApplyDamage(Handle, damage); }
	public void ApplyDamageFrom(float damage, Vector3 travelDirection) { if (IsManagerValid) Manager.ApplyDamageFrom(Handle, damage, travelDirection); }
	public void MoveBy(Vector3 displacement) { if (IsManagerValid) Manager.TryMoveBy(Handle, displacement); }
}
