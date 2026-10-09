using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsWeaponFireFeedback
{
    private void ApplyAdditionalPulse(float strength, ref Vector3 dimensions, ref Vector3 rotation)
    {
        float age = 1f - strength;
        switch (_shape)
        {
            case "hexagon": rotation.Z = age * .22f; break;
            case "sphere":
                dimensions = new Vector3(1f + age * .8f, 1f + age * .8f, .65f + strength * .5f);
                break;
            case "cube": dimensions = new Vector3(1f + age * .3f, 1f + age * .3f, .7f + strength * .3f); break;
            case "pyramid": dimensions.Z = .8f + age * .6f; break;
            case "cone":
                float flicker = 1f + Mathf.Sin(age * 24f) * .12f * strength;
                dimensions = new Vector3(flicker, flicker, .8f + age * .6f);
                break;
            case "hyperphere":
                dimensions = new Vector3(1f + age * .65f, 1f + age * .65f, .8f + age * .5f);
                rotation.Z = age * -.3f;
                break;
            case "pentachoron": rotation.Z = Mathf.Sin(age * 17f) * .18f * strength; break;
            case "hypercone": rotation.Z = age * 2.4f; dimensions.Z = .75f + age * .65f; break;
            case "impossiblecube":
                dimensions = new Vector3(.65f + strength * .35f, .65f + strength * .35f, .8f + strength * .2f);
                rotation = new Vector3(age * .25f, age * -.2f, age * .18f);
                break;
            case "mobius":
                dimensions = new Vector3(1f + age * .4f, 1f - age * .22f, 1f);
                rotation.Z = age * -.5f;
                break;
            case "klein":
                dimensions = new Vector3(1f + age * .25f, 1f + age * .5f, 1f);
                rotation.Y = Mathf.Sin(age * Mathf.Pi) * .3f;
                break;
            case "boys":
                dimensions = new Vector3(1f + age * .3f, 1f + age * .3f, .8f + strength * .2f);
                rotation.Z = Mathf.Sin(age * Mathf.Pi) * .35f;
                break;
        }
    }

    private void BuildAdditionalGeometry()
    {
        // Metre-scale aperture dimensions match the exported MuzzleAnchor locations.
        // All segments and materials are allocated on equip and reused on every shot.
        switch (_shape)
        {
            case "hexagon":
                AddRing(6, .038f, -.018f, Mathf.Pi / 6f);
                AddRing(6, .025f, -.045f, Mathf.Pi / 6f);
                AddSpokes(6, .038f, -.018f, .025f, -.045f, Mathf.Pi / 6f);
                break;
            case "sphere":
                AddOrientedRing(12, .055f, new Vector3(0f, 0f, -.042f), Basis.Identity);
                AddOrientedRing(12, .042f, new Vector3(0f, 0f, -.042f), new Basis(Vector3.Right, Mathf.Pi * .5f));
                break;
            case "cube":
                AddRing(4, .055f, -.015f, Mathf.Pi * .25f);
                AddRing(4, .055f, -.085f, Mathf.Pi * .25f);
                AddSpokes(4, .055f, -.015f, .055f, -.085f, Mathf.Pi * .25f);
                break;
            case "pyramid":
                AddRing(4, .052f, -.018f, Mathf.Pi * .25f);
                AddSpokes(4, .052f, -.018f, 0f, -.115f, Mathf.Pi * .25f);
                break;
            case "cone":
                AddRing(10, .035f, -.018f, 0f);
                AddRing(10, .07f, -.125f, 0f);
                AddSpokes(5, .035f, -.018f, .07f, -.125f, 0f);
                break;
            case "hyperphere":
                AddOrientedRing(12, .095f, new Vector3(0f, 0f, -.052f), Basis.Identity);
                AddOrientedRing(12, .075f, new Vector3(0f, 0f, -.052f), new Basis(Vector3.Right, Mathf.Pi * .5f));
                AddOrientedRing(12, .057f, new Vector3(0f, 0f, -.052f), new Basis(Vector3.Up, Mathf.Pi * .5f));
                break;
            case "pentachoron":
                // Five projected vertices with all pairwise edges, rather than another ring.
                Vector3[] points = new Vector3[5];
                for (int i = 0; i < 5; i++)
                {
                    float angle = Mathf.Pi * .5f + Mathf.Tau * i / 5f;
                    points[i] = new Vector3(Mathf.Cos(angle) * .085f, Mathf.Sin(angle) * .085f,
                        -.045f + (i % 2 == 0 ? -.035f : .015f));
                }
                for (int i = 0; i < 5; i++)
                    for (int j = i + 1; j < 5; j++) AddEdge(points[i], points[j]);
                break;
            case "hypercone":
                AddRing(10, .055f, -.018f, 0f);
                for (int strand = 0; strand < 2; strand++)
                    for (int i = 0; i < 12; i++)
                        AddEdge(HelixPoint(i / 12f, strand), HelixPoint((i + 1) / 12f, strand));
                break;
            case "impossiblecube":
                AddRing(4, .095f, -.020f, Mathf.Pi * .25f);
                AddRing(4, .068f, -.115f, Mathf.Pi * .4f);
                for (int i = 0; i < 4; i++)
                {
                    float a = Mathf.Pi * .25f + Mathf.Tau * i / 4f;
                    float b = Mathf.Pi * .4f + Mathf.Tau * (i + 1) / 4f;
                    AddEdge(new Vector3(Mathf.Cos(a) * .095f, Mathf.Sin(a) * .095f, -.020f),
                        new Vector3(Mathf.Cos(b) * .068f, Mathf.Sin(b) * .068f, -.115f));
                }
                break;
            case "mobius":
                // A single continuous edge winds twice through a half-twisted band boundary.
                for (int i = 0; i < 32; i++)
                    AddEdge(MobiusPoint(Mathf.Tau * 2f * i / 32f), MobiusPoint(Mathf.Tau * 2f * (i + 1) / 32f));
                break;
            case "klein":
                for (int i = 0; i < 24; i++)
                    AddEdge(KleinPoint(Mathf.Tau * i / 24f), KleinPoint(Mathf.Tau * (i + 1) / 24f));
                break;
            case "boys":
                for (int i = 0; i < 30; i++)
                    AddEdge(ClonePoint(Mathf.Tau * i / 30f), ClonePoint(Mathf.Tau * (i + 1) / 30f));
                AddRing(6, .031f, -.070f, Mathf.Pi / 6f);
                break;
        }
    }

    private void AddOrientedRing(int sides, float radius, Vector3 center, Basis basis)
    {
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.Tau * i / sides;
            float b = Mathf.Tau * (i + 1) / sides;
            AddEdge(center + basis * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                center + basis * new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, 0f));
        }
    }

    private void AddSpokes(int sides, float backRadius, float backDepth, float frontRadius, float frontDepth, float start)
    {
        for (int i = 0; i < sides; i++)
        {
            float angle = start + Mathf.Tau * i / sides;
            Vector3 radial = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            AddEdge(radial * backRadius + new Vector3(0f, 0f, backDepth),
                radial * frontRadius + new Vector3(0f, 0f, frontDepth));
        }
    }

    private static Vector3 HelixPoint(float u, int strand)
    {
        float angle = u * Mathf.Tau * 1.5f + strand * Mathf.Pi;
        float radius = .055f * (1f - u * .8f);
        return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -.018f - u * .19f);
    }

    private static Vector3 MobiusPoint(float angle)
    {
        float radius = .046f + .011f * Mathf.Cos(angle * .5f);
        return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * .65f,
            -.043f + .014f * Mathf.Sin(angle * .5f));
    }

    private static Vector3 KleinPoint(float angle)
    {
        float radius = .042f + .013f * Mathf.Cos(angle * 2f);
        return new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * .055f,
            -.040f + .026f * Mathf.Sin(angle * 2f));
    }

    private static Vector3 ClonePoint(float angle)
    {
        float radius = .027f * (2f + Mathf.Cos(angle * 3f));
        return new Vector3(Mathf.Cos(angle * 2f) * radius, Mathf.Sin(angle * 2f) * radius,
            -.045f + .025f * Mathf.Sin(angle * 3f));
    }
}
