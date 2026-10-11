using System;
using System.Numerics;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    private ulong[] _aliveSlotWords = Array.Empty<ulong>();
    private int _highestLiveSlot = -1;
    internal int HighestLiveSlot => _highestLiveSlot;

    private void MarkSlotAlive(int slot)
    {
        _aliveSlotWords[slot >> 6] |= 1UL << (slot & 63);
        if (slot > _highestLiveSlot) _highestLiveSlot = slot;
    }

    private void MarkSlotDead(int slot)
    {
        _aliveSlotWords[slot >> 6] &= ~(1UL << (slot & 63));
        if (slot != _highestLiveSlot) return;
        _highestLiveSlot = -1;
        for (int word = slot >> 6; word >= 0; word--)
        {
            ulong bits = _aliveSlotWords[word];
            if (bits == 0) continue;
            _highestLiveSlot = (word << 6) + BitOperations.Log2(bits);
            break;
        }
    }

    // Snapshot restoration bypasses Spawn/death. Reconcile its slot bookkeeping once,
    // retaining the existing free-stack order when snapshot occupancy is unchanged.
    internal void RebuildAliveSlotBookkeeping()
    {
        int wordCount = (_enemies.Length + 63) >> 6;
        if (_aliveSlotWords.Length != wordCount) _aliveSlotWords = new ulong[wordCount];
        bool occupancyChanged = false;
        int liveCount = 0;
        _highestLiveSlot = -1;
        for (int word = 0; word < wordCount; word++)
        {
            ulong bits = 0;
            int end = Math.Min(_enemies.Length, (word + 1) << 6);
            for (int slot = word << 6; slot < end; slot++)
            {
                if (!_enemies[slot].Alive) continue;
                bits |= 1UL << (slot & 63);
                liveCount++;
                _highestLiveSlot = slot;
            }
            occupancyChanged |= bits != _aliveSlotWords[word];
            _aliveSlotWords[word] = bits;
        }
        ActiveCount = liveCount;
        if (occupancyChanged)
        {
            _freeCount = 0;
            for (int slot = _enemies.Length - 1; slot >= 0; slot--)
                if (!_enemies[slot].Alive) _free[_freeCount++] = slot;
        }
        _gridDirty = true;
    }

    internal AliveSlotEnumerable EnumerateAliveSlots() => new(_aliveSlotWords, _enemies.Length);

    internal readonly struct AliveSlotEnumerable
    {
        private readonly ulong[] _words;
        private readonly int _capacity;
        internal AliveSlotEnumerable(ulong[] words, int capacity) { _words = words; _capacity = capacity; }
        public AliveSlotEnumerator GetEnumerator() => new(_words, _capacity);
    }

    internal struct AliveSlotEnumerator
    {
        private readonly ulong[] _words;
        private readonly int _capacity;
        private int _next;
        public int Current { get; private set; }
        internal AliveSlotEnumerator(ulong[] words, int capacity)
        { _words = words; _capacity = capacity; _next = 0; Current = -1; }

        public bool MoveNext()
        {
            while (_next < _capacity)
            {
                int word = _next >> 6;
                // Read the current word again after each body. Callbacks can kill or
                // spawn later slots, just as in the original ascending array loop.
                ulong bits = _words[word] & (ulong.MaxValue << (_next & 63));
                if (bits != 0)
                {
                    Current = (word << 6) + BitOperations.TrailingZeroCount(bits);
                    _next = Current + 1;
                    return Current < _capacity;
                }
                _next = (word + 1) << 6;
            }
            return false;
        }
    }
}
