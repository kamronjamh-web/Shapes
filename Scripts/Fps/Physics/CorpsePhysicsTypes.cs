using Godot;

namespace SuperGatchaRebuild.Fps.Physics;

internal enum CorpseStaticShapeKind { Box, ConvexHull, TriangleMesh }

// Arrays are owned by this immutable snapshot. Capture on the main thread and do
// not mutate after ReplaceWorld. Triangle triples must use Box3D's CCW winding.
internal sealed record CorpseStaticShape(CorpseStaticShapeKind Kind, Vector3 Position,
    Quaternion Rotation, Vector3 Size, Vector3[] Vertices, float Friction = 1f, float Restitution = 0f);
internal sealed record CorpseWorldSnapshot(int Version, CorpseStaticShape[] Shapes);

internal readonly record struct CorpseSpawn(bool Pyramid, Vector3 Position, Quaternion Rotation,
    Vector3 Size, Vector3 LinearVelocity, Vector3 AngularVelocity, float LinearDamping = .25f,
    float AngularDamping = .45f, float Friction = .65f, float Restitution = .12f, float Mass = 1f);

internal readonly record struct CorpsePose(int Slot, uint Generation, int Epoch, Vector3 Position,
    Quaternion Rotation, Vector3 LinearVelocity, Vector3 AngularVelocity, bool Sleeping);

internal readonly record struct CorpseFrameInfo(int Epoch, long Sequence, double StepMs, double ReadMs,
    double PendingSeconds, double DroppedSeconds, int GeometryVersion, bool Running);
