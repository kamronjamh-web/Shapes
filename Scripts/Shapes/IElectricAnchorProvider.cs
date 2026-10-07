using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Shapes;

public interface IElectricAnchorProvider
{
	void CollectElectricAnchorPoints(ICollection<Vector3> anchors);
}
