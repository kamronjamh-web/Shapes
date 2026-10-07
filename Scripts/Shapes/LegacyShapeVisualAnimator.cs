using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild;

namespace SuperGatchaRebuild.Shapes;

public enum LegacyShapeVisualMode
{
	FlatSquare,
	FlatCircle,
	FlatTriangle,
	FlatHexagon,
	FacetedSphere,
	FacetedCube,
	FacetedPyramid,
	FacetedCone,
	Hyperphere,
	Pentachoron,
	Hypercone,
	ImpossibleCube,
	Mobius,
	Klein,
	BoysSurface
}

public partial class LegacyShapeVisualAnimator : Node3D, IElectricAnchorProvider
{
	private readonly struct Point4D(float x, float y, float z, float w)
	{
		public readonly float X = x;
		public readonly float Y = y;
		public readonly float Z = z;
		public readonly float W = w;
	}

	[Export] public LegacyShapeVisualMode Mode = LegacyShapeVisualMode.FacetedSphere;
	[Export] public float AnimationSpeed = 1f;

	private const float FacetedRotateSpeed = 0.18f;
	private const float SlowedShapeAnimationMultiplier = 0.5f;
	private const float HyperphereOutlinePointScale = 1.28f;
	private const int BoysU = 30;
	private const int BoysV = 60;

	private Color _tint = Colors.White;
	private float _time;
	private float _flatSpinRate;
	private int _orbiterCount;

	private MeshInstance3D? _bodyMesh;
	private MeshInstance3D? _edgeMesh;
	private MeshInstance3D? _flatRing;
	private MeshInstance3D? _outlineMesh;
	private MultiMeshInstance3D? _pointCloud;
	private MultiMeshInstance3D? _pointOutlineCloud;
	private MultiMeshInstance3D? _orbiters;
	private MultiMesh? _pointMultiMesh;
	private MultiMesh? _pointOutlineMultiMesh;
	private MultiMesh? _orbiterMultiMesh;

	private Point4D[] _points4D = [];
	private int[] _lineIndices = [];
	private Vector3[] _projected = [];
	private Vector3[] _lineBuffer = [];
	private Vector3[] _boysGrid = [];
	private int _pointEvery = 1;
	private float _pointBaseSize = 0.035f;

	public void Configure(LegacyShapeVisualMode mode, Color tint)
	{
		Mode = mode;
		_tint = tint;
	}

	public void RefreshForShapeVisualSettings()
	{
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}

		_bodyMesh = null;
		_edgeMesh = null;
		_flatRing = null;
		_outlineMesh = null;
		_pointCloud = null;
		_pointOutlineCloud = null;
		_orbiters = null;
		_pointMultiMesh = null;
		_pointOutlineMultiMesh = null;
		_orbiterMultiMesh = null;

		BuildVisual();
	}

	public override void _Ready()
	{
		BuildVisual();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		float speedMultiplier = Mode == LegacyShapeVisualMode.BoysSurface ? 1f : SlowedShapeAnimationMultiplier;
		float dt = (float)delta * AnimationSpeed * speedMultiplier;
		_time += dt;

		switch (Mode)
		{
			case LegacyShapeVisualMode.FlatSquare:
			case LegacyShapeVisualMode.FlatCircle:
			case LegacyShapeVisualMode.FlatTriangle:
			case LegacyShapeVisualMode.FlatHexagon:
				UpdateFlatShape();
				break;
			case LegacyShapeVisualMode.FacetedSphere:
			case LegacyShapeVisualMode.FacetedCube:
			case LegacyShapeVisualMode.FacetedPyramid:
			case LegacyShapeVisualMode.FacetedCone:
			case LegacyShapeVisualMode.ImpossibleCube:
				UpdateLegacy3D(dt);
				break;
			case LegacyShapeVisualMode.Hyperphere:
				UpdateFourDPointCloud(RotateHyperphere, 3.2f, 5.5f, 0.52f, 0.020f);
				break;
			case LegacyShapeVisualMode.Pentachoron:
				UpdateFourDLineAndPoints(RotatePentachoron, 4f, 6f, 0.46f, 0.042f);
				break;
			case LegacyShapeVisualMode.Hypercone:
				UpdateFourDPointCloud(RotateHypercone, 3.5f, 5.8f, 0.46f, 0.019f);
				break;
			case LegacyShapeVisualMode.Mobius:
				UpdateFourDLineAndPoints(RotateTesseractLike, 4f, 6f, 0.50f, 0.026f);
				break;
			case LegacyShapeVisualMode.Klein:
				UpdateFourDLineAndPoints(RotateKlein, 4.5f, 5f, 0.70f, 0.016f);
				break;
			case LegacyShapeVisualMode.BoysSurface:
				UpdateBoysSurface();
				break;
		}
	}

	public void CollectElectricAnchorPoints(ICollection<Vector3> anchors)
	{
		int initialCount = anchors.Count;
		AddSampledAnchorPoints(anchors, _projected, 20);
		AddSampledAnchorPoints(anchors, _lineBuffer, 24);
		AddSampledAnchorPoints(anchors, _boysGrid, 24);

		if (anchors.Count > initialCount)
		{
			return;
		}

		AddMeshBoundsAnchors(anchors, _bodyMesh);
		AddMeshBoundsAnchors(anchors, _edgeMesh);
		AddMeshBoundsAnchors(anchors, _flatRing);
	}

	private void BuildVisual()
	{
		_pointEvery = 1;
		_pointBaseSize = 0.035f;
		_points4D = [];
		_lineIndices = [];
		_projected = [];
		_lineBuffer = [];
		_boysGrid = [];

		switch (Mode)
		{
			case LegacyShapeVisualMode.FlatSquare:
				BuildFlatShape(4, 0.48f, 0.22f, 4);
				break;
			case LegacyShapeVisualMode.FlatCircle:
				BuildFlatShape(48, 0.46f, 0f, 8);
				break;
			case LegacyShapeVisualMode.FlatTriangle:
				BuildFlatShape(3, 0.53f, 0.18f, 3);
				break;
			case LegacyShapeVisualMode.FlatHexagon:
				BuildFlatShape(6, 0.50f, 0.16f, 6);
				break;
			case LegacyShapeVisualMode.FacetedSphere:
				BuildCelShadedSphere();
				break;
			case LegacyShapeVisualMode.FacetedCube:
				BuildFacetedMesh(CreateCubeVertices(out List<int[]> cubeFaces), cubeFaces, 0.62f, new Vector3(-0.20f, 0f, 0f));
				break;
			case LegacyShapeVisualMode.FacetedPyramid:
				BuildFacetedMesh(CreatePyramidVertices(out List<int[]> pyramidFaces), pyramidFaces, 0.68f, new Vector3(-0.30f, 0f, 0f));
				break;
			case LegacyShapeVisualMode.FacetedCone:
				BuildFacetedMesh(CreateConeVertices(out List<int[]> coneFaces), coneFaces, 0.56f, new Vector3(-0.30f, 0f, 0f));
				break;
			case LegacyShapeVisualMode.Hyperphere:
				BuildCelShadedHyperphere();
				break;
			case LegacyShapeVisualMode.Pentachoron:
				_points4D = CreatePentachoronPoints();
				_lineIndices = CreateCompleteLineIndices(_points4D.Length);
				BuildLineAndPointCloud(_points4D.Length, _lineIndices.Length);
				break;
			case LegacyShapeVisualMode.Hypercone:
				_points4D = CreateHyperconePoints();
				BuildPointCloud(_points4D.Length);
				break;
			case LegacyShapeVisualMode.ImpossibleCube:
				BuildImpossibleCube();
				break;
			case LegacyShapeVisualMode.Mobius:
				_points4D = CreateMobiusPoints();
				_lineIndices = CreatePairLineIndices(_points4D.Length);
				BuildLineAndPointCloud(_points4D.Length, _lineIndices.Length);
				break;
			case LegacyShapeVisualMode.Klein:
				_points4D = CreateKleinPoints(out _lineIndices);
				_pointEvery = 3;
				BuildLineAndPointCloud((_points4D.Length + _pointEvery - 1) / _pointEvery, _lineIndices.Length);
				break;
			case LegacyShapeVisualMode.BoysSurface:
				BuildBoysSurface();
				break;
		}
	}

	private void BuildFlatShape(int sides, float radius, float spinRate, int orbiterCount)
	{
		_flatSpinRate = spinRate;
		_orbiterCount = orbiterCount;
		StandardMaterial3D bodyMaterial = CreateMaterial(new Color(_tint.R, _tint.G, _tint.B, 0.92f), _tint.Lightened(0.18f), 0.62f, true, false);
		bodyMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		StandardMaterial3D lineMaterial = CreateMaterial(_tint.Lightened(0.22f), _tint.Lightened(0.3f), 0.95f, true, true);

		_bodyMesh = AddMesh("LegacyFlatBody", CreateFlatPolygonMesh(sides, radius), bodyMaterial);
		_edgeMesh = AddMesh("LegacyFlatGlyph", CreateFlatGlyphMesh(sides, radius), lineMaterial);
		_flatRing = AddMesh("LegacyOrbitRing", CreateCircleLineMesh(radius * 1.34f, 96), lineMaterial);
		_orbiters = CreatePointCloud("LegacyFlatOrbiters", orbiterCount, 0.032f, _tint.Lightened(0.35f));
	}

	private void UpdateFlatShape()
	{
		float pulse = 1f + Mathf.Sin(_time * 2.2f) * 0.035f;
		float spin = _flatSpinRate * _time;
		if (_bodyMesh != null)
		{
			_bodyMesh.Rotation = new Vector3(0f, 0f, spin);
			_bodyMesh.Scale = Vector3.One * pulse;
		}

		if (_edgeMesh != null)
		{
			_edgeMesh.Rotation = new Vector3(0f, 0f, -spin * 1.6f + Mathf.Pi * 0.25f);
		}

		if (_flatRing != null)
		{
			_flatRing.Rotation = new Vector3(0f, 0f, -_time * 0.85f);
			_flatRing.Scale = Vector3.One * (1f + Mathf.Sin(_time * 2.2f) * 0.025f);
		}

		if (_orbiterMultiMesh == null)
		{
			return;
		}

		float orbitRadius = 0.68f;
		for (int i = 0; i < _orbiterCount; i++)
		{
			float angle = _time * (0.65f + (i % 2) * 0.12f) + Mathf.Tau * i / _orbiterCount;
			Vector3 pos = new(Mathf.Cos(angle) * orbitRadius, Mathf.Sin(angle) * orbitRadius, 0.03f);
			float size = 0.022f + (Mathf.Sin(_time * 3f + i) + 1f) * 0.008f;
			SetMultimeshPoint(_orbiterMultiMesh, i, pos, size);
		}
	}

	private void BuildFacetedMesh(List<Vector3> vertices, List<int[]> faces, float alpha, Vector3 baseRotation)
	{
		ArrayMesh surfaceMesh = CreateTriangleMesh(vertices, faces);
		ArrayMesh edgeMesh = CreateLineMesh(vertices, faces);
		ShaderMaterial bodyMaterial = ShapeVisualFactory.CreateNeutralCelShadedMaterial();
		StandardMaterial3D lineMaterial = ShapeVisualFactory.CreateNeutralCelAccentMaterial(0.56f, true, true);
		_bodyMesh = AddMesh("LegacyFacetedBody", surfaceMesh, bodyMaterial);
		_edgeMesh = AddMesh("LegacyFacetedEdges", edgeMesh, lineMaterial);
		_outlineMesh = AddMesh(
			"LegacyFacetedOutline",
			surfaceMesh,
			ShapeVisualFactory.CreateRarityOutlineShellMaterial(ShapeVisualFactory.ResolveCelOutlineColor(_tint), 0f));
		_outlineMesh.Scale = ShapeVisualFactory.CalculateOutlineScale(surfaceMesh, ShapeVisualFactory.StandardCelOutlineWidth);
		_bodyMesh.Rotation = baseRotation;
		_edgeMesh.Rotation = baseRotation;
		_outlineMesh.Rotation = baseRotation;
	}

	private void BuildCelShadedSphere()
	{
		SphereMesh sphereMesh = new()
		{
			Radius = 0.54f,
			Height = 1.08f,
			RadialSegments = 32,
			Rings = 18,
		};

		_bodyMesh = AddMesh("LegacyCelSphereBody", sphereMesh, ShapeVisualFactory.CreateNeutralCelShadedMaterial());
		_outlineMesh = AddMesh(
			"LegacyCelSphereOutline",
			sphereMesh,
			ShapeVisualFactory.CreateRarityOutlineShellMaterial(ShapeVisualFactory.ResolveCelOutlineColor(_tint), ShapeVisualFactory.StandardCelOutlineWidth));
		_outlineMesh.Scale = ShapeVisualFactory.CalculateOutlineScale(sphereMesh, ShapeVisualFactory.StandardCelOutlineWidth);
	}

	private void UpdateLegacy3D(float dt)
	{
		RotateY(dt * FacetedRotateSpeed);
		float pulse = 1f + Mathf.Sin(_time * 2.1f) * 0.018f;
		Scale = Vector3.One * pulse;
	}

	private void BuildPointCloud(int count, bool preserveProjectedBuffer = false)
	{
		if (!preserveProjectedBuffer)
		{
			_projected = new Vector3[count];
		}

		_pointCloud = CreatePointCloud("LegacyPointCloud", count, _pointBaseSize, _tint.Lightened(0.25f));
		_pointMultiMesh = _pointCloud.Multimesh;
	}

	private void BuildCelShadedHyperphere()
	{
		_bodyMesh = null;
		_edgeMesh = null;
		_outlineMesh = null;

		_points4D = CreateHyperpherePoints(800);
		_projected = new Vector3[_points4D.Length];
		_pointCloud = CreatePointCloud("LegacyPointCloud", _points4D.Length, _pointBaseSize, ShapeVisualFactory.CreateNeutralCelShadedMaterial());
		_pointMultiMesh = _pointCloud.Multimesh;
		_pointOutlineCloud = CreatePointCloud(
			"LegacyPointOutlineCloud",
			_points4D.Length,
			_pointBaseSize,
			ShapeVisualFactory.CreateRarityOutlineShellMaterial(ShapeVisualFactory.ResolveCelOutlineColor(_tint), 0.28f));
		_pointOutlineMultiMesh = _pointOutlineCloud.Multimesh;
	}

	private void BuildLineAndPointCloud(int visiblePointCount, int linePointCount)
	{
		_projected = new Vector3[_points4D.Length];
		_lineBuffer = new Vector3[linePointCount];
		_edgeMesh = AddMesh("LegacyProjectedLines", CreateRawLineMesh(_lineBuffer), CreateMaterial(_tint.Lightened(0.22f), _tint.Lightened(0.3f), 0.9f, true, true));
		BuildPointCloud(visiblePointCount, preserveProjectedBuffer: true);
	}

	private void UpdateFourDPointCloud(Func<Point4D, float, Point4D> rotate, float wCamera, float zCamera, float scale, float baseSize)
	{
		if (_pointMultiMesh == null)
		{
			return;
		}

		for (int i = 0; i < _points4D.Length; i++)
		{
			Vector3 projected = Project4DTo3D(rotate(_points4D[i], _time), wCamera, zCamera, scale);
			_projected[i] = projected;
			float size = baseSize * (0.85f + Mathf.Clamp(projected.Z + 0.8f, 0f, 1.6f) * 0.28f);
			SetMultimeshPoint(_pointMultiMesh, i, projected, size);

			if (_pointOutlineMultiMesh != null)
			{
				SetMultimeshPoint(_pointOutlineMultiMesh, i, projected, size * HyperphereOutlinePointScale);
			}
		}
	}

	private static void AddSampledAnchorPoints(ICollection<Vector3> anchors, Vector3[] points, int maxPoints)
	{
		if (points.Length == 0 || maxPoints <= 0)
		{
			return;
		}

		int stride = Mathf.Max(1, Mathf.CeilToInt(points.Length / (float)maxPoints));
		for (int pointIndex = 0; pointIndex < points.Length; pointIndex += stride)
		{
			Vector3 point = points[pointIndex];
			if (point.LengthSquared() < 0.00001f)
			{
				continue;
			}

			anchors.Add(point);
		}
	}

	private void AddMeshBoundsAnchors(ICollection<Vector3> anchors, MeshInstance3D? meshInstance)
	{
		if (meshInstance?.Mesh == null || !IsInstanceValid(meshInstance))
		{
			return;
		}

		Aabb bounds = meshInstance.Mesh.GetAabb();
		if (bounds.Size.LengthSquared() < 0.00001f)
		{
			return;
		}

		Vector3 min = bounds.Position;
		Vector3 max = bounds.Position + bounds.Size;
		Vector3 center = (min + max) * 0.5f;
		Vector3[] localPoints =
		[
			new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
			new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
			new(center.X, center.Y, min.Z), new(center.X, center.Y, max.Z), new(center.X, min.Y, center.Z),
			new(center.X, max.Y, center.Z), new(min.X, center.Y, center.Z), new(max.X, center.Y, center.Z),
		];

		for (int pointIndex = 0; pointIndex < localPoints.Length; pointIndex++)
		{
			anchors.Add(ToLocal(meshInstance.ToGlobal(localPoints[pointIndex])));
		}
	}

	private void UpdateFourDLineAndPoints(Func<Point4D, float, Point4D> rotate, float wCamera, float zCamera, float scale, float baseSize)
	{
		if (_pointMultiMesh == null)
		{
			return;
		}

		for (int i = 0; i < _points4D.Length; i++)
		{
			_projected[i] = Project4DTo3D(rotate(_points4D[i], _time), wCamera, zCamera, scale);
		}

		for (int i = 0; i < _lineIndices.Length; i++)
		{
			_lineBuffer[i] = _projected[_lineIndices[i]];
		}

		UpdateLineMesh(_lineBuffer);

		int pointIndex = 0;
		for (int i = 0; i < _projected.Length && pointIndex < _pointMultiMesh.InstanceCount; i += _pointEvery)
		{
			Vector3 projected = _projected[i];
			float size = baseSize * (0.86f + Mathf.Clamp(projected.Z + 0.8f, 0f, 1.6f) * 0.28f);
			SetMultimeshPoint(_pointMultiMesh, pointIndex++, projected, size);
		}
	}

	private void BuildImpossibleCube()
	{
		List<Vector3> vertices =
		[
			new(-0.52f, -0.52f, -0.52f), new(0.52f, -0.52f, -0.52f), new(0.52f, 0.52f, -0.52f), new(-0.52f, 0.52f, -0.52f),
			new(-0.52f, -0.52f, 0.52f), new(0.52f, -0.52f, 0.52f), new(0.52f, 0.52f, 0.52f), new(-0.52f, 0.52f, 0.52f),
		];
		int[] edges =
		[
			0, 1, 1, 2, 2, 3, 3, 0,
			4, 5, 5, 6, 6, 7, 7, 4,
			0, 4, 1, 5, 2, 6, 3, 7,
			4, 2, 2, 7, 7, 4,
		];

		Vector3[] linePoints = new Vector3[edges.Length];
		for (int i = 0; i < edges.Length; i++)
		{
			linePoints[i] = vertices[edges[i]];
		}

		_edgeMesh = AddMesh("LegacyImpossibleCubeBars", CreateRawLineMesh(linePoints), CreateMaterial(_tint.Lightened(0.15f), _tint.Lightened(0.25f), 1.0f, true, true));
		Rotation = new Vector3(0.40f, 0f, 0f);
	}

	private void BuildBoysSurface()
	{
		_boysGrid = new Vector3[(BoysU + 1) * (BoysV + 1)];
		PopulateBoysSurfaceGrid();
		ArrayMesh surfaceMesh = CreateBoysSurfaceSurfaceMesh(_boysGrid);
		_bodyMesh = AddMesh("LegacyBoysBody", surfaceMesh, ShapeVisualFactory.CreateOpaqueNeutralCelShadedMaterial(doubleSided: true));
		_outlineMesh = null;
		List<Vector3> emptyLines = new();
		AppendBoysLineTopology(emptyLines);
		_lineBuffer = emptyLines.ToArray();
		_edgeMesh = AddMesh("LegacyBoysGrid", CreateRawLineMesh(_lineBuffer), CreateMaterial(_tint.Lightened(0.18f), _tint.Lightened(0.25f), 0.82f, true, true));
		int pointCount = ((BoysU / 2) + 1) * ((BoysV / 2) + 1);
		_pointCloud = CreatePointCloud("LegacyBoysPoints", pointCount, 0.016f, _tint.Lightened(0.25f));
		_pointMultiMesh = _pointCloud.Multimesh;
	}

	private void UpdateBoysSurface()
	{
		PopulateBoysSurfaceGrid();

		int write = 0;
		for (int i = 0; i <= BoysU; i += 2)
		{
			for (int j = 0; j < BoysV; j++)
			{
				_lineBuffer[write++] = _boysGrid[BoysIndex(i, j)];
				_lineBuffer[write++] = _boysGrid[BoysIndex(i, j + 1)];
			}
		}

		for (int j = 0; j <= BoysV; j += 3)
		{
			for (int i = 0; i < BoysU; i++)
			{
				_lineBuffer[write++] = _boysGrid[BoysIndex(i, j)];
				_lineBuffer[write++] = _boysGrid[BoysIndex(i + 1, j)];
			}
		}

		ArrayMesh surfaceMesh = CreateBoysSurfaceSurfaceMesh(_boysGrid);
		if (_bodyMesh != null)
		{
			_bodyMesh.Mesh = surfaceMesh;
		}

		UpdateLineMesh(_lineBuffer);

		if (_pointMultiMesh == null)
		{
			return;
		}

		int pointIndex = 0;
		for (int i = 0; i <= BoysU; i += 2)
		{
			for (int j = 0; j <= BoysV; j += 2)
			{
				Vector3 pos = _boysGrid[BoysIndex(i, j)];
				float size = 0.012f + Mathf.Clamp(pos.Z + 0.5f, 0f, 1.2f) * 0.006f;
				SetMultimeshPoint(_pointMultiMesh, pointIndex++, pos, size);
			}
		}
	}

	private void PopulateBoysSurfaceGrid()
	{
		float t = _time * 0.24f;
		for (int i = 0; i <= BoysU; i++)
		{
			float u = i / (float)BoysU * Mathf.Pi;
			for (int j = 0; j <= BoysV; j++)
			{
				float v = j / (float)BoysV * Mathf.Tau;
				float lobe = 1f + 0.3f * Mathf.Sin(3f * v) * Mathf.Sin(u);
				float flip = Mathf.Cos(v + Mathf.Sin(t * 1.2f) * 0.8f);
				float x = Mathf.Sin(u) * Mathf.Cos(v) * lobe;
				float y = Mathf.Sin(u) * Mathf.Sin(v) * lobe;
				float z = Mathf.Cos(u) * flip;

				float rx = x * Mathf.Cos(t) - z * Mathf.Sin(t);
				float rz = x * Mathf.Sin(t) + z * Mathf.Cos(t);
				float ry = y * Mathf.Cos(0.3f) - rz * Mathf.Sin(0.3f);
				float rz2 = y * Mathf.Sin(0.3f) + rz * Mathf.Cos(0.3f);
				_boysGrid[BoysIndex(i, j)] = new Vector3(rx, ry, rz2) * 0.42f;
			}
		}
	}

	private static int BoysIndex(int u, int v) => u * (BoysV + 1) + v;

	private static ArrayMesh CreateBoysSurfaceSurfaceMesh(Vector3[] grid)
	{
		List<Vector3> triangles = new(BoysU * BoysV * 6);
		for (int i = 0; i < BoysU; i++)
		{
			for (int j = 0; j < BoysV; j++)
			{
				Vector3 a = grid[BoysIndex(i, j)];
				Vector3 b = grid[BoysIndex(i + 1, j)];
				Vector3 c = grid[BoysIndex(i + 1, j + 1)];
				Vector3 d = grid[BoysIndex(i, j + 1)];

				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
				triangles.Add(a);
				triangles.Add(c);
				triangles.Add(d);
			}
		}

		return CreateRawTriangleMesh(triangles.ToArray());
	}

	private static void AppendBoysLineTopology(List<Vector3> lines)
	{
		for (int i = 0; i <= BoysU; i += 2)
		{
			for (int j = 0; j < BoysV; j++)
			{
				lines.Add(Vector3.Zero);
				lines.Add(Vector3.Zero);
			}
		}

		for (int j = 0; j <= BoysV; j += 3)
		{
			for (int i = 0; i < BoysU; i++)
			{
				lines.Add(Vector3.Zero);
				lines.Add(Vector3.Zero);
			}
		}
	}

	private static Point4D RotateTesseractLike(Point4D point, float t)
	{
		float x = point.X;
		float y = point.Y;
		float z = point.Z;
		float w = point.W;
		RotatePair(ref x, ref w, t * 0.9f);
		RotatePair(ref y, ref z, t * 1.1f);
		RotatePair(ref x, ref y, t * 0.7f);
		return new Point4D(x, y, z, w);
	}

	private static Point4D RotateHyperphere(Point4D point, float t)
	{
		float x = point.X;
		float y = point.Y;
		float z = point.Z;
		float w = point.W;
		RotatePair(ref x, ref w, t * 0.6f);
		RotatePair(ref y, ref z, t * 0.9f);
		RotatePair(ref x, ref y, t * 0.4f);
		return new Point4D(x, y, z, w);
	}

	private static Point4D RotatePentachoron(Point4D point, float t)
	{
		float x = point.X;
		float y = point.Y;
		float z = point.Z;
		float w = point.W;
		RotatePair(ref x, ref w, t * 0.8f);
		RotatePair(ref y, ref z, t * 1.1f);
		RotatePair(ref x, ref y, t * 0.6f);
		RotatePair(ref z, ref w, t * 0.9f);
		return new Point4D(x, y, z, w);
	}

	private static Point4D RotateHypercone(Point4D point, float t)
	{
		float x = point.X;
		float y = point.Y;
		float z = point.Z;
		float w = point.W;
		RotatePair(ref x, ref w, t * 0.7f);
		RotatePair(ref y, ref z, t * 0.5f);
		RotatePair(ref x, ref y, t * 0.3f);
		return new Point4D(x, y, z, w);
	}

	private static Point4D RotateKlein(Point4D point, float t)
	{
		float x = point.X;
		float y = point.Y;
		float z = point.Z;
		float w = point.W;
		RotatePair(ref x, ref w, t * 0.6f);
		RotatePair(ref y, ref z, t * 0.4f);
		RotatePair(ref x, ref y, t * 0.3f);
		return new Point4D(x, y, z, w);
	}

	private static void RotatePair(ref float a, ref float b, float t)
	{
		float cos = Mathf.Cos(t);
		float sin = Mathf.Sin(t);
		float na = a * cos - b * sin;
		float nb = a * sin + b * cos;
		a = na;
		b = nb;
	}

	private static Vector3 Project4DTo3D(Point4D point, float wCamera, float zCamera, float scale)
	{
		float wFactor = wCamera / (wCamera - point.W);
		float x3 = point.X * wFactor;
		float y3 = point.Y * wFactor;
		float z3 = point.Z * wFactor;
		float zFactor = zCamera / (zCamera - z3);
		return new Vector3(x3 * zFactor * scale, y3 * zFactor * scale, z3 * zFactor * scale);
	}

	private static List<Vector3> CreateSphereVertices(out List<int[]> faces)
	{
		const int parallels = 20;
		const int meridians = 24;
		List<Vector3> vertices = new();
		faces = new List<int[]>();

		for (int i = 0; i <= parallels; i++)
		{
			float theta = i / (float)parallels * Mathf.Pi;
			for (int j = 0; j < meridians; j++)
			{
				float phi = j / (float)meridians * Mathf.Tau;
				vertices.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi) * 0.52f, Mathf.Cos(theta) * 0.52f, Mathf.Sin(theta) * Mathf.Sin(phi) * 0.52f));
			}
		}

		for (int i = 0; i < parallels; i++)
		{
			for (int j = 0; j < meridians; j++)
			{
				int a = i * meridians + j;
				int b = i * meridians + ((j + 1) % meridians);
				int c = (i + 1) * meridians + ((j + 1) % meridians);
				int d = (i + 1) * meridians + j;
				faces.Add([a, b, c, d]);
			}
		}

		return vertices;
	}

	private static List<Vector3> CreateCubeVertices(out List<int[]> faces)
	{
		List<Vector3> vertices =
		[
			new(-0.52f, -0.52f, -0.52f), new(0.52f, -0.52f, -0.52f), new(0.52f, 0.52f, -0.52f), new(-0.52f, 0.52f, -0.52f),
			new(-0.52f, -0.52f, 0.52f), new(0.52f, -0.52f, 0.52f), new(0.52f, 0.52f, 0.52f), new(-0.52f, 0.52f, 0.52f),
		];
		faces =
		[
			[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [2, 3, 7, 6], [0, 3, 7, 4], [1, 2, 6, 5]
		];
		return vertices;
	}

	private static List<Vector3> CreatePyramidVertices(out List<int[]> faces)
	{
		List<Vector3> vertices =
		[
			new(0f, 0.62f, 0f), new(-0.52f, -0.31f, -0.52f), new(0.52f, -0.31f, -0.52f), new(0.52f, -0.31f, 0.52f), new(-0.52f, -0.31f, 0.52f)
		];
		faces = [[1, 2, 3, 4], [0, 1, 2], [0, 2, 3], [0, 3, 4], [0, 4, 1]];
		return vertices;
	}

	private static List<Vector3> CreateConeVertices(out List<int[]> faces)
	{
		const int circles = 30;
		const int pointsPerCircle = 24;
		List<Vector3> vertices = new();
		faces = new List<int[]>();

		for (int i = 0; i < circles; i++)
		{
			float t = i / (float)(circles - 1);
			float y = Mathf.Lerp(-0.52f, 0.52f, t);
			float radius = t * 0.56f;
			for (int j = 0; j < pointsPerCircle; j++)
			{
				float angle = j / (float)pointsPerCircle * Mathf.Tau;
				vertices.Add(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
			}
		}

		int apex = vertices.Count;
		vertices.Add(new Vector3(0f, -0.52f, 0f));

		for (int i = 0; i < circles - 1; i++)
		{
			for (int j = 0; j < pointsPerCircle; j++)
			{
				int a = i * pointsPerCircle + j;
				int b = i * pointsPerCircle + ((j + 1) % pointsPerCircle);
				int c = (i + 1) * pointsPerCircle + ((j + 1) % pointsPerCircle);
				int d = (i + 1) * pointsPerCircle + j;
				faces.Add([a, b, c, d]);
			}
		}

		for (int j = 0; j < pointsPerCircle; j++)
		{
			faces.Add([apex, j, (j + 1) % pointsPerCircle]);
		}

		return vertices;
	}

	private static Point4D[] CreateHyperpherePoints(int count)
	{
		Random random = new(1337);
		Point4D[] points = new Point4D[count];
		for (int i = 0; i < count; i++)
		{
			float x = RandomNormal(random);
			float y = RandomNormal(random);
			float z = RandomNormal(random);
			float w = RandomNormal(random);
			float len = Mathf.Sqrt(x * x + y * y + z * z + w * w);
			points[i] = new Point4D(x / len, y / len, z / len, w / len);
		}

		return points;
	}

	private static float RandomNormal(Random random)
	{
		double u = 0;
		double v = 0;
		while (u == 0) u = random.NextDouble();
		while (v == 0) v = random.NextDouble();
		return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(Math.Tau * v));
	}

	private static Point4D[] CreatePentachoronPoints()
	{
		return
		[
			new(1f, 1f, 1f, -1f),
			new(1f, -1f, -1f, -1f),
			new(-1f, 1f, -1f, -1f),
			new(-1f, -1f, 1f, -1f),
			new(0f, 0f, 0f, 1f),
		];
	}

	private static Point4D[] CreateHyperconePoints()
	{
		const int circles = 25;
		const int pointsPerCircle = 32;
		List<Point4D> points = new();
		for (int i = 0; i < circles; i++)
		{
			float t = i / (float)(circles - 1);
			float w = -1f + t * 2f;
			float radius = (1f - t) * 1.2f;
			for (int j = 0; j < pointsPerCircle; j++)
			{
				float angle = j / (float)pointsPerCircle * Mathf.Tau;
				points.Add(new Point4D(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f, w));
			}
		}

		points.Add(new Point4D(0f, 0f, 0f, 1f));
		return points.ToArray();
	}

	private static Point4D[] CreateMobiusPoints()
	{
		const int count = 300;
		Point4D[] points = new Point4D[count];
		for (int i = 0; i < count; i++)
		{
			float t = i / (float)(count - 1) * Mathf.Tau;
			points[i] = new Point4D(Mathf.Cos(t), Mathf.Sin(t), 0f, Mathf.Sin(t * 2f) * 0.5f);
		}

		return points;
	}

	private static Point4D[] CreateKleinPoints(out int[] lineIndices)
	{
		const int numU = 40;
		const int numV = 30;
		List<Point4D> points = new();
		List<int> lines = new();

		for (int i = 0; i <= numU; i++)
		{
			float u = i / (float)numU * Mathf.Tau;
			for (int j = 0; j <= numV; j++)
			{
				float v = j / (float)numV * Mathf.Tau;
				float r = 4f * (1f - Mathf.Cos(u) / 2f);
				float x = 6f * Mathf.Cos(u) * (1f + Mathf.Sin(u)) + r * Mathf.Cos(v + Mathf.Pi);
				float y = 16f * Mathf.Sin(u);
				float z = r * Mathf.Sin(v + Mathf.Pi);
				float w = 6f * Mathf.Cos(u) * (1f - Mathf.Sin(u));
				points.Add(new Point4D(x * 0.05f, y * 0.05f, z * 0.05f, w * 0.05f));
			}
		}

		for (int i = 0; i < numU; i++)
		{
			for (int j = 0; j < numV; j++)
			{
				int idx = i * (numV + 1) + j;
				lines.Add(idx);
				lines.Add(idx + 1);
				lines.Add(idx);
				lines.Add(idx + numV + 1);
			}
		}

		lineIndices = lines.ToArray();
		return points.ToArray();
	}

	private static int[] CreateCompleteLineIndices(int vertexCount)
	{
		List<int> indices = new();
		for (int i = 0; i < vertexCount; i++)
		{
			for (int j = i + 1; j < vertexCount; j++)
			{
				indices.Add(i);
				indices.Add(j);
			}
		}

		return indices.ToArray();
	}

	private static int[] CreatePairLineIndices(int vertexCount)
	{
		List<int> indices = new();
		for (int i = 0; i < vertexCount - 1; i++)
		{
			indices.Add(i);
			indices.Add(i + 1);
		}

		return indices.ToArray();
	}

	private static ArrayMesh CreateTriangleMesh(List<Vector3> vertices, List<int[]> faces)
	{
		List<Vector3> triangles = new();
		foreach (int[] face in faces)
		{
			for (int i = 1; i < face.Length - 1; i++)
			{
				triangles.Add(vertices[face[0]]);
				triangles.Add(vertices[face[i]]);
				triangles.Add(vertices[face[i + 1]]);
			}
		}

		return CreateRawTriangleMesh(triangles.ToArray());
	}

	private static ArrayMesh CreateLineMesh(List<Vector3> vertices, List<int[]> faces)
	{
		HashSet<(int A, int B)> seen = new();
		List<Vector3> lines = new();
		foreach (int[] face in faces)
		{
			for (int i = 0; i < face.Length; i++)
			{
				int a = face[i];
				int b = face[(i + 1) % face.Length];
				(int A, int B) key = a < b ? (a, b) : (b, a);
				if (!seen.Add(key))
				{
					continue;
				}

				lines.Add(vertices[a]);
				lines.Add(vertices[b]);
			}
		}

		return CreateRawLineMesh(lines.ToArray());
	}

	private static ArrayMesh CreateRawTriangleMesh(Vector3[] vertices)
	{
		Godot.Collections.Array arrays = new();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		ArrayMesh mesh = new();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static ArrayMesh CreateRawLineMesh(Vector3[] vertices)
	{
		Godot.Collections.Array arrays = new();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		ArrayMesh mesh = new();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		return mesh;
	}

	private static ArrayMesh CreateFlatPolygonMesh(int sides, float radius)
	{
		List<Vector3> triangles = new();
		int segments = sides == 48 ? 48 : sides;
		float start = sides == 4 ? Mathf.Pi * 0.25f : -Mathf.Pi * 0.5f;
		for (int i = 0; i < segments; i++)
		{
			float a0 = start + Mathf.Tau * i / segments;
			float a1 = start + Mathf.Tau * (i + 1) / segments;
			triangles.Add(Vector3.Zero);
			triangles.Add(new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0f));
			triangles.Add(new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0f));
		}

		return CreateRawTriangleMesh(triangles.ToArray());
	}

	private static ArrayMesh CreateFlatGlyphMesh(int sides, float radius)
	{
		if (sides == 48)
		{
			List<Vector3> rings = new();
			AddCircleLines(rings, radius * 0.40f, 64, 0f);
			AddCircleLines(rings, radius * 0.58f, 64, 0f);
			AddCircleLines(rings, radius * 0.76f, 64, 0f);
			return CreateRawLineMesh(rings.ToArray());
		}

		List<Vector3> lines = new();
		Vector3[] points = CreateFlatPolygonPoints(sides, radius * 0.82f);
		Vector3[] inner = CreateFlatPolygonPoints(sides, radius * 0.46f);
		for (int i = 0; i < points.Length; i++)
		{
			lines.Add(points[i]);
			lines.Add(points[(i + 1) % points.Length]);
			lines.Add(Vector3.Zero);
			lines.Add(points[i]);
			lines.Add(inner[i]);
			lines.Add(inner[(i + 1) % inner.Length]);
		}

		return CreateRawLineMesh(lines.ToArray());
	}

	private static Vector3[] CreateFlatPolygonPoints(int sides, float radius)
	{
		Vector3[] points = new Vector3[sides];
		float start = sides == 4 ? Mathf.Pi * 0.25f : -Mathf.Pi * 0.5f;
		for (int i = 0; i < sides; i++)
		{
			float a = start + Mathf.Tau * i / sides;
			points[i] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0.02f);
		}

		return points;
	}

	private static ArrayMesh CreateCircleLineMesh(float radius, int segments)
	{
		List<Vector3> lines = new();
		AddCircleLines(lines, radius, segments, 0.025f);
		return CreateRawLineMesh(lines.ToArray());
	}

	private static void AddCircleLines(List<Vector3> lines, float radius, int segments, float z)
	{
		for (int i = 0; i < segments; i++)
		{
			float a0 = Mathf.Tau * i / segments;
			float a1 = Mathf.Tau * (i + 1) / segments;
			lines.Add(new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, z));
			lines.Add(new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, z));
		}
	}

	private MeshInstance3D AddMesh(string name, Mesh mesh, Material material)
	{
		MeshInstance3D meshInstance = new()
		{
			Name = name,
			Mesh = mesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(meshInstance);
		return meshInstance;
	}

	private MultiMeshInstance3D CreatePointCloud(string name, int count, float baseSize, Color tint)
	{
		return CreatePointCloud(name, count, baseSize, CreateMaterial(tint, tint, 0.95f, true, true));
	}

	private MultiMeshInstance3D CreatePointCloud(string name, int count, float baseSize, Material material)
	{
		SphereMesh pointMesh = new()
		{
			Radius = 1f,
			Height = 2f,
			RadialSegments = 8,
			Rings = 4,
		};

		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = pointMesh,
			InstanceCount = count,
			VisibleInstanceCount = count,
		};

		MultiMeshInstance3D cloud = new()
		{
			Name = name,
			Multimesh = multiMesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(cloud);

		for (int i = 0; i < count; i++)
		{
			SetMultimeshPoint(multiMesh, i, Vector3.Zero, baseSize);
		}

		if (ReferenceEquals(_orbiters, null) && name.Contains("Orbiters", StringComparison.OrdinalIgnoreCase))
		{
			_orbiterMultiMesh = multiMesh;
		}

		return cloud;
	}

	private static void SetMultimeshPoint(MultiMesh multiMesh, int index, Vector3 position, float size)
	{
		Vector3 safePosition = RuntimeMath.FiniteOr(position, Vector3.Zero);
		float safeSize = RuntimeMath.FiniteOr(size, 0.01f);
		safeSize = Mathf.Clamp(safeSize, 0.001f, 100f);

		Basis basis = Basis.Identity.Scaled(Vector3.One * safeSize);
		Transform3D transform = new(basis, safePosition);
		if (!RuntimeMath.IsFinite(transform.Origin)
			|| !RuntimeMath.IsFinite(transform.Basis.X)
			|| !RuntimeMath.IsFinite(transform.Basis.Y)
			|| !RuntimeMath.IsFinite(transform.Basis.Z))
		{
			transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.01f), Vector3.Zero);
		}

		multiMesh.SetInstanceTransform(index, transform);
	}

	private void UpdateLineMesh(Vector3[] vertices)
	{
		if (_edgeMesh != null)
		{
			_edgeMesh.Mesh = CreateRawLineMesh(vertices);
		}
	}

	private static StandardMaterial3D CreateMaterial(Color albedo, Color emission, float emissionEnergy, bool transparent, bool unshaded)
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = albedo,
			EmissionEnabled = true,
			Emission = emission,
			EmissionEnergyMultiplier = emissionEnergy,
			Metallic = 0.05f,
			Roughness = 0.28f,
		};

		if (transparent || albedo.A < 1f)
		{
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
		}

		if (unshaded)
		{
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		}

		return material;
	}
}
