using Godot;

namespace SuperGatchaRebuild.UI;

public partial class FloatingDamageNumber3D : Label3D
{
    private const float Lifetime = 0.72f;
    private const float RiseSpeed = 4.8f;

    private float _age;
    private Color _baseColor = Colors.White;

    public void Configure(float damage, bool critical, Vector3 worldPosition)
    {
        Position = worldPosition;
        Text = Mathf.RoundToInt(Mathf.Max(0f, damage)).ToString();
        _baseColor = critical ? new Color(1f, 0.12f, 0.10f, 1f) : Colors.White;

        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = true;
        Shaded = false;
        DoubleSided = true;
        FontSize = critical ? 42 : 34;
        PixelSize = critical ? 0.042f : 0.036f;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Modulate = _baseColor;
        OutlineSize = critical ? 10 : 8;
        OutlineModulate = new Color(0f, 0f, 0f, 0.82f);
        RenderPriority = 100;
        OutlineRenderPriority = 99;
        Scale = critical ? Vector3.One * 1.18f : Vector3.One;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        Position += Vector3.Up * RiseSpeed * dt;

        float progress = Mathf.Clamp(_age / Lifetime, 0f, 1f);
        float alpha = 1f - progress * progress;
        Modulate = new Color(_baseColor.R, _baseColor.G, _baseColor.B, alpha);
        OutlineModulate = new Color(0f, 0f, 0f, 0.82f * alpha);
        Scale = Vector3.One * Mathf.Lerp(1.12f, 0.86f, progress);

        if (_age >= Lifetime)
        {
            QueueFree();
        }
    }
}