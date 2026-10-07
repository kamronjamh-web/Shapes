using Godot;

namespace SuperGatchaRebuild;

internal static class RuntimeMath
{
    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.X) && IsFinite(value.Y);
    }

    public static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }

    public static float FiniteOr(float value, float fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    public static Vector2 FiniteOr(Vector2 value, Vector2 fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    public static Vector3 FiniteOr(Vector3 value, Vector3 fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    public static Vector2 SafeDirection(Vector2 value, Vector2 fallback)
    {
        if (!IsFinite(value))
        {
            return SafeFallbackDirection(fallback);
        }

        float lengthSquared = value.LengthSquared();
        if (!IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
        {
            return SafeFallbackDirection(fallback);
        }

        return value / Mathf.Sqrt(lengthSquared);
    }

    private static Vector2 SafeFallbackDirection(Vector2 fallback)
    {
        float lengthSquared = IsFinite(fallback) ? fallback.LengthSquared() : 0f;
        if (!IsFinite(fallback) || !IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
        {
            return Vector2.Right;
        }

        return fallback / Mathf.Sqrt(lengthSquared);
    }
}