using Godot;
using System;

namespace SuperGatchaRebuild;

public partial class EnemyActor : Node3D
{
    private const float GruntBaseHp = 40f;
    private const float InterceptorBaseHp = 48f;
    private const float ChargerBaseHp = 56f;
    private const float MortarBaseHp = 56f;
    private const float BulwarkBaseHp = 112f;

    private const float ChargerTelegraphDuration = 0.42f;
    private const float ChargerDashDuration = 0.30f;
    private const float ChargerDashSpeed = 42f;

    public EnemyKind Kind = EnemyKind.Grunt;
    public float MaxHp = GruntBaseHp;
    public float Hp = GruntBaseHp;
    public float Radius = 0.8f;
    public float MoveSpeed = 14f;
    public float ShootCd = 1.8f;
    public float BulletSpeed = 24f;
    public float BulletDamage = 10f;
    public bool ContactDamageEnabled = true;
    public float ContactDps = 14f;
    public float OrbitAngle = 0f;
    public Vector2 LastVelocity = Vector2.Zero;
    public float FlashTimer = 0f;
    private Vector2 _position2D = Vector2.Zero;
    private float _chargerTelegraphTimer;
    private float _chargerDashTimer;
    private Vector2 _chargerDashDirection = Vector2.Right;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _mat = null!;

    public new Vector2 Position
    {
        get => _position2D;
        set
        {
            if (!RuntimeMath.IsFinite(value))
            {
                return;
            }

            _position2D = value;
            base.Position = new Vector3(value.X, 0f, value.Y);
        }
    }

    public bool IsAlive => Hp > 0f;

    public void Configure(EnemyKind kind, float hpScale, float dmgScale, Vector2 startPos, Vector2 playerPos)
    {
        Kind = kind;
        Position = startPos;
        OrbitAngle = Mathf.Atan2(startPos.Y - playerPos.Y, startPos.X - playerPos.X);
        FlashTimer = 0f;

        _chargerTelegraphTimer = 0f;
        _chargerDashTimer = 0f;
        _chargerDashDirection = RuntimeMath.SafeDirection(playerPos - startPos, Vector2.Right);

        switch (kind)
        {
            case EnemyKind.Interceptor:
                MaxHp = InterceptorBaseHp * hpScale;
                Radius = 1.0f;
                MoveSpeed = 16f * (1f + (dmgScale - 1f) * 0.20f);
                ShootCd = 1.4f;
                BulletSpeed = 28f;
                BulletDamage = 12f * dmgScale;
                ContactDamageEnabled = false;
                ContactDps = 0f;
                break;
            case EnemyKind.Bulwark:
                MaxHp = BulwarkBaseHp * hpScale;
                Radius = 1.45f;
                MoveSpeed = 8.5f * (1f + (dmgScale - 1f) * 0.12f);
                ShootCd = float.MaxValue;
                BulletSpeed = 0f;
                BulletDamage = 0f;
                ContactDamageEnabled = true;
                ContactDps = 24f * dmgScale;
                break;
            case EnemyKind.Charger:
                MaxHp = ChargerBaseHp * hpScale;
                Radius = 1.0f;
                MoveSpeed = 12f * (1f + (dmgScale - 1f) * 0.18f);
                ShootCd = 1.7f;
                BulletSpeed = 0f;
                BulletDamage = 0f;
                ContactDamageEnabled = true;
                ContactDps = 32f * dmgScale;
                break;
            case EnemyKind.Mortar:
                MaxHp = MortarBaseHp * hpScale;
                Radius = 1.1f;
                MoveSpeed = 9.5f * (1f + (dmgScale - 1f) * 0.14f);
                ShootCd = 2.8f;
                BulletSpeed = 15f;
                BulletDamage = 18f * dmgScale;
                ContactDamageEnabled = false;
                ContactDps = 0f;
                break;
            default:
                MaxHp = GruntBaseHp * hpScale;
                Radius = 0.8f;
                MoveSpeed = 14f * (1f + (dmgScale - 1f) * 0.18f);
                ShootCd = 1.8f;
                BulletSpeed = 24f;
                BulletDamage = 10f * dmgScale;
                ContactDamageEnabled = true;
                ContactDps = 18f * dmgScale;
                break;
        }

        Hp = MaxHp;
        BuildVisualForKind();
        Position = startPos;
        UpdateVisuals();
    }

    public void TakeDamage(float dmg)
    {
        Hp = Mathf.Max(0f, Hp - dmg);
        FlashTimer = 0.08f;
    }

    public void TickAI(
        double delta,
        Vector2 playerPos,
        Vector2 playerVel,
        Func<Vector2, Vector2, float, TeamId, Color, float, BulletActor> spawnBullet,
        Func<Vector2, float, Vector2> clampResolve,
        Func<Vector2, Vector2, bool> isBlockedByPillar)
    {
        float dt = (float)delta;
        if (FlashTimer > 0f)
        {
            FlashTimer -= dt;
        }

        ShootCd -= dt;

        if (Kind == EnemyKind.Interceptor)
        {
            Vector2 toPlayer = playerPos - Position;
            float dist = RuntimeMath.IsFinite(toPlayer) ? Mathf.Max(0.001f, toPlayer.Length()) : 0.001f;
            Vector2 n = RuntimeMath.SafeDirection(toPlayer, Vector2.Right);

            float desiredRange = 35f;
            float radialErr = dist - desiredRange;
            float radialSpd = Mathf.Clamp(radialErr * 1.2f, -MoveSpeed * 1.4f, MoveSpeed * 1.4f);

            Vector2 tangent = new Vector2(-n.Y, n.X);
            OrbitAngle += 0.35f * dt;
            Vector2 orbitPush = tangent * (MoveSpeed * 0.8f) + new Vector2(Mathf.Cos(OrbitAngle), Mathf.Sin(OrbitAngle)) * (MoveSpeed * 0.15f);
            Vector2 moveVel = n * radialSpd + orbitPush;
            if (moveVel.Length() > MoveSpeed * 1.8f)
            {
                moveVel = moveVel.Normalized() * MoveSpeed * 1.8f;
            }

            LastVelocity = moveVel;
            Position += moveVel * dt;

            if (ShootCd <= 0f)
            {
                ShootCd = 1.35f;
                Vector2 lead = playerPos + playerVel * 0.45f;
                Vector2 dir = RuntimeMath.SafeDirection(lead - Position, n);
                if (!isBlockedByPillar(Position, playerPos))
                {
                    spawnBullet(Position, dir * BulletSpeed, BulletDamage, TeamId.Monster, new Color(1f, 0.45f, 0.15f), 1.1f);
                }
            }
        }
        else if (Kind == EnemyKind.Bulwark)
        {
            Vector2 dir = RuntimeMath.SafeDirection(playerPos - Position, Vector2.Right);
            LastVelocity = dir * MoveSpeed;
            Position += LastVelocity * dt;
        }
        else if (Kind == EnemyKind.Charger)
        {
            TickCharger(dt, playerPos, playerVel);
        }
        else if (Kind == EnemyKind.Mortar)
        {
            Vector2 toPlayer = playerPos - Position;
            float dist = RuntimeMath.IsFinite(toPlayer) ? Mathf.Max(0.001f, toPlayer.Length()) : 0.001f;
            Vector2 dir = RuntimeMath.SafeDirection(toPlayer, Vector2.Right);

            float desiredRange = 54f;
            float radialErr = dist - desiredRange;
            float moveScalar = Mathf.Clamp(radialErr * 0.9f, -MoveSpeed, MoveSpeed);
            Vector2 strafe = new Vector2(-dir.Y, dir.X) * Mathf.Sin(OrbitAngle) * (MoveSpeed * 0.35f);
            OrbitAngle += 0.9f * dt;

            LastVelocity = dir * moveScalar + strafe;
            if (LastVelocity.Length() > MoveSpeed * 1.1f)
            {
                LastVelocity = LastVelocity.Normalized() * MoveSpeed * 1.1f;
            }

            Position += LastVelocity * dt;

            if (ShootCd <= 0f)
            {
                ShootCd = 2.65f;
                Vector2 lead = playerPos + playerVel * 0.85f;
                Vector2 shotDir = RuntimeMath.SafeDirection(lead - Position, dir);
                if (!isBlockedByPillar(Position, lead))
                {
                    spawnBullet(Position, shotDir * BulletSpeed, BulletDamage, TeamId.Monster, new Color(1f, 0.82f, 0.30f), 1.65f);
                }
            }
        }
        else
        {
            Vector2 dir = RuntimeMath.SafeDirection(playerPos - Position, Vector2.Right);
            LastVelocity = dir * MoveSpeed;
            Position += LastVelocity * dt;

            if (ShootCd <= 0f)
            {
                ShootCd = 1.85f;
                Vector2 dirShot = RuntimeMath.SafeDirection(playerPos - Position, dir);
                if (!isBlockedByPillar(Position, playerPos))
                {
                    spawnBullet(Position, dirShot * BulletSpeed, BulletDamage, TeamId.Monster, new Color(1f, 0.3f, 0.2f), 1.0f);
                }
            }
        }

        Position = clampResolve(Position, Radius);
        UpdateVisuals();
    }

    private void TickCharger(float dt, Vector2 playerPos, Vector2 playerVel)
    {
        Vector2 toPlayer = playerPos - Position;
        float dist = RuntimeMath.IsFinite(toPlayer) ? Mathf.Max(0.001f, toPlayer.Length()) : 0.001f;
        Vector2 dir = RuntimeMath.SafeDirection(toPlayer, Vector2.Right);

        if (_chargerDashTimer > 0f)
        {
            _chargerDashTimer -= dt;
            LastVelocity = _chargerDashDirection * ChargerDashSpeed;
            Position += LastVelocity * dt;
            return;
        }

        if (_chargerTelegraphTimer > 0f)
        {
            _chargerTelegraphTimer -= dt;
            LastVelocity = Vector2.Zero;
            if (_chargerTelegraphTimer <= 0f)
            {
                _chargerDashTimer = ChargerDashDuration;
            }

            return;
        }

        LastVelocity = dir * MoveSpeed;
        Position += LastVelocity * dt;

        if (ShootCd <= 0f && dist <= 34f)
        {
            ShootCd = 1.9f;
            _chargerTelegraphTimer = ChargerTelegraphDuration;
            _chargerDashDirection = RuntimeMath.SafeDirection(playerPos + playerVel * 0.18f - Position, dir);
        }
    }

    public override void _Process(double delta)
    {
        if (FlashTimer > 0f)
        {
            FlashTimer -= (float)delta;
        }

        UpdateVisuals();
    }

    private void BuildVisualForKind()
    {
        if (_mesh != null)
        {
            _mesh.QueueFree();
        }

        _mat = new StandardMaterial3D
        {
            Metallic = 0.2f,
            Roughness = 0.45f,
        };

        _mesh = new MeshInstance3D();
        switch (Kind)
        {
            case EnemyKind.Interceptor:
                _mesh.Position = new Vector3(0f, 0.9f, 0f);
                _mesh.Mesh = new CylinderMesh
                {
                    TopRadius = 0.05f,
                    BottomRadius = 1.0f,
                    Height = 2.4f,
                    RadialSegments = 6,
                };
                _mesh.RotationDegrees = new Vector3(90f, 0f, 0f);
                _mesh.Scale = new Vector3(0.75f, 0.75f, 0.75f);
                break;
            case EnemyKind.Bulwark:
                _mesh.Position = new Vector3(0f, 0.9f, 0f);
                _mesh.Mesh = new BoxMesh
                {
                    Size = new Vector3(2.7f, 1.9f, 2.7f),
                };
                _mesh.Scale = Vector3.One;
                break;
            case EnemyKind.Charger:
                _mesh.Position = new Vector3(0f, 0.8f, 0f);
                _mesh.Mesh = new CapsuleMesh
                {
                    Radius = 0.75f,
                    Height = 1.9f,
                };
                _mesh.Scale = new Vector3(0.95f, 1.0f, 0.95f);
                break;
            case EnemyKind.Mortar:
                _mesh.Position = new Vector3(0f, 0.85f, 0f);
                _mesh.Mesh = new CylinderMesh
                {
                    TopRadius = 0.9f,
                    BottomRadius = 1.05f,
                    Height = 1.7f,
                    RadialSegments = 14,
                };
                _mesh.Scale = new Vector3(0.95f, 1.0f, 0.95f);
                break;
            default:
                _mesh.Position = new Vector3(0f, 0.3f, 0f);
                _mesh.Mesh = new SphereMesh
                {
                    Radius = 1.0f,
                    Height = 2.0f,
                };
                _mesh.Scale = new Vector3(0.6f, 0.3f, 0.6f);
                break;
        }

        _mesh.MaterialOverride = _mat;
        AddChild(_mesh);
    }

    private void UpdateVisuals()
    {
        if (_mat == null)
        {
            return;
        }

        if (FlashTimer > 0f)
        {
            _mat.AlbedoColor = Colors.White;
        }
        else
        {
            _mat.AlbedoColor = Kind switch
            {
                EnemyKind.Interceptor => new Color(0.95f, 0.2f, 0.2f),
                EnemyKind.Bulwark => new Color(0.22f, 0.80f, 0.58f),
                EnemyKind.Charger when _chargerDashTimer > 0f => new Color(1f, 0.38f, 0.20f),
                EnemyKind.Charger when _chargerTelegraphTimer > 0f => new Color(1f, 0.92f, 0.34f),
                EnemyKind.Charger => new Color(0.32f, 0.78f, 1f),
                EnemyKind.Mortar => new Color(1f, 0.72f, 0.24f),
                _ => new Color(0.9f, 0.25f, 0.8f),
            };
        }

        if (RuntimeMath.IsFinite(LastVelocity) && LastVelocity.LengthSquared() > 1f)
        {
            float ang = Mathf.Atan2(LastVelocity.Y, LastVelocity.X);
            Rotation = new Vector3(0f, -ang, 0f);
        }
    }
}
