using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
	// Contact-only spatial index. Weapon broad phases retain the existing XZ grid.
	private int[] _contactHeads = Array.Empty<int>();
	private int[] _contactNext = Array.Empty<int>();
	private int[] _contactCellX = Array.Empty<int>();
	private int[] _contactCellY = Array.Empty<int>();
	private int[] _contactCellZ = Array.Empty<int>();
	private float _contactHorizontalCellSize = 1f;
	private float _contactVerticalCellSize = 1f;
	private float _maxContactHeight = 1f;

	private void BuildContactGrid()
	{
		int capacity = _enemies.Length;
		if (_contactNext.Length != capacity || _contactHeads.Length == 0)
		{
			int buckets = 512;
			while (buckets < capacity * 4) buckets <<= 1;
			_contactHeads = new int[buckets];
			_contactNext = new int[capacity];
			_contactCellX = new int[capacity];
			_contactCellY = new int[capacity];
			_contactCellZ = new int[capacity];
		}
		if (_contactPositions.Length != capacity) _contactPositions = new Vector3[capacity];
		_maxContactHeight = 0f;
		for (int slot = 0; slot < capacity; slot++)
		{
			ref Enemy body = ref _enemies[slot];
			if (body.Alive) _maxContactHeight = Mathf.Max(_maxContactHeight, body.Height);
		}
		_contactHorizontalCellSize = Mathf.Max(1f, _maxRadius * 2f);
		_contactVerticalCellSize = Mathf.Max(1f, _maxContactHeight);
		Array.Fill(_contactHeads, -1);
		for (int slot = 0; slot < capacity; slot++)
		{
			ref Enemy body = ref _enemies[slot];
			_contactPositions[slot] = body.Position;
			if (!body.Alive) { _contactNext[slot] = -1; continue; }
			int x = Mathf.FloorToInt(body.Position.X / _contactHorizontalCellSize);
			int y = Mathf.FloorToInt(body.Position.Y / _contactVerticalCellSize);
			int z = Mathf.FloorToInt(body.Position.Z / _contactHorizontalCellSize);
			_contactCellX[slot] = x;
			_contactCellY[slot] = y;
			_contactCellZ[slot] = z;
			int bucket = ContactBucket(x, y, z);
			_contactNext[slot] = _contactHeads[bucket];
			_contactHeads[bucket] = slot;
		}
	}

	// Hash collisions are intentional; callers must compare stored cell X/Y/Z.
	private int ContactBucket(int x, int y, int z)
	{
		uint hash = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u);
		return (int)(hash & (uint)(_contactHeads.Length - 1));
	}
}
