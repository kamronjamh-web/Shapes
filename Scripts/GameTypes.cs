namespace SuperGatchaRebuild;

public enum TeamId
{
    Player = 0,
    Monster = 1,
    Friendly = 2,
    Neutral = 3,
}

public enum EnemyKind
{
    Grunt = 0,
    Interceptor = 1,
    Bulwark = 2,
    Charger = 3,
    Mortar = 4,
}

public enum BossKind
{
    Monolith = 0,
    Overseer = 1,
    Cube = 2,
}

public enum BossPhase
{
    None = 0,
    Move = 1,
    Charging = 2,
    Annihilate = 3,
    Orbit = 4,
    LaserTelegraph = 5,
    LaserTrack = 6,
    LaserCooldown = 7,
    RollTelegraph = 8,
    Rolling = 9,
    Landing = 10,
    Recover = 11,
    TeleportAscend = 12,
    TeleportDescend = 13,
}

public enum PickupKind
{
    XpOrb = 0,
    Coin = 1,
    Heal = 2,
    ShapeItem = 3,
    Essence = 4,
    Modulator = 5,
}

public enum RunItemId
{
    SoldierSyringe = 0,
    Whetstone = 1,
    MagnifyingLens = 2,
    RunnerBoots = 3,
    VitalCore = 4,
    SplitShot = 5,
}

public enum RarityTier
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
    Mythic = 5,
}

public enum ModifierKind
{
    DamagePct = 0,
    FireRatePct = 1,
    MoveSpeedPct = 2,
    MaxHpPct = 3,
    ProjectileSizePct = 4,
    XpGainPct = 5,
    CoinGainPct = 6,
    BubbleStrengthPct = 7,
    ExtraSplitShot = 8,
}

public enum WeaponKind
{
    Square = 0,
    Circle = 1,
    Triangle = 2,
    Hexagon = 3,
    Sphere = 4,
    Cube = 5,
    Pyramid = 6,
    Cone = 7,
    Tesseract = 8,
    Hyperphere = 9,
    Pentachoron = 10,
    Hypercone = 11,
    ImpossibleCube = 12,
    Mobius = 13,
    Klein = 14,
    BoysSurface = 15,
}

public enum ShapeKind
{
    CoreCube = 0,
    StrikerPyramid = 1,
    BulwarkSphere = 2,
}

public enum LevelChoiceKind
{
    Item = 0,
    Weapon = 1,
    Shape = 2,
}

public readonly record struct ModifierRoll(ModifierKind Kind, float Value, RarityTier Rarity);
