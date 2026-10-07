using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Shapes;

public partial class TesseractVisualAnimator : Node3D, IElectricAnchorProvider
{
    private readonly struct Point4D(float x, float y, float z, float w)
    {
        public readonly float X = x;
        public readonly float Y = y;
        public readonly float Z = z;
        public readonly float W = w;
    }

    [Export] public float AnimationSpeed = 0.30f;
    [Export] public float ProjectionScale = 0.42f;
    [Export] public float VertexRadius = 0.035f;
    [Export] public float GlowPulseSpeed = 2.15f;

    private const float WCamera = 4f;
    private const float ZCamera = 6f;

    private readonly Point4D[] _vertices4D = new Point4D[16];
    private readonly int[] _edgeIndices = new int[64];
    private readonly Vector3[] _projectedVertices = new Vector3[16];
    private readonly MeshInstance3D[] _vertexPoints = new MeshInstance3D[16];
    private readonly Vector3[] _linePoints = new Vector3[64];

    private MeshInstance3D? _edgeMesh;
    private StandardMaterial3D? _material;
    private float _baseEmissionEnergy = 0.9f;
    private float _angle;
    private bool _visualsBuilt;

    public void Configure(Color tint, StandardMaterial3D material)
    {
        _material = material;
        _material.AlbedoColor = tint;
        _material.Emission = tint.Lightened(0.2f);
        _material.EmissionEnabled = true;
        _baseEmissionEnergy = _material.EmissionEnergyMultiplier;
    }

    public override void _Ready()
    {
        BuildHypercubeData();
        BuildVisuals();
        UpdateProjection(0f);
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _angle += (float)delta * AnimationSpeed;
        UpdateProjection(_angle);

        if (_material != null)
        {
            _material.EmissionEnergyMultiplier = _baseEmissionEnergy * (0.82f + Mathf.Sin(_angle * GlowPulseSpeed) * 0.18f);
        }
    }

    public void CollectElectricAnchorPoints(ICollection<Vector3> anchors)
    {
        for (int vertexIndex = 0; vertexIndex < _projectedVertices.Length; vertexIndex++)
        {
            anchors.Add(_projectedVertices[vertexIndex]);
        }

        for (int edgeIndex = 0; edgeIndex < _edgeIndices.Length; edgeIndex += 4)
        {
            Vector3 a = _projectedVertices[_edgeIndices[edgeIndex]];
            Vector3 b = _projectedVertices[_edgeIndices[edgeIndex + 1]];
            anchors.Add((a + b) * 0.5f);
        }
    }

    private void BuildHypercubeData()
    {
        for (int i = 0; i < _vertices4D.Length; i++)
        {
            _vertices4D[i] = new Point4D(
                (i & 1) != 0 ? 1f : -1f,
                (i & 2) != 0 ? 1f : -1f,
                (i & 4) != 0 ? 1f : -1f,
                (i & 8) != 0 ? 1f : -1f);
        }

        int edgeWriteIndex = 0;
        for (int i = 0; i < _vertices4D.Length; i++)
        {
            for (int j = i + 1; j < _vertices4D.Length; j++)
            {
                if (HammingDistance(_vertices4D[i], _vertices4D[j]) != 1)
                {
                    continue;
                }

                _edgeIndices[edgeWriteIndex++] = i;
                _edgeIndices[edgeWriteIndex++] = j;
            }
        }
    }

    private void BuildVisuals()
    {
        if (_visualsBuilt)
        {
            return;
        }

        _material ??= CreateFallbackMaterial();
        _baseEmissionEnergy = _material.EmissionEnergyMultiplier;

        _edgeMesh = new MeshInstance3D
        {
            Name = "Projected4DEdges",
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_edgeMesh);

        SphereMesh vertexMesh = new()
        {
            Radius = VertexRadius,
            Height = VertexRadius * 2f,
            RadialSegments = 8,
            Rings = 4,
        };

        for (int i = 0; i < _vertexPoints.Length; i++)
        {
            MeshInstance3D point = new()
            {
                Name = $"Projected4DVertex{i:00}",
                Mesh = vertexMesh,
                MaterialOverride = _material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _vertexPoints[i] = point;
            AddChild(point);
        }

        _visualsBuilt = true;
    }

    private void UpdateProjection(float angle)
    {
        for (int i = 0; i < _vertices4D.Length; i++)
        {
            _projectedVertices[i] = ProjectTo3D(Rotate4D(_vertices4D[i], angle));
            _vertexPoints[i].Position = _projectedVertices[i];
        }

        for (int i = 0; i < _edgeIndices.Length; i++)
        {
            _linePoints[i] = _projectedVertices[_edgeIndices[i]];
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _linePoints;

        ArrayMesh lineMesh = new();
        lineMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        if (_edgeMesh != null)
        {
            _edgeMesh.Mesh = lineMesh;
        }
    }

    private Vector3 ProjectTo3D(Point4D point)
    {
        float wFactor = WCamera / (WCamera - point.W);
        float x3 = point.X * wFactor;
        float y3 = point.Y * wFactor;
        float z3 = point.Z * wFactor;
        float zFactor = ZCamera / (ZCamera - z3);

        return new Vector3(
            x3 * zFactor * ProjectionScale,
            y3 * zFactor * ProjectionScale,
            z3 * zFactor * ProjectionScale);
    }

    private static Point4D Rotate4D(Point4D point, float angle)
    {
        float x = point.X;
        float y = point.Y;
        float z = point.Z;
        float w = point.W;

        float cos = Mathf.Cos(angle * 0.9f);
        float sin = Mathf.Sin(angle * 0.9f);
        float x1 = x * cos - w * sin;
        float w1 = x * sin + w * cos;
        x = x1;
        w = w1;

        cos = Mathf.Cos(angle * 1.1f);
        sin = Mathf.Sin(angle * 1.1f);
        float y1 = y * cos - z * sin;
        float z1 = y * sin + z * cos;
        y = y1;
        z = z1;

        cos = Mathf.Cos(angle * 0.7f);
        sin = Mathf.Sin(angle * 0.7f);
        float x2 = x * cos - y * sin;
        float y2 = x * sin + y * cos;

        return new Point4D(x2, y2, z, w);
    }

    private static int HammingDistance(Point4D a, Point4D b)
    {
        int distance = 0;
        if (!Mathf.IsEqualApprox(a.X, b.X)) distance++;
        if (!Mathf.IsEqualApprox(a.Y, b.Y)) distance++;
        if (!Mathf.IsEqualApprox(a.Z, b.Z)) distance++;
        if (!Mathf.IsEqualApprox(a.W, b.W)) distance++;
        return distance;
    }

    private static StandardMaterial3D CreateFallbackMaterial()
    {
        Color tint = new(0.56f, 0.72f, 1f, 1f);
        return new StandardMaterial3D
        {
            AlbedoColor = tint,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = 0.9f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }
}
