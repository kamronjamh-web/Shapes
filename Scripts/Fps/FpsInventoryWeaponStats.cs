namespace SuperGatchaRebuild.Fps;

/// <summary>Effective FPS values; fractional bonuses are averages and never advance combat carry.</summary>
public sealed record FpsInventoryWeaponStats
{
    public float Damage { get; init; }
    public string DamageLabel { get; init; } = "Impact damage";
    public string DamageHint { get; init; } = "Before critical hits and enemy-specific effects.";
    public float IntervalSeconds { get; init; }
    public float AttacksPerSecond => 1f / IntervalSeconds;
    public float AverageExtraOutput { get; init; }
    public float Range { get; init; }
    public float AreaScale { get; init; }
    public float DurationScale { get; init; }
    public float AveragePierce { get; init; }
    public float AverageBounces { get; init; }
    public float AverageRicochet { get; init; }
    public float AverageChains { get; init; }
    public float CritChancePercent { get; init; }
    public float CritDamageMultiplier { get; init; }
    public float CurrentPlayerHealth { get; init; }
    public float MaximumPlayerHealth { get; init; }
}
