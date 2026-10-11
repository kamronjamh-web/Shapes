using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Fps.Physics;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    private CorpseWorldSnapshot? _corpseWorldSnapshot;
    private int _corpseWorldSignature, _corpseWorldVersion;
    internal string CorpseWorldUnavailableReason { get; private set; } = "";

    // Capture Godot collision shapes on the scene thread. The worker receives only
    // copied values; no Node, Resource, RID or physics-space query crosses threads.
    internal CorpseWorldSnapshot? CaptureCorpseWorld()
    {
        if (!UsesFortifications || !IsInstanceValid(_arenaRoot))
        {
            CorpseWorldUnavailableReason = "Threaded corpses require the known fort collision world.";
            return null;
        }
        HashCode signature = new();
        signature.Add(_arenaRoot.GetInstanceId());
        signature.Add(_arenaRoot.GlobalTransform);
        signature.Add(FortSpatialIndexVersion);
        foreach (FpsDefenseGate gate in _defenseGates)
        {
            signature.Add(gate.GetInstanceId()); signature.Add(gate.IsAlive);
            signature.Add(gate.Dimensions); signature.Add(gate.GlobalTransform);
        }
        int key = signature.ToHashCode();
        if (_corpseWorldSnapshot != null && key == _corpseWorldSignature) return _corpseWorldSnapshot;
        try
        {
            List<CorpseStaticShape> shapes = new();
            CaptureCorpseStatics(_arenaRoot, shapes);
            foreach (FpsDefenseGate gate in _defenseGates)
                if (gate.IsAlive && !gate.IsQueuedForDeletion()) CaptureCorpseStatics(gate, shapes);
            if (shapes.Count == 0) throw new InvalidOperationException("Fort collision snapshot is empty.");
            _corpseWorldSnapshot = new CorpseWorldSnapshot(++_corpseWorldVersion, shapes.ToArray());
            _corpseWorldSignature = key;
            CorpseWorldUnavailableReason = "";
            return _corpseWorldSnapshot;
        }
        catch (Exception error)
        {
            _corpseWorldSnapshot = null;
            CorpseWorldUnavailableReason = error.Message;
            return null;
        }
    }

    private void CaptureCorpseStatics(Node node, List<CorpseStaticShape> result)
    {
        if (node.IsQueuedForDeletion() || node == _player || node == _defenseCore || node == CorpsePool) return;
        if (node is StaticBody3D body)
        {
            if (body is FpsDefenseGate gate && !gate.IsAlive) return;
            if ((body.CollisionLayer & 1) == 0 && body is not FpsDefenseGate) return;
            Godot.Collections.Array<Node> colliders = body.GetChildren();
            using Godot.Collections.Array collidersOwner = (Godot.Collections.Array)colliders;
            foreach (Node child in colliders)
            {
                if (child is not CollisionShape3D collider || collider.Shape == null) continue;
                // Gates defer collider toggles. Their alive state is authoritative
                // during the same breach/restore tick as the worker snapshot.
                if (collider.Disabled && body is not FpsDefenseGate) continue;
                Transform3D transform = collider.GlobalTransform;
                if (!transform.Origin.IsFinite() || !transform.Basis.IsFinite())
                    throw new InvalidOperationException("Nonfinite fort collision transform.");
                float friction = body.PhysicsMaterialOverride?.Friction ?? 1f;
                float bounce = body.PhysicsMaterialOverride?.Bounce ?? 0f;
                if (collider.Shape is BoxShape3D box)
                {
                    Vector3 scale = transform.Basis.Scale;
                    Basis rotation = transform.Basis.Orthonormalized();
                    if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0
                        || !(rotation * Basis.FromScale(scale)).IsEqualApprox(transform.Basis))
                        throw new InvalidOperationException("Unsupported scaled/sheared fort box.");
                    result.Add(new(CorpseStaticShapeKind.Box, transform.Origin, rotation.GetRotationQuaternion(),
                        box.Size * scale, Array.Empty<Vector3>(), friction, bounce));
                }
                else if (collider.Shape is ConvexPolygonShape3D hull)
                {
                    Vector3[] vertices = hull.Points;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = transform * vertices[i];
                    result.Add(new(CorpseStaticShapeKind.ConvexHull, Vector3.Zero, Quaternion.Identity,
                        Vector3.One, vertices, friction, bounce));
                }
                else if (collider.Shape is ConcavePolygonShape3D mesh)
                {
                    Vector3[] vertices = mesh.GetFaces();
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = transform * vertices[i];
                    // Godot fronts are clockwise; Box3D mesh fronts are CCW.
                    for (int i = 0; i + 2 < vertices.Length; i += 3)
                        (vertices[i + 1], vertices[i + 2]) = (vertices[i + 2], vertices[i + 1]);
                    result.Add(new(CorpseStaticShapeKind.TriangleMesh, Vector3.Zero, Quaternion.Identity,
                        Vector3.One, vertices, friction, bounce));
                }
                else throw new InvalidOperationException($"Unsupported fort collider {collider.Shape.GetClass()}.");
            }
            return;
        }
        Godot.Collections.Array<Node> children = node.GetChildren();
        using Godot.Collections.Array childrenOwner = (Godot.Collections.Array)children;
        foreach (Node child in children) CaptureCorpseStatics(child, result);
    }
}
